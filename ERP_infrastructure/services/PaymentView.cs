using System;
using System.Collections.Generic;

namespace ERP_infrastructure.services
{
    // Flattened payment row showing which member paid and what the money settled.
    public class PaymentView
    {
        public int PaymentId { get; set; }
        public int MemberId { get; set; }
        public string MemberName { get; set; } = string.Empty;

        public int? SubscriptionId { get; set; }
        public string PlanName { get; set; } = "- general -";

        public int? SaleId { get; set; }

        /// <summary>"Sale #12", "Membership - Gold", or "General" - what the payment is for.</summary>
        public string AppliesTo { get; set; } = "General";

        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string Method { get; set; } = string.Empty;
        public string ReferenceNo { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    public class PaymentMethodTotal
    {
        public string Method { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal Total { get; set; }
    }

    // Headline payment figures, including what is still owed across sales and memberships.
    public class PaymentSummary
    {
        public int Count { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal CollectedThisMonth { get; set; }
        public decimal CollectedToday { get; set; }
        public int PendingCount { get; set; }
        public decimal PendingAmount { get; set; }
        public decimal RefundedAmount { get; set; }

        public decimal OutstandingFromSales { get; set; }
        public decimal OutstandingFromMemberships { get; set; }
        public decimal TotalOutstanding => OutstandingFromSales + OutstandingFromMemberships;

        public int PartiallyPaidSales { get; set; }
        public int UnpaidSales { get; set; }

        public List<PaymentMethodTotal> ByMethod { get; set; } = new();
    }
}
