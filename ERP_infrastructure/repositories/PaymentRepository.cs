using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.repositories
{
    public class PaymentRepository : GenericRepository<Payment>, IPaymentRepository
    {
        public PaymentRepository(TenantErpDbContext context) : base(context) { }

        private IQueryable<Payment> WithDetails() => _dbSet
            .Include(p => p.Member)
            .Include(p => p.Subscription)
                .ThenInclude(s => s!.Plan)
            .Include(p => p.Sale);

        public async Task<List<Payment>> GetAllWithDetailsAsync()
        {
            return await WithDetails()
                .OrderByDescending(p => p.PaymentDate)
                .ThenByDescending(p => p.PaymentId)
                .ToListAsync();
        }

        public async Task<Payment?> GetWithDetailsAsync(int paymentId)
        {
            return await WithDetails().FirstOrDefaultAsync(p => p.PaymentId == paymentId);
        }

        public async Task<List<Payment>> GetMemberPaymentsAsync(int memberId)
        {
            return await WithDetails()
                .Where(p => p.MemberId == memberId)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();
        }

        public async Task<List<Payment>> GetSubscriptionPaymentsAsync(int subscriptionId)
        {
            return await WithDetails()
                .Where(p => p.SubscriptionId == subscriptionId)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();
        }

        public async Task<List<Payment>> GetSalePaymentsAsync(int saleId)
        {
            return await WithDetails()
                .Where(p => p.SaleId == saleId)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();
        }

        public async Task<List<Payment>> GetInRangeAsync(DateTime fromUtc, DateTime toUtc)
        {
            return await WithDetails()
                .Where(p => p.PaymentDate >= fromUtc && p.PaymentDate < toUtc)
                .OrderByDescending(p => p.PaymentDate)
                .ThenByDescending(p => p.PaymentId)
                .ToListAsync();
        }

        // Only settled money counts towards what a sale has actually been paid.
        public async Task<decimal> GetSalePaidTotalAsync(int saleId)
        {
            return await _dbSet
                .Where(p => p.SaleId == saleId && p.Status == "Completed")
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;
        }

        public async Task<Dictionary<int, decimal>> GetPaidTotalsBySaleAsync(IEnumerable<int> saleIds)
        {
            var ids = saleIds.Distinct().ToList();

            if (ids.Count == 0)
            {
                return new Dictionary<int, decimal>();
            }

            var totals = await _dbSet
                .Where(p => p.SaleId != null
                         && ids.Contains(p.SaleId.Value)
                         && p.Status == "Completed")
                .GroupBy(p => p.SaleId!.Value)
                .Select(g => new { SaleId = g.Key, Total = g.Sum(p => p.Amount) })
                .ToListAsync();

            return totals.ToDictionary(x => x.SaleId, x => x.Total);
        }

        // Only settled money counts towards what a subscription has actually been paid.
        public async Task<decimal> GetSubscriptionPaidTotalAsync(int subscriptionId)
        {
            return await _dbSet
                .Where(p => p.SubscriptionId == subscriptionId && p.Status == "Completed")
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;
        }

        public async Task<Dictionary<int, decimal>> GetPaidTotalsBySubscriptionAsync(
            IEnumerable<int> subscriptionIds)
        {
            var ids = subscriptionIds.Distinct().ToList();

            if (ids.Count == 0)
            {
                return new Dictionary<int, decimal>();
            }

            // One grouped query for every subscription, rather than one query per row.
            var totals = await _dbSet
                .Where(p => p.SubscriptionId != null
                         && ids.Contains(p.SubscriptionId.Value)
                         && p.Status == "Completed")
                .GroupBy(p => p.SubscriptionId!.Value)
                .Select(g => new { SubscriptionId = g.Key, Total = g.Sum(p => p.Amount) })
                .ToListAsync();

            return totals.ToDictionary(x => x.SubscriptionId, x => x.Total);
        }
    }
}
