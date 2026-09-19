using ERP_infrastructure.repositories;

namespace ERP_infrastructure.services
{
    /// <summary>An expense flattened with the name of whoever recorded it.</summary>
    public class ExpenseView
    {
        public int ExpenseId { get; set; }
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime ExpenseDate { get; set; }
        public string PaymentMethod { get; set; } = "";
        public string ReferenceNo { get; set; } = "";
        public int? RecordedByEmployeeId { get; set; }
        public string RecordedByName { get; set; } = "- unassigned -";
    }

    public class ExpenseSummary
    {
        public int Count { get; set; }
        public decimal Total { get; set; }
        public decimal ThisMonth { get; set; }
        public List<ExpenseCategoryTotal> ByCategory { get; set; } = new();
    }

    public interface IExpenseService
    {
        /// <summary>The categories the UI offers and the service accepts.</summary>
        static readonly string[] Categories =
        {
            "Rent", "Utilities", "Equipment", "Supplies", "Maintenance", "Marketing", "Other"
        };

        Task<List<ExpenseView>> GetExpensesAsync(
            DateTime? fromUtc = null, DateTime? toUtc = null, string? category = null);

        Task<ExpenseView?> GetExpenseByIdAsync(int id);

        Task<ExpenseSummary> GetSummaryAsync(DateTime? fromUtc = null, DateTime? toUtc = null);

        Task<ExpenseView> CreateExpenseAsync(
            string category, string description, decimal amount, DateTime expenseDate,
            string paymentMethod, string referenceNo, int? recordedByEmployeeId);

        Task<ExpenseView?> UpdateExpenseAsync(
            int id, string category, string description, decimal amount, DateTime expenseDate,
            string paymentMethod, string referenceNo, int? recordedByEmployeeId);

        Task<bool> DeleteExpenseAsync(int id);
    }
}
