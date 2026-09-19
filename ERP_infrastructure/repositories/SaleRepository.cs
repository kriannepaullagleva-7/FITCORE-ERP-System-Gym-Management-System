using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.repositories
{
    public class SaleRepository : GenericRepository<Sale>, ISaleRepository
    {
        public SaleRepository(TenantErpDbContext context) : base(context) { }

        private IQueryable<Sale> WithDetails() => _dbSet
            .Include(s => s.Member)
            .Include(s => s.CashierEmployee)
            .Include(s => s.Items)
                .ThenInclude(si => si.Product);

        public async Task<List<Sale>> GetAllWithDetailsAsync()
        {
            return await WithDetails()
                .OrderByDescending(s => s.SaleDate)
                .ThenByDescending(s => s.SaleId)
                .ToListAsync();
        }

        public async Task<List<Sale>> GetInRangeAsync(DateTime fromUtc, DateTime toUtc)
        {
            return await WithDetails()
                .Where(s => s.SaleDate >= fromUtc && s.SaleDate < toUtc)
                .OrderByDescending(s => s.SaleDate)
                .ThenByDescending(s => s.SaleId)
                .ToListAsync();
        }

        public async Task<List<Sale>> GetMemberSalesAsync(int memberId)
        {
            return await _dbSet
                .Where(s => s.MemberId == memberId)
                .Include(s => s.Member)
                .Include(s => s.Items)
                .ThenInclude(si => si.Product)
                .OrderByDescending(s => s.SaleDate)
                .ToListAsync();
        }

        public async Task<Sale?> GetSaleWithItemsAsync(int saleId)
        {
            return await WithDetails().FirstOrDefaultAsync(s => s.SaleId == saleId);
        }

        // A cancelled sale was reversed, so it does not count towards revenue.
        public async Task<decimal> GetTotalSalesAsync(DateTime? fromUtc = null)
        {
            var query = _dbSet.Where(s => s.Status != "Cancelled");
            if (fromUtc.HasValue)
                query = query.Where(s => s.SaleDate >= fromUtc.Value);

            return await query.SumAsync(s => (decimal?)s.TotalAmount) ?? 0m;
        }
    }
}
