using ERP_domain.entities;
using ERP_infrastructure.data;
using ERP_infrastructure.repositories;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    public class ExpenseService : IExpenseService
    {
        private static readonly string[] PaymentMethods =
            { "Cash", "Card", "Transfer", "Check", "GCash" };

        private readonly IExpenseRepository _repository;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly TenantErpDbContext _context;
        private readonly IAccountService _accounts;
        private readonly IFinancePostingService _finance;
        private readonly ICurrentUserAccessor _actor;

        public ExpenseService(
            IExpenseRepository repository,
            IEmployeeRepository employeeRepository,
            TenantErpDbContext context,
            IAccountService accounts,
            IFinancePostingService finance,
            ICurrentUserAccessor actor)
        {
            _repository = repository;
            _employeeRepository = employeeRepository;
            _context = context;
            _accounts = accounts;
            _finance = finance;
            _actor = actor;
        }

        // ------------------------------------------------------------------ reading

        public async Task<List<ExpenseView>> GetExpensesAsync(
            DateTime? fromUtc = null, DateTime? toUtc = null,
            string? category = null, string? status = null)
        {
            var query = _context.Expenses
                .AsNoTracking()
                .Include(e => e.RecordedByEmployee)
                .Include(e => e.Supplier)
                .Include(e => e.Account)
                .Include(e => e.BankAccount)
                .AsQueryable();

            if (fromUtc.HasValue) query = query.Where(e => e.ExpenseDate >= fromUtc.Value);
            if (toUtc.HasValue) query = query.Where(e => e.ExpenseDate <= toUtc.Value);

            if (!string.IsNullOrWhiteSpace(category))
            {
                var trimmed = category.Trim();
                query = query.Where(e => e.Category == trimmed);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                var trimmed = status.Trim();
                query = query.Where(e => e.Status == trimmed);
            }

            var rows = await query
                .OrderByDescending(e => e.ExpenseDate)
                .ThenByDescending(e => e.ExpenseId)
                .ToListAsync();

            return await WithPostingsAsync(rows);
        }

        public async Task<ExpenseView?> GetExpenseByIdAsync(int id)
        {
            var row = await _context.Expenses
                .AsNoTracking()
                .Include(e => e.RecordedByEmployee)
                .Include(e => e.Supplier)
                .Include(e => e.Account)
                .Include(e => e.BankAccount)
                .FirstOrDefaultAsync(e => e.ExpenseId == id);

            if (row is null) return null;

            return (await WithPostingsAsync(new List<Expense> { row })).FirstOrDefault();
        }

        public async Task<ExpenseSummary> GetSummaryAsync(
            DateTime? fromUtc = null, DateTime? toUtc = null)
        {
            var now = DateTime.UtcNow;
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var rows = await _context.Expenses
                .AsNoTracking()
                .Where(e => (!fromUtc.HasValue || e.ExpenseDate >= fromUtc.Value) &&
                            (!toUtc.HasValue || e.ExpenseDate <= toUtc.Value) &&
                            e.Status != ExpenseStatuses.Void)
                .Select(e => new { e.Amount, e.ExpenseDate, e.Category, e.Status })
                .ToListAsync();

            var summary = new ExpenseSummary
            {
                Count = rows.Count,
                Total = rows.Sum(r => r.Amount),
                ThisMonth = rows.Where(r => r.ExpenseDate >= monthStart).Sum(r => r.Amount),
                Unpaid = rows.Where(r => r.Status == ExpenseStatuses.Unpaid).Sum(r => r.Amount),
                UnpaidCount = rows.Count(r => r.Status == ExpenseStatuses.Unpaid),

                ByCategory = rows
                    .GroupBy(r => r.Category)
                    .Select(g => new ExpenseCategoryTotal
                    {
                        Category = g.Key,
                        Total = g.Sum(r => r.Amount),
                        Count = g.Count()
                    })
                    .OrderByDescending(x => x.Total)
                    .ToList(),

                // Twenty-four categories is the right level of detail for entering an expense
                // and the wrong level for reading a report, so they roll up.
                ByGroup = rows
                    .GroupBy(r => ExpenseCategories.GroupOf(r.Category))
                    .Select(g => new CategorySlice
                    {
                        Label = g.Key,
                        Value = g.Sum(r => r.Amount),
                        Count = g.Count()
                    })
                    .OrderByDescending(s => s.Value)
                    .ToList()
            };

            var trendStart = monthStart.AddMonths(-11);

            for (var offset = 0; offset < 12; offset++)
            {
                var month = trendStart.AddMonths(offset);

                summary.ByMonth.Add(new TrendPoint
                {
                    Label = month.ToString("MMM yy"),
                    Date = month,
                    Value = rows
                        .Where(r => r.ExpenseDate.Year == month.Year && r.ExpenseDate.Month == month.Month)
                        .Sum(r => r.Amount),
                    Count = rows.Count(r =>
                        r.ExpenseDate.Year == month.Year && r.ExpenseDate.Month == month.Month)
                });
            }

            return summary;
        }

        // ------------------------------------------------------------------ writing

        public async Task<ExpenseView> CreateExpenseAsync(
            string category, string description, decimal amount, DateTime expenseDate,
            string paymentMethod, string referenceNo, int? recordedByEmployeeId,
            string status = ExpenseStatuses.Paid, string paidTo = "",
            int? supplierId = null, int? accountId = null, int? bankAccountId = null)
        {
            var normalisedCategory = await ValidateAsync(
                category, description, amount, recordedByEmployeeId, supplierId, bankAccountId);

            var normalisedStatus = NormaliseStatus(status);

            var actor = _actor.Current;

            var expense = new Expense
            {
                Category = normalisedCategory,
                Description = Clean(description),
                Amount = amount,
                ExpenseDate = expenseDate == default ? DateTime.UtcNow : expenseDate,
                PaymentMethod = NormaliseMethod(paymentMethod),
                ReferenceNo = Clean(referenceNo),
                Status = normalisedStatus,
                PaidTo = Clean(paidTo),
                SupplierId = supplierId,
                BankAccountId = bankAccountId,
                RecordedByEmployeeId = recordedByEmployeeId,
                RecordedByUserId = actor.AppUserId,
                RecordedBy = actor.DisplayName,

                // Resolved from the category when the caller did not name one, so a tenant that
                // never opens the chart of accounts still gets a complete profit and loss.
                AccountId = accountId ?? await ResolveAccountAsync(normalisedCategory),

                CreatedAt = DateTime.UtcNow
            };

            await _repository.AddAsync(expense);

            await _finance.PostExpenseAsync(expense.ExpenseId);

            return (await GetExpenseByIdAsync(expense.ExpenseId))!;
        }

        public async Task<ExpenseView?> UpdateExpenseAsync(
            int id, string category, string description, decimal amount, DateTime expenseDate,
            string paymentMethod, string referenceNo, int? recordedByEmployeeId,
            string status = ExpenseStatuses.Paid, string paidTo = "",
            int? supplierId = null, int? accountId = null, int? bankAccountId = null)
        {
            var expense = await _repository.GetByIdAsync(id);
            if (expense == null) return null;

            var normalisedCategory = await ValidateAsync(
                category, description, amount, recordedByEmployeeId, supplierId, bankAccountId);

            var normalisedStatus = NormaliseStatus(status);

            expense.Category = normalisedCategory;
            expense.Description = Clean(description);
            expense.Amount = amount;
            expense.ExpenseDate = expenseDate == default ? expense.ExpenseDate : expenseDate;
            expense.PaymentMethod = NormaliseMethod(paymentMethod);
            expense.ReferenceNo = Clean(referenceNo);
            expense.Status = normalisedStatus;
            expense.PaidTo = Clean(paidTo);
            expense.SupplierId = supplierId;
            expense.BankAccountId = bankAccountId;
            expense.RecordedByEmployeeId = recordedByEmployeeId;
            expense.AccountId = accountId ?? await ResolveAccountAsync(normalisedCategory);

            await _repository.UpdateAsync(expense);

            // The amount, the date or the account may all have moved, so the old posting no
            // longer describes this expense. It is reversed and a fresh one written, which
            // leaves both on record rather than quietly editing history.
            await _finance.ReverseExpenseAsync(id, "Expense corrected");
            await _finance.PostExpenseAsync(id);

            return await GetExpenseByIdAsync(id);
        }

        public async Task<ExpenseView?> MarkPaidAsync(int id, string paymentMethod, int? bankAccountId)
        {
            var expense = await _repository.GetByIdAsync(id);
            if (expense == null) return null;

            if (string.Equals(expense.Status, ExpenseStatuses.Paid, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("This expense has already been settled.");
            }

            if (string.Equals(expense.Status, ExpenseStatuses.Void, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("A voided expense cannot be paid.");
            }

            if (bankAccountId.HasValue &&
                !await _context.BankAccounts.AnyAsync(a => a.BankAccountId == bankAccountId.Value))
            {
                throw new ValidationException("The selected cash or bank account does not exist.");
            }

            expense.Status = ExpenseStatuses.Paid;
            expense.PaymentMethod = NormaliseMethod(paymentMethod);
            expense.BankAccountId = bankAccountId ?? expense.BankAccountId;

            await _repository.UpdateAsync(expense);

            // It was posted as owed; now it is paid, and the payable has to be cleared rather
            // than the original posting edited.
            await _finance.ReverseExpenseAsync(id, "Expense settled");
            await _finance.PostExpenseAsync(id);

            return await GetExpenseByIdAsync(id);
        }

        public async Task<bool> DeleteExpenseAsync(int id)
        {
            var removed = await _repository.DeleteAsync(id);

            if (removed) await _finance.ReverseExpenseAsync(id, "Expense deleted");

            return removed;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Looks up which journal entry each expense produced, in one query rather than one per
        /// row, so the list can offer a link straight to the posting.
        /// </summary>
        private async Task<List<ExpenseView>> WithPostingsAsync(List<Expense> rows)
        {
            if (rows.Count == 0) return new List<ExpenseView>();

            var ids = rows.Select(r => r.ExpenseId.ToString()).ToList();

            var postings = await _context.JournalEntries
                .AsNoTracking()
                .Where(e => e.SourceEntityName == nameof(Expense) &&
                            e.SourceEntityId != null &&
                            ids.Contains(e.SourceEntityId) &&
                            e.Status == JournalStatuses.Posted)
                .Select(e => new { e.SourceEntityId, e.JournalEntryId })
                .ToListAsync();

            return rows.Select(e =>
            {
                var view = ToView(e);

                view.JournalEntryId = postings
                    .FirstOrDefault(p => p.SourceEntityId == e.ExpenseId.ToString())
                    ?.JournalEntryId;

                return view;
            }).ToList();
        }

        private async Task<int?> ResolveAccountAsync(string category)
        {
            var key = ChartOfAccounts.ExpenseAccountKey(category);

            return await _accounts.ResolveSystemAccountIdAsync(key)
                ?? await _accounts.ResolveSystemAccountIdAsync(AccountKeys.GeneralExpense);
        }

        private async Task<string> ValidateAsync(
            string category, string description, decimal amount,
            int? recordedByEmployeeId, int? supplierId, int? bankAccountId)
        {
            var normalised = ExpenseCategories.Normalise(category);

            if (normalised is null)
            {
                throw new ValidationException(
                    $"'{Clean(category)}' is not a recognised expense category. " +
                    $"Choose one of: {string.Join(", ", ExpenseCategories.All)}.");
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                throw new ValidationException("A description is required.");
            }

            if (amount <= 0m)
            {
                throw new ValidationException("The amount must be greater than zero.");
            }

            if (recordedByEmployeeId.HasValue &&
                await _employeeRepository.GetByIdAsync(recordedByEmployeeId.Value) is null)
            {
                throw new ValidationException(
                    $"No employee with id {recordedByEmployeeId.Value} exists.");
            }

            if (supplierId.HasValue &&
                !await _context.Suppliers.AnyAsync(s => s.SupplierId == supplierId.Value))
            {
                throw new ValidationException($"No supplier with id {supplierId.Value} exists.");
            }

            if (bankAccountId.HasValue &&
                !await _context.BankAccounts.AnyAsync(a => a.BankAccountId == bankAccountId.Value))
            {
                throw new ValidationException("The selected cash or bank account does not exist.");
            }

            return normalised;
        }

        private static string NormaliseStatus(string? status)
        {
            var trimmed = Clean(status);

            if (trimmed.Length == 0) return ExpenseStatuses.Paid;

            return ExpenseStatuses.All.FirstOrDefault(s =>
                string.Equals(s, trimmed, StringComparison.OrdinalIgnoreCase))
                ?? throw new ValidationException(
                    $"The status must be one of: {string.Join(", ", ExpenseStatuses.All)}.");
        }

        private static string NormaliseMethod(string? method) =>
            PaymentMethods.FirstOrDefault(m =>
                string.Equals(m, Clean(method), StringComparison.OrdinalIgnoreCase))
            ?? "Cash";

        private static ExpenseView ToView(Expense e) => new()
        {
            ExpenseId = e.ExpenseId,
            Category = e.Category,
            CategoryGroup = ExpenseCategories.GroupOf(e.Category),
            Description = e.Description,
            Amount = e.Amount,
            ExpenseDate = e.ExpenseDate,
            PaymentMethod = e.PaymentMethod,
            ReferenceNo = e.ReferenceNo,
            Status = e.Status,
            PaidTo = e.PaidTo,
            SupplierId = e.SupplierId,
            SupplierName = e.Supplier?.SupplierName ?? "",
            AccountId = e.AccountId,
            AccountName = e.Account is null
                ? ""
                : $"{e.Account.AccountCode} {e.Account.AccountName}".Trim(),
            BankAccountId = e.BankAccountId,
            BankAccountName = e.BankAccount?.AccountName ?? "",
            RecordedByEmployeeId = e.RecordedByEmployeeId,
            RecordedByName = e.RecordedByEmployee == null
                ? "- unassigned -"
                : $"{e.RecordedByEmployee.FirstName} {e.RecordedByEmployee.LastName}".Trim(),
            RecordedBy = string.IsNullOrWhiteSpace(e.RecordedBy) ? "—" : e.RecordedBy,
            CreatedAt = e.CreatedAt
        };

        private static string Clean(string? value) => (value ?? string.Empty).Trim();
    }
}
