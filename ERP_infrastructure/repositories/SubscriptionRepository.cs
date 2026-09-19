using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.repositories
{
    public class SubscriptionRepository : GenericRepository<Subscription>, ISubscriptionRepository
    {
        public SubscriptionRepository(TenantErpDbContext context) : base(context) { }

        public async Task<List<Subscription>> GetAllWithDetailsAsync()
        {
            return await _dbSet
                .Include(s => s.Member)
                .Include(s => s.Plan)
                .Include(s => s.Payments)
                .OrderByDescending(s => s.SubscriptionId)
                .ToListAsync();
        }

        public async Task<List<Subscription>> GetMemberSubscriptionsAsync(int memberId)
        {
            return await _dbSet
                .Where(s => s.MemberId == memberId)
                .Include(s => s.Member)
                .Include(s => s.Plan)
                .Include(s => s.Payments)
                .OrderByDescending(s => s.EndDate)
                .ToListAsync();
        }

        public async Task<List<Subscription>> GetActiveSubscriptionsAsync()
        {
            return await _dbSet
                .Where(s => s.Status == "Active")
                .Include(s => s.Member)
                .Include(s => s.Plan)
                .ToListAsync();
        }

        public async Task<Subscription?> GetSubscriptionWithDetailsAsync(int subscriptionId)
        {
            return await _dbSet
                .Include(s => s.Member)
                .Include(s => s.Plan)
                .Include(s => s.Payments)
                .FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId);
        }

        // The subscription that decides a membership status: the one running furthest
        // into the future, ignoring cancelled ones.
        public async Task<Subscription?> GetCurrentSubscriptionAsync(int memberId)
        {
            return await _dbSet
                .Where(s => s.MemberId == memberId && s.Status != "Cancelled")
                .Include(s => s.Plan)
                .OrderByDescending(s => s.EndDate)
                .FirstOrDefaultAsync();
        }

        // How many subscriptions reference a plan. A plan with any is protected by a
        // foreign key, so the UI checks this before offering to delete it.
        public async Task<int> CountByPlanAsync(int planId)
        {
            return await _dbSet.CountAsync(s => s.PlanId == planId);
        }

        // Flips still-Active subscriptions whose end date has passed over to Expired.
        public async Task<int> ExpireOverdueAsync(DateTime asOfUtc)
        {
            var overdue = await _dbSet
                .Where(s => s.Status == "Active" && s.EndDate < asOfUtc)
                .ToListAsync();

            if (overdue.Count == 0) return 0;

            foreach (var subscription in overdue)
                subscription.Status = "Expired";

            await SaveChangesAsync();
            return overdue.Count;
        }
    }
}
