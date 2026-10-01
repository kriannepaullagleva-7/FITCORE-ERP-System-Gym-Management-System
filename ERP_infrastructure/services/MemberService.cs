using ERP_domain.entities;
using ERP_infrastructure.repositories;

namespace ERP_infrastructure.services
{
    public class MemberService : IMemberService
    {
        // A membership inside this window is flagged so staff can chase the renewal.
        public const int ExpiringSoonDays = 14;

        private readonly IMemberRepository _repository;
        private readonly IPaymentRepository _paymentRepository;
        private readonly ISubscriptionRepository _subscriptionRepository;

        public MemberService(
            IMemberRepository repository, IPaymentRepository paymentRepository,
            ISubscriptionRepository subscriptionRepository)
        {
            _repository = repository;
            _paymentRepository = paymentRepository;
            _subscriptionRepository = subscriptionRepository;
        }

        public async Task<Member?> GetMemberByIdAsync(int id)
        {
            return await _repository.GetByIdAsync(id);
        }

        public async Task<List<Member>> GetAllMembersAsync()
        {
            return await _repository.GetAllAsync();
        }

        public async Task<Member> CreateMemberAsync(string firstName, string lastName, string phone, string email)
        {
            firstName = (firstName ?? string.Empty).Trim();
            lastName = (lastName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(firstName))
                throw new ValidationException("First name is required.");
            if (string.IsNullOrWhiteSpace(lastName))
                throw new ValidationException("Last name is required.");

            var member = new Member
            {
                FirstName = firstName,
                LastName = lastName,
                Phone = phone?.Trim() ?? string.Empty,
                Email = email?.Trim() ?? string.Empty,
                JoinDate = DateTime.UtcNow,
                Status = "Active",
                CreatedAt = DateTime.UtcNow
            };

            return await _repository.AddAsync(member);
        }

        public async Task<Member?> UpdateMemberAsync(int id, string firstName, string lastName, string phone, string email, string status)
        {
            firstName = (firstName ?? string.Empty).Trim();
            lastName = (lastName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(firstName))
                throw new ValidationException("First name is required.");
            if (string.IsNullOrWhiteSpace(lastName))
                throw new ValidationException("Last name is required.");

            var member = await _repository.GetByIdAsync(id);
            if (member == null) return null;

            member.FirstName = firstName;
            member.LastName = lastName;
            member.Phone = phone?.Trim() ?? string.Empty;
            member.Email = email?.Trim() ?? string.Empty;
            member.Status = string.IsNullOrWhiteSpace(status) ? member.Status : status.Trim();

            return await _repository.UpdateAsync(member);
        }

        /// <summary>
        /// Removes a member who has no history at all - a record created in error, a duplicate
        /// typed twice. Anyone who has ever paid, subscribed or bought something is refused:
        /// their takings have to stay reconcilable after they leave. The caller is expected to
        /// offer <see cref="ArchiveMemberAsync"/> instead.
        ///
        /// The database enforces the same rule with a restricted foreign key, so a write that
        /// bypasses this service still cannot erase financial history.
        /// </summary>
        public async Task<MemberDeleteResult> DeleteMemberAsync(int id)
        {
            var member = await _repository.GetByIdAsync(id);
            if (member == null) return MemberDeleteResult.NotFound;

            var history = await _repository.GetHistoryCountsAsync(id);
            if (history.HasAny) return MemberDeleteResult.HasHistory;

            var deleted = await _repository.DeleteAsync(id);
            return deleted ? MemberDeleteResult.Deleted : MemberDeleteResult.NotFound;
        }

        /// <summary>
        /// Retires a member without touching their history: the row stays, their subscriptions
        /// and payments stay, and they drop out of the active lists.
        ///
        /// This uses the Status column the application already filters on rather than a new
        /// soft-delete flag, so no report, dashboard or revenue figure changes meaning. Archived
        /// members are still counted in anything historical, which is the point.
        /// </summary>
        public async Task<Member?> ArchiveMemberAsync(int id)
        {
            var member = await _repository.GetByIdAsync(id);
            if (member == null) return null;

            if (string.Equals(member.Status, ArchivedStatus, StringComparison.OrdinalIgnoreCase))
            {
                return member;
            }

            member.Status = ArchivedStatus;
            return await _repository.UpdateAsync(member);
        }

        /// <summary>
        /// Returns an archived member to active use.
        /// </summary>
        public async Task<Member?> RestoreMemberAsync(int id)
        {
            var member = await _repository.GetByIdAsync(id);
            if (member == null) return null;

            member.Status = ActiveStatus;
            return await _repository.UpdateAsync(member);
        }

        /// <summary>
        /// Pauses a membership rather than ending it.
        ///
        /// Suspension and archiving both take somebody off the active roll, and they are not
        /// the same thing: an archived member has left, a suspended one is coming back. Keeping
        /// them apart is what lets the gym tell "we lost forty members this year" from "forty
        /// members are injured", which are very different pieces of news.
        /// </summary>
        public async Task<Member?> SuspendMemberAsync(int id, string reason, DateTime? until)
        {
            var member = await _repository.GetByIdAsync(id);
            if (member == null) return null;

            if (string.Equals(member.Status, ArchivedStatus, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "This member has been archived. Restore them first if they are coming back.");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ValidationException(
                    "Say why the membership is being suspended, so the front desk can explain it.");
            }

            if (until.HasValue && until.Value.Date < DateTime.UtcNow.Date)
            {
                throw new ValidationException("A suspension cannot end in the past.");
            }

            member.Status = SuspendedStatus;
            member.SuspensionReason = reason.Trim();
            member.SuspendedUntil = until;

            return await _repository.UpdateAsync(member);
        }

