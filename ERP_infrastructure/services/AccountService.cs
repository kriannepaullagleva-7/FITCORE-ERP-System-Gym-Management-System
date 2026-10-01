using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    public class AccountService : IAccountService
    {
        private readonly TenantErpDbContext _context;

        public AccountService(TenantErpDbContext context)
        {
            _context = context;
        }

        public async Task<List<AccountView>> GetAccountsAsync(
            string? accountType = null, bool includeInactive = true, bool withBalances = true)
        {
            await EnsureChartOfAccountsAsync();

            var query = _context.Accounts
                .AsNoTracking()
                .Include(a => a.ParentAccount)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(accountType))
            {
                var normalised = AccountTypes.Normalise(accountType)
                    ?? throw new ValidationException(
                        $"Account type must be one of: {string.Join(", ", AccountTypes.All)}.");

                query = query.Where(a => a.AccountType == normalised);
            }

            if (!includeInactive) query = query.Where(a => a.IsActive);

            var accounts = await query.OrderBy(a => a.AccountCode).ToListAsync();

            var totals = withBalances
                ? await LoadBalancesAsync()
                : new Dictionary<int, AccountTotals>();

            return accounts.Select(a =>
            {
                var view = ToView(a);

                if (totals.TryGetValue(a.AccountId, out var total))
                {
                    view.TotalDebit = total.Debit;
                    view.TotalCredit = total.Credit;
                    view.EntryCount = total.Count;
                    view.Balance = AccountTypes.BalanceOf(a.AccountType, total.Debit, total.Credit);
                }

                return view;
            }).ToList();
        }

        public async Task<AccountView?> GetAccountAsync(int accountId)
        {
            var account = await _context.Accounts
                .AsNoTracking()
                .Include(a => a.ParentAccount)
                .FirstOrDefaultAsync(a => a.AccountId == accountId);

            if (account is null) return null;

            var view = ToView(account);

            var total = await _context.JournalEntryLines
                .AsNoTracking()
                .Where(l => l.AccountId == accountId && l.Entry.Status == JournalStatuses.Posted)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Debit = g.Sum(l => l.Debit),
                    Credit = g.Sum(l => l.Credit),
                    Count = g.Count()
                })
                .FirstOrDefaultAsync();

            view.TotalDebit = total?.Debit ?? 0m;
            view.TotalCredit = total?.Credit ?? 0m;
            view.EntryCount = total?.Count ?? 0;
            view.Balance = AccountTypes.BalanceOf(account.AccountType, view.TotalDebit, view.TotalCredit);

            return view;
        }

        public async Task<int?> ResolveSystemAccountIdAsync(string systemKey)
        {
            if (string.IsNullOrWhiteSpace(systemKey)) return null;

            return await _context.Accounts
                .AsNoTracking()
                .Where(a => a.SystemKey == systemKey)
                .Select(a => (int?)a.AccountId)
                .FirstOrDefaultAsync();
        }

        public async Task<AccountView> CreateAccountAsync(
            string accountCode, string accountName, string accountType,
            string accountSubType, string description, int? parentAccountId)
        {
            var code = Clean(accountCode);
            var name = Clean(accountName);

            var type = AccountTypes.Normalise(accountType)
                ?? throw new ValidationException(
                    $"Account type must be one of: {string.Join(", ", AccountTypes.All)}.");

            if (code.Length == 0) throw new ValidationException("An account code is required.");
            if (name.Length == 0) throw new ValidationException("An account name is required.");

            if (await _context.Accounts.AnyAsync(a => a.AccountCode == code))
            {
                throw new ValidationException(
                    $"Account code {code} is already in use. Codes identify an account in every " +
                    "report, so they cannot be shared.");
            }

            await ValidateParentAsync(parentAccountId, type, null);

            var account = new Account
            {
                AccountCode = code,
                AccountName = name,
                AccountType = type,
                AccountSubType = Clean(accountSubType),
                Description = Clean(description),
                ParentAccountId = parentAccountId,

                // An account the operator created is theirs: nothing posts to it automatically
                // and they may delete it again while it is still empty.
                SystemKey = "",
                IsSystemAccount = false,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.Accounts.Add(account);
            await _context.SaveChangesAsync();

            return (await GetAccountAsync(account.AccountId))!;
        }

        public async Task<AccountView?> UpdateAccountAsync(
            int accountId, string accountCode, string accountName, string accountType,
            string accountSubType, string description, int? parentAccountId, bool isActive)
        {
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountId == accountId);
            if (account is null) return null;

            var code = Clean(accountCode);
            var name = Clean(accountName);

            var type = AccountTypes.Normalise(accountType)
                ?? throw new ValidationException(
                    $"Account type must be one of: {string.Join(", ", AccountTypes.All)}.");

            if (code.Length == 0) throw new ValidationException("An account code is required.");
            if (name.Length == 0) throw new ValidationException("An account name is required.");

            if (await _context.Accounts.AnyAsync(a => a.AccountCode == code && a.AccountId != accountId))
            {
                throw new ValidationException($"Account code {code} is already in use.");
            }

            // A system account may be renamed and recoded - how the chart reads belongs to the
            // operator. Its type may not change: the posting rules and every statement depend
            // on cash being an asset and revenue being revenue.
            if (account.IsSystemAccount && !string.Equals(account.AccountType, type, StringComparison.Ordinal))
            {
                throw new ValidationException(
                    $"{account.AccountName} is a system account and its type cannot be changed. " +
                    "Create your own account if you need a different one.");
            }

            if (account.IsSystemAccount && !isActive)
            {
                throw new ValidationException(
                    $"{account.AccountName} is used by automatic posting and cannot be deactivated. " +
                    "Deactivating it would stop sales, payments and pay runs reaching the ledger.");
            }

            await ValidateParentAsync(parentAccountId, type, accountId);

            account.AccountCode = code;
            account.AccountName = name;
            account.AccountType = type;
            account.AccountSubType = Clean(accountSubType);
            account.Description = Clean(description);
            account.ParentAccountId = parentAccountId;
            account.IsActive = isActive;

            await _context.SaveChangesAsync();

            return await GetAccountAsync(accountId);
        }

        public async Task<bool> DeleteAccountAsync(int accountId)
        {
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountId == accountId);
            if (account is null) return false;

            if (account.IsSystemAccount)
            {
                throw new ValidationException(
                    $"{account.AccountName} is part of the standard chart and cannot be deleted. " +
                    "Deactivate it instead if you do not use it.");
            }

            var postings = await _context.JournalEntryLines.CountAsync(l => l.AccountId == accountId);

            if (postings > 0)
            {
                throw new ValidationException(
                    $"{account.AccountName} has {postings} posting{(postings == 1 ? "" : "s")} " +
                    "against it. Deleting it would change every balance it appears in. " +
                    "Deactivate it instead - it will stop being offered without disturbing the history.");
            }

            if (await _context.Accounts.AnyAsync(a => a.ParentAccountId == accountId))
            {
                throw new ValidationException(
                    $"{account.AccountName} has accounts underneath it. Move or remove those first.");
            }

            if (await _context.BankAccounts.AnyAsync(b => b.LedgerAccountId == accountId))
            {
                throw new ValidationException(
                    $"{account.AccountName} backs a cash or bank account and cannot be deleted.");
            }

            _context.Accounts.Remove(account);
            await _context.SaveChangesAsync();

            return true;
        }

        /// <summary>
        /// Whether the chart has already been verified during this request.
        ///
        /// The service is registered per request, so this cannot leak between callers or
        /// tenants. Within one request it saves a read of the whole Accounts table on every
        /// finance call - and a KPI dashboard makes several. Nothing can remove a system
        /// account mid-request (deleting one is refused), so having checked once is enough.
        /// </summary>
        private bool _chartVerified;

        public async Task<int> EnsureChartOfAccountsAsync()
        {
            if (_chartVerified) return 0;

            var added = await SeedChartOfAccountsAsync();

            _chartVerified = true;
            return added;
        }

        private async Task<int> SeedChartOfAccountsAsync()
        {
            var existing = await _context.Accounts
                .Select(a => new { a.AccountId, a.AccountCode, a.SystemKey })
                .ToListAsync();

            var byKey = existing
                .Where(a => !string.IsNullOrWhiteSpace(a.SystemKey))
                .ToDictionary(a => a.SystemKey, StringComparer.OrdinalIgnoreCase);

            var byCode = existing
                .ToDictionary(a => a.AccountCode, StringComparer.OrdinalIgnoreCase);

            var added = 0;

            foreach (var seed in ChartOfAccounts.Standard)
            {
                // The key wins over the code: a tenant that renumbered "Rent" from 5200 to
                // 6100 still has a rent account, and seeding a second one would split their
                // expense history in two.
                if (byKey.ContainsKey(seed.SystemKey)) continue;
                if (byCode.ContainsKey(seed.Code)) continue;

                _context.Accounts.Add(new Account
                {
                    AccountCode = seed.Code,
                    AccountName = seed.Name,
                    AccountType = seed.Type,
                    AccountSubType = seed.SubType,
                    SystemKey = seed.SystemKey,
                    Description = seed.Description,
                    IsSystemAccount = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });

                added++;
            }

            if (added > 0) await _context.SaveChangesAsync();

            return added;
        }

        // ------------------------------------------------------------------ helpers

        private readonly record struct AccountTotals(decimal Debit, decimal Credit, int Count);

        /// <summary>
        /// Every account's posted totals in one grouped query rather than one query per
        /// account. Against a remote database the difference between one round trip and fifty
        /// is the entire cost of opening the chart of accounts.
        /// </summary>
        private async Task<Dictionary<int, AccountTotals>> LoadBalancesAsync()
        {
            var rows = await _context.JournalEntryLines
                .AsNoTracking()
                .Where(l => l.Entry.Status == JournalStatuses.Posted)
                .GroupBy(l => l.AccountId)
                .Select(g => new
                {
                    AccountId = g.Key,
                    Debit = g.Sum(l => l.Debit),
                    Credit = g.Sum(l => l.Credit),
                    Count = g.Count()
                })
                .ToListAsync();

            return rows.ToDictionary(
                r => r.AccountId,
                r => new AccountTotals(r.Debit, r.Credit, r.Count));
        }

        private async Task ValidateParentAsync(int? parentAccountId, string type, int? selfId)
        {
            if (!parentAccountId.HasValue) return;

            if (parentAccountId == selfId)
            {
                throw new ValidationException("An account cannot be its own parent.");
            }

            var parent = await _context.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.AccountId == parentAccountId.Value)
                ?? throw new ValidationException(
                    $"No account with id {parentAccountId.Value} exists to be the parent.");

            if (!string.Equals(parent.AccountType, type, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    $"An account can only sit under one of the same type. " +
                    $"{parent.AccountName} is {parent.AccountType.ToLowerInvariant()} " +
                    $"and this one is {type.ToLowerInvariant()}.");
            }
        }

        private static AccountView ToView(Account a) => new()
        {
            AccountId = a.AccountId,
            AccountCode = a.AccountCode,
            AccountName = a.AccountName,
            AccountType = a.AccountType,
            AccountSubType = a.AccountSubType,
            SystemKey = a.SystemKey,
            IsSystemAccount = a.IsSystemAccount,
            IsActive = a.IsActive,
            Description = a.Description,
            ParentAccountId = a.ParentAccountId,
            ParentAccountName = a.ParentAccount?.AccountName ?? "",
            IsDebitNormal = AccountTypes.IsDebitNormal(a.AccountType)
        };

        private static string Clean(string? value) => (value ?? "").Trim();
    }
}
