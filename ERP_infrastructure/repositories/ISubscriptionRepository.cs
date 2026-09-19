using ERP_domain.entities;

namespace ERP_infrastructure.repositories
{
    public interface ISubscriptionRepository : IGenericRepository<Subscription>
    {
        Task<List<Subscription>> GetAllWithDetailsAsync();
        Task<List<Subscription>> GetMemberSubscriptionsAsync(int memberId);
        Task<List<Subscription>> GetActiveSubscriptionsAsync();
        Task<Subscription?> GetSubscriptionWithDetailsAsync(int subscriptionId);
        Task<Subscription?> GetCurrentSubscriptionAsync(int memberId);
        Task<int> CountByPlanAsync(int planId);
        Task<int> ExpireOverdueAsync(DateTime asOfUtc);
    }
}