        public async Task<Member?> ReactivateMemberAsync(int id)
        {
            var member = await _repository.GetByIdAsync(id);
            if (member == null) return null;

            member.Status = ActiveStatus;
            member.SuspensionReason = string.Empty;
            member.SuspendedUntil = null;

            return await _repository.UpdateAsync(member);
        }

        private const string ArchivedStatus = "Archived";
        private const string ActiveStatus = "Active";
        private const string SuspendedStatus = "Suspended";

        public async Task<MemberHistoryCounts> GetMemberHistoryCountsAsync(int id)
        {
            return await _repository.GetHistoryCountsAsync(id);
        }

        public async Task<Member?> GetMemberWithSubscriptionsAsync(int memberId)
        {
            return await _repository.GetMemberWithSubscriptionsAsync(memberId);
        }

        public async Task<List<Member>> GetActiveMembersAsync()
        {
            return await _repository.GetActiveMembers();
        }

        public async Task<List<Member>> SearchMembersAsync(string term)
        {
            return await _repository.SearchAsync(term);
        }

        // Builds the Membership grid: every member alongside the subscription that currently
        // governs their access, plus what they still owe on it - and every walk-in membership
        // besides, since a walk-in's subscription is a real membership sold with no Member row
        // to hang it off. Leaving those out would make the one screen that lists "every
        // membership sold" quietly skip the ones sold to walk-ins.
        public async Task<List<MembershipView>> GetMembershipOverviewAsync()
        {
            var members = await _repository.GetMembersWithSubscriptionsAsync();
            var today = DateTime.UtcNow.Date;

            // Work out which subscription decides each member's status first, so every paid
            // total can be fetched in one query instead of one query per member.
            var currentByMember = new Dictionary<int, Subscription?>(members.Count);

            foreach (var member in members)
            {
                // The subscription running furthest into the future decides the status.
                currentByMember[member.MemberId] = member.Subscriptions
                    .Where(s => s.Status != "Cancelled")
                    .OrderByDescending(s => s.EndDate)
                    .FirstOrDefault()
                    ?? member.Subscriptions.OrderByDescending(s => s.EndDate).FirstOrDefault();
            }

            var allSubscriptions = await _subscriptionRepository.GetAllWithDetailsAsync();
            var walkIns = allSubscriptions.Where(s => s.MemberId is null).ToList();

            var paidTotals = await _paymentRepository.GetPaidTotalsBySubscriptionAsync(
                currentByMember.Values.Where(s => s != null).Select(s => s!.SubscriptionId)
                    .Concat(walkIns.Select(s => s.SubscriptionId)));

            var views = new List<MembershipView>(members.Count + walkIns.Count);

            foreach (var member in members)
            {
                var view = new MembershipView
                {
                    MemberId = member.MemberId,
                    FirstName = member.FirstName,
                    LastName = member.LastName,
                    Phone = member.Phone,
                    Email = member.Email,
                    MemberStatus = member.Status,
                    JoinDate = member.JoinDate
                };

                ApplySubscription(view, currentByMember[member.MemberId], paidTotals, today);
                views.Add(view);
            }

            foreach (var walkIn in walkIns)
            {
                var view = new MembershipView
                {
                    MemberId = null,
                    FirstName = string.IsNullOrWhiteSpace(walkIn.WalkInName) ? "Walk-In" : walkIn.WalkInName,
                    LastName = "",
                    Phone = walkIn.WalkInPhone ?? "",
                    MemberStatus = "Walk-In",
                    JoinDate = walkIn.StartDate
                };

                ApplySubscription(view, walkIn, paidTotals, today);
                views.Add(view);
            }

            return views;
        }

        /// <summary>
        /// The membership/payment status derivation shared by a member's current subscription
        /// and a walk-in's - the same rules regardless of whether a Member record is behind it.
        /// </summary>
        private static void ApplySubscription(
            MembershipView view, Subscription? current,
            IReadOnlyDictionary<int, decimal> paidTotals, DateTime today)
        {
            if (current == null)
            {
                view.MembershipStatus = "No Plan";
                view.PaymentStatus = "N/A";
                return;
            }

            view.SubscriptionId = current.SubscriptionId;
            view.PlanId = current.PlanId;
            view.PlanName = current.Plan?.PlanName ?? $"Plan #{current.PlanId}";
            view.PlanPrice = current.Plan?.Price ?? 0m;
            view.StartDate = current.StartDate;
            view.ExpiryDate = current.EndDate;

            var daysRemaining = (int)Math.Ceiling((current.EndDate.Date - today).TotalDays);
            view.DaysRemaining = daysRemaining;

            if (current.Status == "Cancelled")
                view.MembershipStatus = "Cancelled";
            else if (daysRemaining < 0)
                view.MembershipStatus = "Expired";
            else if (daysRemaining <= ExpiringSoonDays)
                view.MembershipStatus = "Expiring Soon";
            else
                view.MembershipStatus = "Active";

            var paid = paidTotals.TryGetValue(current.SubscriptionId, out var total) ? total : 0m;
            view.AmountPaid = paid;
            view.Balance = Math.Max(0m, view.PlanPrice - paid);

            if (view.PlanPrice <= 0m)
                view.PaymentStatus = "N/A";
            else if (paid <= 0m)
                view.PaymentStatus = "Unpaid";
            else if (view.Balance > 0m)
                view.PaymentStatus = "Partial";
            else
                view.PaymentStatus = "Paid";
        }
    }
}
