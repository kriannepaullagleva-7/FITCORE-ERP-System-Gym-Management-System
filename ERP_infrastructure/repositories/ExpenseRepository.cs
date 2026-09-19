using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.repositories
{
    public class ExpenseRepository : GenericRepository<Expense>, IExpenseRepository
    {
        public ExpenseRepository(TenantErpDbContext context) : base(context) { }

        private IQueryable<Expense> Filtered(DateTime? fromUtc, DateTime? toUtc, string? category = null)
        {
            var query = _dbSet.AsNoTracking().AsQueryable();

            if (fromUtc.HasValue) query = query.Where(e => e.ExpenseDate >= fromUtc.Value);
            if (toUtc.HasValue) query = query.Where(e => e.ExpenseDate <= toUtc.Value);

            if (!string.IsNullOrWhiteSpace(category))
            {
                var c = category.Trim();
                query = query.Where(e => e.Category == c);
            }

            return query;
        }

        public async Task<List<Expense>> GetAllWithEmployeeAsync(
            DateTime? fromUtc = null, DateTime? toUtc = null, string? category = null)
        {
            return await Filtered(fromUtc, toUtc, category)
                .Include(e => e.RecordedByEmployee)
                .OrderByDescending(e => e.ExpenseDate)
                .ThenByDescending(e => e.ExpenseId)
                .ToListAsync();
        }

        public async Task<Expense?> GetWithEmployeeAsync(int expenseId)
        {
            return await _dbSet
                .AsNoTracking()
                .Include(e => e.RecordedByEmployee)
                .FirstOrDefaultAsync(e => e.ExpenseId == expenseId);
        }

        public async Task<decimal> GetTotalAsync(DateTime? fromUtc = null, DateTime? toUtc = null)
        {
            return await Filtered(fromUtc, toUtc).SumAsync(e => (decimal?)e.Amount) ?? 0m;
        }

        public async Task<List<ExpenseCategoryTotal>> GetTotalsByCategoryAsync(
            DateTime? fromUtc = null, DateTime? toUtc = null)
        {
            return await Filtered(fromUtc, toUtc)
                .GroupBy(e => e.Category)
                .Select(g => new ExpenseCategoryTotal
                {
                    Category = g.Key,
                    Total = g.Sum(e => e.Amount),
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Total)
                .ToListAsync();
        }
    }
}
