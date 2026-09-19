using ERP_domain.entities;
using ERP_infrastructure.repositories;

namespace ERP_infrastructure.services
{
    public class SubscriptionService : ISubscriptionService
    {
        private readonly ISubscriptionRepository _subscriptionRepo;
        private readonly IGenericRepository<MembershipPlan> _planRepo;
        private readonly IMemberRepository _memberRepo;

        public SubscriptionService(
            ISubscriptionRepository subscriptionRepo,
            IGenericRepository<MembershipPlan> planRepo,
            IMemberRepository memberRepo)
        {
            _subscriptionRepo = subscriptionRepo;
            _planRepo = planRepo;
            _memberRepo = memberRepo;
        }

        public async Task<Subscription?> GetSubscriptionByIdAsync(int id)
        {
            return await _subscriptionRepo.GetSubscriptionWithDetailsAsync(id);
        }

        public async Task<List<Subscription>> GetAllSubscriptionsAsync()
        {
            return await _subscriptionRepo.GetAllWithDetailsAsync();
        }

        public async Task<List<Subscription>> GetMemberSubscriptionsAsync(int memberId)
        {
            return await _subscriptionRepo.GetMemberSubscriptionsAsync(memberId);
        }

        public async Task<List<Subscription>> GetActiveSubscriptionsAsync()
        {
            return await _subscriptionRepo.GetActiveSubscriptionsAsync();
        }

        public async Task<Subscription?> GetCurrentSubscriptionAsync(int memberId)
        {
            return await _subscriptionRepo.GetCurrentSubscriptionAsync(memberId);
        }

        public async Task<Subscription> CreateSubscriptionAsync(int memberId, int planId)
        {
            return await CreateSubscriptionAsync(memberId, planId, DateTime.UtcNow);
        }

        public async Task<Subscription> CreateSubscriptionAsync(int memberId, int planId, DateTime startDate)
        {
            var member = await _memberRepo.GetByIdAsync(memberId);
            if (member == null) throw new InvalidOperationException("Member not found");

            var plan = await _planRepo.GetByIdAsync(planId);
            if (plan == null) throw new InvalidOperationException("Plan not found");

            if (plan.DurationMonths <= 0)
                throw new InvalidOperationException(
                    $"Plan '{plan.PlanName}' has no duration set, so an end date cannot be worked out.");

            if (startDate == default) startDate = DateTime.UtcNow;

            var subscription = new Subscription
            {
                MemberId = memberId,
                PlanId = planId,
                StartDate = startDate,
                EndDate = startDate.AddMonths(plan.DurationMonths),
                Status = "Active",
                CreatedAt = DateTime.UtcNow
            };

            return await _subscriptionRepo.AddAsync(subscription);
        }

        public async Task<Subscription?> RenewSubscriptionAsync(int subscriptionId)
        {
            var subscription = await _subscriptionRepo.GetByIdAsync(subscriptionId);
            if (subscription == null) return null;

            var plan = await _planRepo.GetByIdAsync(subscription.PlanId);
            if (plan == null) return null;

            if (plan.DurationMonths <= 0)
                throw new InvalidOperationException(
                    $"Plan '{plan.PlanName}' has no duration set, so it cannot be renewed.");

            // Renewing early extends from the existing end date rather than losing paid-for days.
            var renewFrom = subscription.EndDate > DateTime.UtcNow ? subscription.EndDate : DateTime.UtcNow;

            subscription.StartDate = renewFrom;
            subscription.EndDate = renewFrom.AddMonths(plan.DurationMonths);
            subscription.Status = "Active";

            return await _subscriptionRepo.UpdateAsync(subscription);
        }

        public async Task<Subscription?> CancelSubscriptionAsync(int subscriptionId)
        {
            var subscription = await _subscriptionRepo.GetByIdAsync(subscriptionId);
            if (subscription == null) return null;

            subscription.Status = "Cancelled";
            return await _subscriptionRepo.UpdateAsync(subscription);
        }

        public async Task<bool> DeleteSubscriptionAsync(int id)
        {
            return await _subscriptionRepo.DeleteAsync(id);
        }

        public async Task<int> ExpireOverdueSubscriptionsAsync()
        {
            return await _subscriptionRepo.ExpireOverdueAsync(DateTime.UtcNow);
        }
    }
}
