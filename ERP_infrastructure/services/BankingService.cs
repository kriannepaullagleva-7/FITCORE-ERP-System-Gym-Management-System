using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Cash and bank accounts, the movements through them, and reconciling those movements
    /// against the ledger.
    ///
    /// Every cash account is backed by a ledger account, so the balance shown here and the
    /// balance the trial balance reports are the same number arrived at two different ways.
    /// Reconciliation is the act of finding out why they differ when they do - which is the
    /// entire reason for keeping both.
    /// </summary>
    public interface IBankingService
    {
        Task<List<BankAccountView>> GetAccountsAsync(bool includeInactive = true);

        Task<BankAccountView?> GetAccountAsync(int bankAccountId);

        Task<BankAccountView> CreateAccountAsync(
            string accountName, string bankName, string accountNumber, string kind,
            decimal openingBalance, string notes);

        Task<BankAccountView?> UpdateAccountAsync(
            int bankAccountId, string accountName, string bankName, string accountNumber,
            string kind, string notes, bool isActive);

        Task<bool> DeleteAccountAsync(int bankAccountId);

        Task<List<BankTransactionView>> GetTransactionsAsync(
            int? bankAccountId = null, DateTime? fromUtc = null, DateTime? toUtc = null,
            bool? reconciled = null, int take = 500);

        Task<BankTransactionView> RecordTransactionAsync(
            int bankAccountId, DateTime transactionDate, string direction, decimal amount,
            string reference, string description);

        /// <summary>Marks a statement line as agreed against the ledger.</summary>
        Task<BankTransactionView?> SetReconciledAsync(int bankTransactionId, bool reconciled);

        /// <summary>Agrees several lines at once, which is how a statement is actually worked through.</summary>
        Task<int> ReconcileManyAsync(IEnumerable<int> bankTransactionIds);

        Task<ReconciliationView?> GetReconciliationAsync(
            int bankAccountId, DateTime? fromUtc, DateTime? toUtc);

        Task<bool> DeleteTransactionAsync(int bankTransactionId);
    }

    public class BankingService : IBankingService
    {
        private readonly TenantErpDbContext _context;
        private readonly IAccountService _accounts;
        private readonly ICurrentUserAccessor _actor;

        public BankingService(
            TenantErpDbContext context, IAccountService accounts, ICurrentUserAccessor actor)
        {
            _context = context;
            _accounts = accounts;
            _actor = actor;
        }

        // ------------------------------------------------------------------ accounts

        public async Task<List<BankAccountView>> GetAccountsAsync(bool includeInactive = true)
        {
            await _accounts.EnsureChartOfAccountsAsync();

            var query = _context.BankAccounts
                .AsNoTracking()
                .Include(a => a.LedgerAccount)
                .AsQueryable();

            if (!includeInactive) query = query.Where(a => a.IsActive);

            var accounts = await query.OrderBy(a => a.AccountName).ToListAsync();

            var ledgerIds = accounts.Select(a => a.LedgerAccountId).Distinct().ToList();

            var ledgerTotals = await _context.JournalEntryLines
                .AsNoTracking()
                .Where(l => ledgerIds.Contains(l.AccountId) && l.Entry.Status == JournalStatuses.Posted)
                .GroupBy(l => l.AccountId)
                .Select(g => new { AccountId = g.Key, Balance = g.Sum(l => l.Debit - l.Credit) })
                .ToListAsync();

            var unreconciled = await _context.BankTransactions
                .AsNoTracking()
                .Where(t => !t.IsReconciled)
                .GroupBy(t => t.BankAccountId)
                .Select(g => new { BankAccountId = g.Key, Count = g.Count() })
                .ToListAsync();

            return accounts.Select(a =>
            {
                var view = ToView(a);

                view.LedgerBalance =
                    ledgerTotals.FirstOrDefault(t => t.AccountId == a.LedgerAccountId)?.Balance ?? 0m;

                view.UnreconciledCount =
                    unreconciled.FirstOrDefault(u => u.BankAccountId == a.BankAccountId)?.Count ?? 0;

                return view;
            }).ToList();
        }

        public async Task<BankAccountView?> GetAccountAsync(int bankAccountId)
        {
            var all = await GetAccountsAsync();
            return all.FirstOrDefault(a => a.BankAccountId == bankAccountId);
        }

        public async Task<BankAccountView> CreateAccountAsync(
            string accountName, string bankName, string accountNumber, string kind,
            decimal openingBalance, string notes)
        {
            await _accounts.EnsureChartOfAccountsAsync();

            var name = Clean(accountName);
            if (name.Length == 0) throw new ValidationException("The account needs a name.");

            var normalisedKind = CashAccountKinds.All.FirstOrDefault(k =>
                string.Equals(k, Clean(kind), StringComparison.OrdinalIgnoreCase))
                ?? throw new ValidationException(
                    $"The kind must be one of: {string.Join(", ", CashAccountKinds.All)}.");

            if (await _context.BankAccounts.AnyAsync(a => a.AccountName == name))
            {
                throw new ValidationException($"There is already an account called '{name}'.");
            }

            // Cash on hand and everything bank-like are backed by their own ledger accounts,
            // which is what lets the cash flow statement tell the till apart from the bank.
            var ledgerKey = string.Equals(normalisedKind, CashAccountKinds.Cash, StringComparison.Ordinal)
                ? AccountKeys.Cash
                : AccountKeys.Bank;

            var ledgerAccountId = await _accounts.ResolveSystemAccountIdAsync(ledgerKey)
                ?? throw new ValidationException(
                    "The chart of accounts has no cash or bank account to back this one.");

            var account = new BankAccount
            {
                AccountName = name,
                BankName = Clean(bankName),
                AccountNumber = Clean(accountNumber),
                Kind = normalisedKind,
                LedgerAccountId = ledgerAccountId,
                OpeningBalance = openingBalance,
                CurrentBalance = openingBalance,
                Notes = Clean(notes),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.BankAccounts.Add(account);
            await _context.SaveChangesAsync();

            return (await GetAccountAsync(account.BankAccountId))!;
        }

        public async Task<BankAccountView?> UpdateAccountAsync(
            int bankAccountId, string accountName, string bankName, string accountNumber,
            string kind, string notes, bool isActive)
        {
            var account = await _context.BankAccounts
                .FirstOrDefaultAsync(a => a.BankAccountId == bankAccountId);

            if (account is null) return null;

            var name = Clean(accountName);
            if (name.Length == 0) throw new ValidationException("The account needs a name.");

            if (await _context.BankAccounts.AnyAsync(a =>
                    a.AccountName == name && a.BankAccountId != bankAccountId))
            {
                throw new ValidationException($"There is already an account called '{name}'.");
            }

            var normalisedKind = CashAccountKinds.All.FirstOrDefault(k =>
                string.Equals(k, Clean(kind), StringComparison.OrdinalIgnoreCase))
                ?? account.Kind;

            account.AccountName = name;
            account.BankName = Clean(bankName);
            account.AccountNumber = Clean(accountNumber);
            account.Kind = normalisedKind;
            account.Notes = Clean(notes);
            account.IsActive = isActive;

            await _context.SaveChangesAsync();

            return await GetAccountAsync(bankAccountId);
        }

        public async Task<bool> DeleteAccountAsync(int bankAccountId)
        {
            var account = await _context.BankAccounts
                .FirstOrDefaultAsync(a => a.BankAccountId == bankAccountId);

            if (account is null) return false;

            var movements = await _context.BankTransactions
                .CountAsync(t => t.BankAccountId == bankAccountId);

            if (movements > 0)
            {
                throw new ValidationException(
                    $"'{account.AccountName}' has {movements} movement" +
                    $"{(movements == 1 ? "" : "s")} recorded against it. Deactivate it instead - " +
                    "the history stays and the account stops being offered.");
            }

            if (await _context.Expenses.AnyAsync(e => e.BankAccountId == bankAccountId))
            {
                throw new ValidationException(
                    $"'{account.AccountName}' is named on recorded expenses and cannot be deleted.");
            }

            _context.BankAccounts.Remove(account);
            await _context.SaveChangesAsync();

            return true;
        }

        // ------------------------------------------------------------------ transactions

        public async Task<List<BankTransactionView>> GetTransactionsAsync(
            int? bankAccountId = null, DateTime? fromUtc = null, DateTime? toUtc = null,
            bool? reconciled = null, int take = 500)
        {
            var query = _context.BankTransactions
                .AsNoTracking()
                .Include(t => t.BankAccount)
                .AsQueryable();

            if (bankAccountId.HasValue) query = query.Where(t => t.BankAccountId == bankAccountId.Value);
            if (fromUtc.HasValue) query = query.Where(t => t.TransactionDate >= fromUtc.Value);
            if (toUtc.HasValue) query = query.Where(t => t.TransactionDate < toUtc.Value);
            if (reconciled.HasValue) query = query.Where(t => t.IsReconciled == reconciled.Value);

            var rows = await query
                .OrderByDescending(t => t.TransactionDate)
                .ThenByDescending(t => t.BankTransactionId)
                .Take(Math.Clamp(take, 1, 5000))
                .ToListAsync();

            return rows.Select(ToView).ToList();
        }

        public async Task<BankTransactionView> RecordTransactionAsync(
            int bankAccountId, DateTime transactionDate, string direction, decimal amount,
            string reference, string description)
        {
            var account = await _context.BankAccounts
                .FirstOrDefaultAsync(a => a.BankAccountId == bankAccountId)
                ?? throw new ValidationException($"No cash or bank account with id {bankAccountId} exists.");

            if (!account.IsActive)
            {
                throw new ValidationException($"'{account.AccountName}' is inactive.");
            }

            if (amount <= 0m)
            {
                throw new ValidationException(
                    "The amount must be greater than zero. Use the direction to say which way it went.");
            }

            var normalisedDirection = BankTransactionDirections.All.FirstOrDefault(d =>
                string.Equals(d, Clean(direction), StringComparison.OrdinalIgnoreCase))
                ?? throw new ValidationException("The direction must be In or Out.");

            var signed = string.Equals(normalisedDirection, BankTransactionDirections.Out,
                StringComparison.Ordinal) ? -amount : amount;

            var actor = _actor.Current;

            var transaction = new BankTransaction
            {
                BankAccountId = bankAccountId,
                TransactionDate = transactionDate == default ? DateTime.UtcNow : transactionDate,
                Direction = normalisedDirection,
                Amount = amount,
                BalanceAfter = account.CurrentBalance + signed,
                Reference = Clean(reference),
                Description = Clean(description),
                PerformedByUserId = actor.AppUserId,
                PerformedBy = actor.Username ?? "",
                CreatedAt = DateTime.UtcNow
            };

            account.CurrentBalance += signed;

            _context.BankTransactions.Add(transaction);
            await _context.SaveChangesAsync();

            transaction.BankAccount = account;
            return ToView(transaction);
        }

        public async Task<BankTransactionView?> SetReconciledAsync(int bankTransactionId, bool reconciled)
        {
            var transaction = await _context.BankTransactions
                .Include(t => t.BankAccount)
                .FirstOrDefaultAsync(t => t.BankTransactionId == bankTransactionId);

            if (transaction is null) return null;

            var actor = _actor.Current;

            transaction.IsReconciled = reconciled;
            transaction.ReconciledAt = reconciled ? DateTime.UtcNow : null;
            transaction.ReconciledByUserId = reconciled ? actor.AppUserId : null;
            transaction.ReconciledBy = reconciled ? actor.Username ?? "" : "";

            await _context.SaveChangesAsync();

            return ToView(transaction);
        }

        public async Task<int> ReconcileManyAsync(IEnumerable<int> bankTransactionIds)
        {
            var ids = (bankTransactionIds ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (ids.Count == 0) return 0;

            var rows = await _context.BankTransactions
                .Where(t => ids.Contains(t.BankTransactionId) && !t.IsReconciled)
                .ToListAsync();

            var actor = _actor.Current;
            var now = DateTime.UtcNow;

            foreach (var row in rows)
            {
                row.IsReconciled = true;
                row.ReconciledAt = now;
                row.ReconciledByUserId = actor.AppUserId;
                row.ReconciledBy = actor.Username ?? "";
            }

            await _context.SaveChangesAsync();

            return rows.Count;
        }

        public async Task<ReconciliationView?> GetReconciliationAsync(
            int bankAccountId, DateTime? fromUtc, DateTime? toUtc)
        {
            var account = await _context.BankAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.BankAccountId == bankAccountId);

            if (account is null) return null;

            var range = ReportRange.Resolve(fromUtc, toUtc);

            var transactions = await _context.BankTransactions
                .AsNoTracking()
                .Include(t => t.BankAccount)
                .Where(t => t.BankAccountId == bankAccountId &&
                            t.TransactionDate >= range.FromUtc &&
                            t.TransactionDate < range.ToUtc)
                .OrderBy(t => t.TransactionDate)
                .ToListAsync();

            var ledgerBalance = await _context.JournalEntryLines
                .AsNoTracking()
                .Where(l => l.AccountId == account.LedgerAccountId &&
                            l.Entry.Status == JournalStatuses.Posted &&
                            l.Entry.EntryDate < range.ToUtc)
                .SumAsync(l => (decimal?)(l.Debit - l.Credit)) ?? 0m;

            var view = new ReconciliationView
            {
                BankAccountId = account.BankAccountId,
                BankAccountName = account.AccountName,
                FromUtc = range.FromUtc,
                ToUtc = range.ToUtc,
                StatementBalance = account.CurrentBalance,
                LedgerBalance = ledgerBalance,
                ReconciledTotal = transactions.Where(t => t.IsReconciled).Sum(t => t.SignedAmount),
                UnreconciledTotal = transactions.Where(t => !t.IsReconciled).Sum(t => t.SignedAmount),
                ReconciledCount = transactions.Count(t => t.IsReconciled),
                UnreconciledCount = transactions.Count(t => !t.IsReconciled),
                Unreconciled = transactions.Where(t => !t.IsReconciled).Select(ToView).ToList()
            };

            view.Difference = view.StatementBalance - view.LedgerBalance;

            return view;
        }

        public async Task<bool> DeleteTransactionAsync(int bankTransactionId)
        {
            var transaction = await _context.BankTransactions
                .Include(t => t.BankAccount)
                .FirstOrDefaultAsync(t => t.BankTransactionId == bankTransactionId);

            if (transaction is null) return false;

            if (transaction.IsReconciled)
            {
                throw new ValidationException(
                    "This movement has been reconciled against the ledger. Un-reconcile it first " +
                    "if it really was entered in error.");
            }

            // Taking the money back out of the running balance, so the account does not drift
            // away from the sum of its movements.
            transaction.BankAccount.CurrentBalance -= transaction.SignedAmount;

            _context.BankTransactions.Remove(transaction);
            await _context.SaveChangesAsync();

            return true;
        }

        // ------------------------------------------------------------------ helpers

        private static BankAccountView ToView(BankAccount a) => new()
        {
            BankAccountId = a.BankAccountId,
            AccountName = a.AccountName,
            BankName = a.BankName,
            AccountNumber = a.AccountNumber,
            Kind = a.Kind,
            LedgerAccountId = a.LedgerAccountId,
            LedgerAccountName = a.LedgerAccount?.AccountName ?? "",
            OpeningBalance = a.OpeningBalance,
            CurrentBalance = a.CurrentBalance,
            IsActive = a.IsActive,
            Notes = a.Notes
        };

        private static BankTransactionView ToView(BankTransaction t) => new()
        {
            BankTransactionId = t.BankTransactionId,
            BankAccountId = t.BankAccountId,
            BankAccountName = t.BankAccount?.AccountName ?? "",
            TransactionDate = t.TransactionDate,
            Direction = t.Direction,
            Amount = t.Amount,
            SignedAmount = t.SignedAmount,
            BalanceAfter = t.BalanceAfter,
            Reference = t.Reference,
            Description = t.Description,
            IsReconciled = t.IsReconciled,
            ReconciledAt = t.ReconciledAt,
            ReconciledBy = t.ReconciledBy,
            JournalEntryId = t.JournalEntryId,
            PerformedBy = t.PerformedBy
        };

        private static string Clean(string? value) => (value ?? "").Trim();
    }
}
