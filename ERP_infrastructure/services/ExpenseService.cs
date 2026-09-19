using ERP_domain.entities;
using ERP_infrastructure.repositories;

namespace ERP_infrastructure.services
{
    public class ExpenseService : IExpenseService
    {
        private static readonly string[] AllowedCategories =
        {
            "Rent", "Utilities", "Equipment", "Supplies", "Maintenance", "Marketing", "Other"
        };

        private readonly IExpenseRepository _repository;
        private readonly IEmployeeRepository _employeeRepository;

        public ExpenseService(
            IExpenseRepository repository,
            IEmployeeRepository employeeRepository)
        {
            _repository = repository;
            _employeeRepository = employeeRepository;
        }

        private static ExpenseView ToView(Expense e) => new()
        {
            ExpenseId = e.ExpenseId,
            Category = e.Category,
            Description = e.Description,
            Amount = e.Amount,
            ExpenseDate = e.ExpenseDate,
            PaymentMethod = e.PaymentMethod,
            ReferenceNo = e.ReferenceNo,
            RecordedByEmployeeId = e.RecordedByEmployeeId,
            RecordedByName = e.RecordedByEmployee == null
                ? "- unassigned -"
                : $"{e.RecordedByEmployee.FirstName} {e.RecordedByEmployee.LastName}".Trim()
        };

        public async Task<List<ExpenseView>> GetExpensesAsync(
            DateTime? fromUtc = null, DateTime? toUtc = null, string? category = null)
        {
            var rows = await _repository.GetAllWithEmployeeAsync(fromUtc, toUtc, category);
            return rows.Select(ToView).ToList();
        }

        public async Task<ExpenseView?> GetExpenseByIdAsync(int id)
        {
            var row = await _repository.GetWithEmployeeAsync(id);
            return row == null ? null : ToView(row);
        }

        public async Task<ExpenseSummary> GetSummaryAsync(
            DateTime? fromUtc = null, DateTime? toUtc = null)
        {
            var now = DateTime.UtcNow;
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var rows = await _repository.GetAllWithEmployeeAsync(fromUtc, toUtc);

            return new ExpenseSummary
            {
                Count = rows.Count,
                Total = rows.Sum(r => r.Amount),
                ThisMonth = rows.Where(r => r.ExpenseDate >= monthStart).Sum(r => r.Amount),
                ByCategory = await _repository.GetTotalsByCategoryAsync(fromUtc, toUtc)
            };
        }

        public async Task<ExpenseView> CreateExpenseAsync(
            string category, string description, decimal amount, DateTime expenseDate,
            string paymentMethod, string referenceNo, int? recordedByEmployeeId)
        {
            var normalisedCategory = await ValidateAsync(
                category, description, amount, recordedByEmployeeId);

            var expense = new Expense
            {
                Category = normalisedCategory,
                Description = Clean(description),
                Amount = amount,
                ExpenseDate = expenseDate == default ? DateTime.UtcNow : expenseDate,
                PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? "Cash" : Clean(paymentMethod),
                ReferenceNo = Clean(referenceNo),
                RecordedByEmployeeId = recordedByEmployeeId,
                CreatedAt = DateTime.UtcNow
            };

            await _repository.AddAsync(expense);

            return (await GetExpenseByIdAsync(expense.ExpenseId))!;
        }

        public async Task<ExpenseView?> UpdateExpenseAsync(
            int id, string category, string description, decimal amount, DateTime expenseDate,
            string paymentMethod, string referenceNo, int? recordedByEmployeeId)
        {
            var expense = await _repository.GetByIdAsync(id);
            if (expense == null) return null;

            var normalisedCategory = await ValidateAsync(
                category, description, amount, recordedByEmployeeId);

            expense.Category = normalisedCategory;
            expense.Description = Clean(description);
            expense.Amount = amount;
            expense.ExpenseDate = expenseDate == default ? expense.ExpenseDate : expenseDate;
            expense.PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? "Cash" : Clean(paymentMethod);
            expense.ReferenceNo = Clean(referenceNo);
            expense.RecordedByEmployeeId = recordedByEmployeeId;

            await _repository.UpdateAsync(expense);

            return await GetExpenseByIdAsync(id);
        }

        public async Task<bool> DeleteExpenseAsync(int id)
        {
            return await _repository.DeleteAsync(id);
        }

        private async Task<string> ValidateAsync(
            string category, string description, decimal amount, int? recordedByEmployeeId)
        {
            var normalised = Clean(category);

            if (string.IsNullOrWhiteSpace(normalised))
            {
                normalised = "Other";
            }

            if (!AllowedCategories.Contains(normalised, StringComparer.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    $"Category must be one of: {string.Join(", ", AllowedCategories)}.");
            }

            // Store the canonical casing so grouping in reports stays consistent.
            normalised = AllowedCategories.First(
                c => string.Equals(c, normalised, StringComparison.OrdinalIgnoreCase));

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

            return normalised;
        }

        private static string Clean(string? value) => (value ?? string.Empty).Trim();
    }
}
