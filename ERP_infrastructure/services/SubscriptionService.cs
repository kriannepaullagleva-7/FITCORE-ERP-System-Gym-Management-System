using ERP_domain.entities;
using ERP_infrastructure.repositories;

namespace ERP_infrastructure.services
{
    public class SubscriptionService : ISubscriptionService
    {
        private readonly ISubscriptionRepository _subscriptionRepo;
        private readonly IGenericRepository<MembershipPlan> _planRepo;
        private readonly IMemberRepository _memberRepo;
        private readonly IAuditService _audit;
        private readonly IFinancePostingService _finance;

        public SubscriptionService(
            ISubscriptionRepository subscriptionRepo,
            IGenericRepository<MembershipPlan> planRepo,
            IMemberRepository memberRepo,
            IAuditService audit,
            IFinancePostingService finance)
        {
            _subscriptionRepo = subscriptionRepo;
            _planRepo = planRepo;
            _memberRepo = memberRepo;
            _audit = audit;
            _finance = finance;
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

        public async Task<Subscription> CreateSubscriptionAsync(
            int? memberId, int planId, DateTime startDate,
            string? walkInName = null, string? walkInPhone = null)
        {
            // Exactly one of a real member or a walk-in name identifies whose membership this
            // is. A subscription is not required to name a Member record at all.
            Member? member = null;
            if (memberId.HasValue)
            {
                member = await _memberRepo.GetByIdAsync(memberId.Value);
                if (member == null) throw new InvalidOperationException("Member not found");
            }

            var plan = await _planRepo.GetByIdAsync(planId);
            if (plan == null) throw new InvalidOperationException("Plan not found");

            if (plan.DurationMonths <= 0)
                throw new InvalidOperationException(
                    $"Plan '{plan.PlanName}' has no duration set, so an end date cannot be worked out.");

            if (startDate == default) startDate = DateTime.UtcNow;

            var subscription = new Subscription
            {
                MemberId = memberId,
                WalkInName = member is null
                    ? (string.IsNullOrWhiteSpace(walkInName) ? "Walk-In" : walkInName.Trim())
                    : null,
                WalkInPhone = member is null ? walkInPhone?.Trim() : null,
                PlanId = planId,
                StartDate = startDate,
                EndDate = startDate.AddMonths(plan.DurationMonths),
                Status = "Active",
                CreatedAt = DateTime.UtcNow
            };

            var saved = await _subscriptionRepo.AddAsync(subscription);

            // Membership revenue is earned when the membership is sold, not when it is paid
            // for, so the member now owes for it. Their payment clears that receivable exactly
            // as a sale's does - which is what lets an unpaid sign-up show as money owed rather
            // than as nothing at all.
            await _finance.PostSubscriptionAsync(saved.SubscriptionId);

            return saved;
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
            var previousEndDate = subscription.EndDate;

            subscription.StartDate = renewFrom;
            subscription.EndDate = renewFrom.AddMonths(plan.DurationMonths);
            subscription.Status = "Active";

            var updated = await _subscriptionRepo.UpdateAsync(subscription);

            // The change-tracker sweep already records the raw field diff; this is what makes
            // that diff readable as "renewed" rather than three columns that happened to move
            // together - and it is the renewal history this membership otherwise has no row of
            // its own to keep, since renewing extends the existing subscription in place.
            await _audit.RecordAsync(
                AuditActions.SubscriptionRenewed, ErpModules.Membership, nameof(Subscription),
                subscriptionId.ToString(),
                $"Renewed from {previousEndDate:d MMM yyyy} to {updated.EndDate:d MMM yyyy} " +
                $"({plan.PlanName}).");

            // A renewal is the plan being sold again, so it earns revenue again. Keyed by the
            // term it starts, because renewing extends the subscription in place rather than
            // creating a row the ledger could key off.
            await _finance.PostSubscriptionRenewalAsync(subscriptionId, renewFrom);

            return updated;
        }

        public async Task<Subscription?> CancelSubscriptionAsync(int subscriptionId)
        {
            var subscription = await _subscriptionRepo.GetByIdAsync(subscriptionId);
            if (subscription == null) return null;

            subscription.Status = "Cancelled";
            var updated = await _subscriptionRepo.UpdateAsync(subscription);

            await _audit.RecordAsync(
                AuditActions.SubscriptionCancelled, ErpModules.Membership, nameof(Subscription),
                subscriptionId.ToString(),
                $"Cancelled — was due to expire {subscription.EndDate:d MMM yyyy}.");

            // The membership was never delivered, so the revenue and the receivable it raised
            // are undone. Any payment already taken keeps its own posting until it is refunded,
            // which is a separate decision.
            await _finance.ReverseSubscriptionAsync(
                subscriptionId, $"Membership #{subscriptionId} cancelled");

            return updated;
        }

        public async Task<bool> DeleteSubscriptionAsync(int id)
        {
            var removed = await _subscriptionRepo.DeleteAsync(id);

            if (removed)
            {
                await _finance.ReverseSubscriptionAsync(id, $"Membership #{id} deleted");
            }

            return removed;
        }

        public async Task<int> ExpireOverdueSubscriptionsAsync()
        {
            return await _subscriptionRepo.ExpireOverdueAsync(DateTime.UtcNow);
        }
    }
}
