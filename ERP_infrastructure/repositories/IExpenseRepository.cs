using ERP_domain.entities;

namespace ERP_infrastructure.repositories
{
    /// <summary>Totals grouped by expense category, used by the expense report.</summary>
    public class ExpenseCategoryTotal
    {
        public string Category { get; set; } = "";
        public decimal Total { get; set; }
        public int Count { get; set; }
    }

    public interface IExpenseRepository : IGenericRepository<Expense>
    {
        Task<List<Expense>> GetAllWithEmployeeAsync(
            DateTime? fromUtc = null, DateTime? toUtc = null, string? category = null);

        Task<Expense?> GetWithEmployeeAsync(int expenseId);

        Task<decimal> GetTotalAsync(DateTime? fromUtc = null, DateTime? toUtc = null);

        Task<List<ExpenseCategoryTotal>> GetTotalsByCategoryAsync(
            DateTime? fromUtc = null, DateTime? toUtc = null);
    }
}
