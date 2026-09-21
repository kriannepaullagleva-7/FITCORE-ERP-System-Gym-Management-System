using ERP_domain.entities;
using ERP_infrastructure.data;
using ERP_infrastructure.repositories;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    public class PaymentService : IPaymentService
    {
        private static readonly string[] AllowedStatuses = { "Completed", "Pending", "Failed", "Refunded" };
        private static readonly string[] AllowedMethods = { "Cash", "Card", "Transfer", "Check", "GCash" };

        private readonly IPaymentRepository _repository;
        private readonly ISubscriptionRepository _subscriptionRepo;
        private readonly IMemberRepository _memberRepo;
        private readonly TenantErpDbContext _context;
        private readonly ICurrentUserAccessor _actor;

        public PaymentService(
            IPaymentRepository repository,
            ISubscriptionRepository subscriptionRepo,
            IMemberRepository memberRepo,
            TenantErpDbContext context,
            ICurrentUserAccessor actor)
        {
            _repository = repository;
            _subscriptionRepo = subscriptionRepo;
            _memberRepo = memberRepo;
            _context = context;
            _actor = actor;
        }

        public static PaymentView ToView(Payment payment)
        {
            var memberName = payment.Member == null
                ? $"Member #{payment.MemberId}"
                : $"{payment.Member.FirstName} {payment.Member.LastName}".Trim();

            // Categories written before the column existed, or by a caller that did not set
            // one, are recovered from what the payment is attached to.
            var category = PaymentCategories.IsKnown(payment.Category)
                ? payment.Category
                : PaymentCategories.Infer(payment.SubscriptionId, payment.SaleId);

            return new PaymentView
            {
                PaymentId = payment.PaymentId,
                MemberId = payment.MemberId,
                MemberName = memberName,

                // A counter sale is not "about" the member, so the grid says Others while the
                // receipt still names them.
                MemberDisplay = category == PaymentCategories.Sales ? "Others" : memberName,

                Category = category,
                ProcessedByUserId = payment.ProcessedByUserId,
                ProcessedBy = string.IsNullOrWhiteSpace(payment.ProcessedBy)
                    ? "—"
                    : payment.ProcessedBy,

                SubscriptionId = payment.SubscriptionId,
                PlanName = payment.Subscription?.Plan?.PlanName ?? "- general -",
                SaleId = payment.SaleId,
                AppliesTo = payment.SaleId.HasValue
                    ? $"Sale #{payment.SaleId}"
                    : payment.SubscriptionId.HasValue
                        ? $"Membership - {payment.Subscription?.Plan?.PlanName ?? $"#{payment.SubscriptionId}"}"
                        : "General",
                Amount = payment.Amount,
                PaymentDate = payment.PaymentDate,
                Method = payment.Method,
                ReferenceNo = payment.ReferenceNo,
                Status = payment.Status,
                Notes = payment.Notes
            };
        }

        public async Task<Payment?> GetPaymentByIdAsync(int id)
        {
            return await _repository.GetWithDetailsAsync(id);
        }

        public async Task<List<PaymentView>> GetAllPaymentsAsync()
        {
            var payments = await _repository.GetAllWithDetailsAsync();
            return payments.Select(ToView).ToList();
        }

        public async Task<List<PaymentView>> GetPaymentsInRangeAsync(DateTime fromUtc, DateTime toUtc)
        {
            var payments = await _repository.GetInRangeAsync(fromUtc, toUtc);
            return payments.Select(ToView).ToList();
        }

        public async Task<List<PaymentView>> GetMemberPaymentsAsync(int memberId)
        {
            var payments = await _repository.GetMemberPaymentsAsync(memberId);
            return payments.Select(ToView).ToList();
        }

        public async Task<List<PaymentView>> GetSalePaymentsAsync(int saleId)
        {
            var payments = await _repository.GetSalePaymentsAsync(saleId);
            return payments.Select(ToView).ToList();
        }

        public async Task<List<Payment>> GetSubscriptionPaymentsAsync(int subscriptionId)
        {
            return await _repository.GetSubscriptionPaymentsAsync(subscriptionId);
        }

        public async Task<decimal> GetSubscriptionPaidTotalAsync(int subscriptionId)
        {
            return await _repository.GetSubscriptionPaidTotalAsync(subscriptionId);
        }

        public async Task<decimal> GetSalePaidTotalAsync(int saleId)
        {
            return await _repository.GetSalePaidTotalAsync(saleId);
        }

        public async Task<PaymentSummary> GetSummaryAsync()
        {
            var now = DateTime.UtcNow;
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var dayStart = now.Date;

            var summary = new PaymentSummary();

            // Every headline payment figure in one grouped pass over the table.
            var totals = await _context.Payments
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    Collected = g.Sum(p => p.Status == "Completed" ? p.Amount : 0m),
                    ThisMonth = g.Sum(p =>
                        p.Status == "Completed" && p.PaymentDate >= monthStart ? p.Amount : 0m),
                    Today = g.Sum(p =>
                        p.Status == "Completed" && p.PaymentDate >= dayStart ? p.Amount : 0m),
                    PendingCount = g.Count(p => p.Status == "Pending"),
                    PendingAmount = g.Sum(p => p.Status == "Pending" ? p.Amount : 0m),
                    Refunded = g.Sum(p => p.Status == "Refunded" ? p.Amount : 0m)
                })
                .FirstOrDefaultAsync();

            summary.Count = totals?.Count ?? 0;
            summary.TotalCollected = totals?.Collected ?? 0m;
            summary.CollectedThisMonth = totals?.ThisMonth ?? 0m;
            summary.CollectedToday = totals?.Today ?? 0m;
            summary.PendingCount = totals?.PendingCount ?? 0;
            summary.PendingAmount = totals?.PendingAmount ?? 0m;
            summary.RefundedAmount = totals?.Refunded ?? 0m;

            summary.ByMethod = await _context.Payments
                .Where(p => p.Status == "Completed")
                .GroupBy(p => p.Method)
                .Select(g => new PaymentMethodTotal
                {
                    Method = g.Key,
                    Count = g.Count(),
                    Total = g.Sum(p => p.Amount)
                })
                .OrderByDescending(x => x.Total)
                .ToListAsync();

            // What is still owed on live sales: the sale total less the money settled against
            // it. Cancelled sales are excluded because they are no longer a debt.
            var saleBalances = await _context.Sales
                .Where(s => s.Status != "Cancelled")
                .Select(s => new
                {
                    s.TotalAmount,
                    Paid = s.Payments
                        .Where(p => p.Status == "Completed")
                        .Sum(p => (decimal?)p.Amount) ?? 0m
                })
                .ToListAsync();

            summary.OutstandingFromSales = saleBalances.Sum(s => Math.Max(0m, s.TotalAmount - s.Paid));
            summary.PartiallyPaidSales = saleBalances.Count(s => s.Paid > 0 && s.Paid < s.TotalAmount);
            summary.UnpaidSales = saleBalances.Count(s => s.Paid <= 0 && s.TotalAmount > 0);

            // The same question for memberships: plan price less what has been paid on the
            // subscription.
            var membershipBalances = await _context.Subscriptions
                .Where(s => s.Status != "Cancelled")
                .Select(s => new
                {
                    Price = s.Plan.Price,
                    Paid = s.Payments
                        .Where(p => p.Status == "Completed")
                        .Sum(p => (decimal?)p.Amount) ?? 0m
                })
                .ToListAsync();

            summary.OutstandingFromMemberships =
                membershipBalances.Sum(s => Math.Max(0m, s.Price - s.Paid));

            return summary;
        }

        public async Task<Payment> RecordPaymentAsync(
            int memberId,
            int? subscriptionId,
            int? saleId,
            decimal amount,
            DateTime paymentDate,
            string method,
            string referenceNo,
            string status,
            string notes)
        {
            if (amount <= 0)
                throw new InvalidOperationException("Payment amount must be greater than zero.");

            if (subscriptionId.HasValue && saleId.HasValue)
                throw new InvalidOperationException(
                    "A payment settles either a membership or a sale, not both.");

            var member = await _memberRepo.GetByIdAsync(memberId);
            if (member == null)
                throw new InvalidOperationException("Member not found.");

            if (subscriptionId.HasValue)
            {
                var subscription = await _subscriptionRepo.GetByIdAsync(subscriptionId.Value);
                if (subscription == null)
                    throw new InvalidOperationException("Subscription not found.");
                if (subscription.MemberId != memberId)
                    throw new InvalidOperationException("That subscription belongs to a different member.");
            }

            var normalisedStatus = NormaliseStatus(status);

            if (saleId.HasValue)
            {
                var sale = await _context.Sales
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.SaleId == saleId.Value);

                if (sale == null)
                    throw new InvalidOperationException("Sale not found.");
                if (sale.MemberId != memberId)
                    throw new InvalidOperationException("That sale belongs to a different member.");
                if (string.Equals(sale.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("That sale has been cancelled and cannot take payment.");

                // Refuse to collect more than the sale is worth, which is what would otherwise
                // turn an overpayment into a silently wrong "Paid" balance.
                if (normalisedStatus == "Completed")
                {
                    var alreadyPaid = await _repository.GetSalePaidTotalAsync(saleId.Value);
                    var balance = sale.TotalAmount - alreadyPaid;

                    if (amount > balance)
                        throw new InvalidOperationException(
                            $"That is more than the sale still owes. Outstanding balance is {balance:N2}.");
                }
            }

            var actor = _actor.Current;

            var payment = new Payment
            {
                MemberId = memberId,
                SubscriptionId = subscriptionId,
                SaleId = saleId,

                // Derived, never taken from the caller. A payment's category is a fact about
                // what it is attached to, so letting a client name it would only create rows
                // whose label disagrees with their own foreign keys.
                Category = PaymentCategories.Infer(subscriptionId, saleId),

                Amount = amount,
                PaymentDate = paymentDate == default ? DateTime.UtcNow : paymentDate,
                Method = NormaliseMethod(method),
                ReferenceNo = (referenceNo ?? string.Empty).Trim(),
                Status = normalisedStatus,
                Notes = (notes ?? string.Empty).Trim(),

                // The cashier is whoever the token says is signed in - not a name the client
                // supplies, which is the whole point of recording it.
                ProcessedByUserId = actor.AppUserId,
                ProcessedBy = actor.DisplayName,

                CreatedAt = DateTime.UtcNow
            };

            return await _repository.AddAsync(payment);
        }

        public async Task<Payment> RecordPaymentAsync(int subscriptionId, decimal amount, string method)
        {
            var subscription = await _subscriptionRepo.GetByIdAsync(subscriptionId);
            if (subscription == null)
                throw new InvalidOperationException("Subscription not found.");

            return await RecordPaymentAsync(
                subscription.MemberId,
                subscriptionId,
                null,
                amount,
                DateTime.UtcNow,
                method,
                string.Empty,
                "Completed",
                string.Empty);
        }

        public async Task<Payment?> UpdatePaymentAsync(
            int id,
            decimal amount,
            DateTime paymentDate,
            string method,
            string referenceNo,
            string status,
            string notes)
        {
            if (amount <= 0)
                throw new InvalidOperationException("Payment amount must be greater than zero.");

            var payment = await _repository.GetByIdAsync(id);
            if (payment == null) return null;

            var normalisedStatus = NormaliseStatus(status);

            // Raising the amount on a sale payment must respect the same ceiling a new payment
            // would, ignoring this payment's own current contribution.
            if (payment.SaleId.HasValue && normalisedStatus == "Completed")
            {
                var sale = await _context.Sales
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.SaleId == payment.SaleId.Value);

                if (sale != null)
                {
                    var paidByOthers = await _context.Payments
                        .Where(p => p.SaleId == payment.SaleId.Value
                                 && p.PaymentId != id
                                 && p.Status == "Completed")
                        .SumAsync(p => (decimal?)p.Amount) ?? 0m;

                    var ceiling = sale.TotalAmount - paidByOthers;

                    if (amount > ceiling)
                        throw new InvalidOperationException(
                            $"That is more than the sale still owes. Outstanding balance is {ceiling:N2}.");
                }
            }

            payment.Amount = amount;
            payment.PaymentDate = paymentDate == default ? payment.PaymentDate : paymentDate;
            payment.Method = NormaliseMethod(method);
            payment.ReferenceNo = (referenceNo ?? string.Empty).Trim();
            payment.Status = normalisedStatus;
            payment.Notes = (notes ?? string.Empty).Trim();

            return await _repository.UpdateAsync(payment);
        }

        public async Task<Payment?> UpdatePaymentStatusAsync(int id, string status)
        {
            var payment = await _repository.GetByIdAsync(id);
            if (payment == null) return null;

            payment.Status = NormaliseStatus(status);
            return await _repository.UpdateAsync(payment);
        }

        public async Task<Payment?> VoidPaymentAsync(int id, string reason = "")
        {
            var payment = await _repository.GetByIdAsync(id);
            if (payment == null) return null;

            if (payment.Status == "Refunded")
                throw new InvalidOperationException("This payment has already been refunded.");

            payment.Status = "Refunded";

            var trimmed = (reason ?? string.Empty).Trim();
            if (trimmed.Length > 0)
            {
                var note = $"Voided: {trimmed}";
                payment.Notes = string.IsNullOrWhiteSpace(payment.Notes)
                    ? note
                    : $"{payment.Notes} | {note}";

                if (payment.Notes.Length > 300) payment.Notes = payment.Notes[..300];
            }

            return await _repository.UpdateAsync(payment);
        }

        public async Task<bool> DeletePaymentAsync(int id)
        {
            return await _repository.DeleteAsync(id);
        }

        private static string NormaliseStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status)) return "Completed";

            var match = AllowedStatuses.FirstOrDefault(
                s => string.Equals(s, status.Trim(), StringComparison.OrdinalIgnoreCase));

            return match ?? throw new InvalidOperationException(
                $"'{status}' is not a valid payment status. Use one of: {string.Join(", ", AllowedStatuses)}.");
        }

        private static string NormaliseMethod(string method)
        {
            if (string.IsNullOrWhiteSpace(method)) return "Cash";

            var match = AllowedMethods.FirstOrDefault(
                m => string.Equals(m, method.Trim(), StringComparison.OrdinalIgnoreCase));

            return match ?? throw new InvalidOperationException(
                $"'{method}' is not a valid payment method. Use one of: {string.Join(", ", AllowedMethods)}.");
        }
    }
}
