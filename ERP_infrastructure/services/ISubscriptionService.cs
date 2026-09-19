using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    public interface ISubscriptionService
    {
        Task<Subscription?> GetSubscriptionByIdAsync(int id);
        Task<List<Subscription>> GetAllSubscriptionsAsync();
        Task<List<Subscription>> GetMemberSubscriptionsAsync(int memberId);
        Task<List<Subscription>> GetActiveSubscriptionsAsync();
        Task<Subscription?> GetCurrentSubscriptionAsync(int memberId);
        Task<Subscription> CreateSubscriptionAsync(int memberId, int planId);
        Task<Subscription> CreateSubscriptionAsync(int memberId, int planId, DateTime startDate);
        Task<Subscription?> RenewSubscriptionAsync(int subscriptionId);
        Task<Subscription?> CancelSubscriptionAsync(int subscriptionId);
        Task<bool> DeleteSubscriptionAsync(int id);

        // Moves every past-due Active subscription to Expired and returns how many changed.
        Task<int> ExpireOverdueSubscriptionsAsync();
    }
}
