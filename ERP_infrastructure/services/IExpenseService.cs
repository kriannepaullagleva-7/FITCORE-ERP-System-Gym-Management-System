using ERP_infrastructure.repositories;
using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    /// <summary>An expense flattened with everything a list needs to read without a join.</summary>
    public class ExpenseView
    {
        public int ExpenseId { get; set; }
        public string Category { get; set; } = "";

        /// <summary>The broad grouping an income statement shows, from the category.</summary>
        public string CategoryGroup { get; set; } = "";

        public string Description { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime ExpenseDate { get; set; }
        public string PaymentMethod { get; set; } = "";
        public string ReferenceNo { get; set; } = "";
        public string Status { get; set; } = "";
        public string PaidTo { get; set; } = "";

        public int? SupplierId { get; set; }
        public string SupplierName { get; set; } = "";

        public int? AccountId { get; set; }
        public string AccountName { get; set; } = "";

        public int? BankAccountId { get; set; }
        public string BankAccountName { get; set; } = "";

        public int? RecordedByEmployeeId { get; set; }
        public string RecordedByName { get; set; } = "- unassigned -";

        /// <summary>The signed-in user who entered it.</summary>
        public string RecordedBy { get; set; } = "";

        /// <summary>Set once the expense has reached the ledger, so a screen can link to it.</summary>
        public int? JournalEntryId { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    public class ExpenseSummary
    {
        public int Count { get; set; }
        public decimal Total { get; set; }
        public decimal ThisMonth { get; set; }

        /// <summary>Recorded but not yet settled. Part of what the gym owes.</summary>
        public decimal Unpaid { get; set; }
        public int UnpaidCount { get; set; }

        public List<ExpenseCategoryTotal> ByCategory { get; set; } = new();

        /// <summary>The same totals rolled up to the groups an income statement shows.</summary>
        public List<CategorySlice> ByGroup { get; set; } = new();

        public List<TrendPoint> ByMonth { get; set; } = new();
    }

    /// <summary>
    /// Operating expenses: rent, utilities, repairs, fees - money out that is neither payroll
    /// nor stock.
    ///
    /// Note what this is not for. Stock bought for resale is a <see cref="Purchase"/>: goods
    /// acquired to sell are an asset until they are sold, and recording a delivery here would
    /// make the month it arrived look like a loss and the month it sold look like a windfall.
    /// </summary>
    public interface IExpenseService
    {
        /// <summary>The categories the UI offers and the service accepts.</summary>
        static IReadOnlyList<string> Categories => ExpenseCategories.All;

        Task<List<ExpenseView>> GetExpensesAsync(
            DateTime? fromUtc = null, DateTime? toUtc = null,
            string? category = null, string? status = null);

        Task<ExpenseView?> GetExpenseByIdAsync(int id);

        Task<ExpenseSummary> GetSummaryAsync(DateTime? fromUtc = null, DateTime? toUtc = null);

        Task<ExpenseView> CreateExpenseAsync(
            string category, string description, decimal amount, DateTime expenseDate,
            string paymentMethod, string referenceNo, int? recordedByEmployeeId,
            string status = ExpenseStatuses.Paid, string paidTo = "",
            int? supplierId = null, int? accountId = null, int? bankAccountId = null);

        Task<ExpenseView?> UpdateExpenseAsync(
            int id, string category, string description, decimal amount, DateTime expenseDate,
            string paymentMethod, string referenceNo, int? recordedByEmployeeId,
            string status = ExpenseStatuses.Paid, string paidTo = "",
            int? supplierId = null, int? accountId = null, int? bankAccountId = null);

        /// <summary>
        /// Settles an expense that was recorded as owed, which clears it from payables and
        /// moves the money out of cash.
        /// </summary>
        Task<ExpenseView?> MarkPaidAsync(int id, string paymentMethod, int? bankAccountId);

        Task<bool> DeleteExpenseAsync(int id);
    }
}
