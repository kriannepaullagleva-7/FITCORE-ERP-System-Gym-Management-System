using System;
using System.Collections.Generic;

namespace ERP_infrastructure.services
{
    // Flattened payment row showing which member paid and what the money settled.
    public class PaymentView
    {
        public int PaymentId { get; set; }

        /// <summary>Null for a walk-in payment. See <see cref="MemberName"/>.</summary>
        public int? MemberId { get; set; }

        /// <summary>
        /// The member's real name, or the walk-in's typed name, or "Walk-In" if neither. Always
        /// populated. Used on receipts and detail views.
        /// </summary>
        public string MemberName { get; set; } = string.Empty;

        /// <summary>
        /// What the Payments grid shows in its Member column.
        ///
        /// Counter sales are recorded against a member because the schema requires one, but
        /// in a financial history the member is not the point of the row - the sale is. So a
        /// sales payment reads "Others" here while <see cref="MemberName"/> keeps the real
        /// name for the receipt. Nothing is hidden, only de-emphasised where it would mislead.
        /// </summary>
        public string MemberDisplay { get; set; } = string.Empty;

        /// <summary>Membership, Sales or General.</summary>
        public string Category { get; set; } = string.Empty;

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

        /// <summary>The signed-in user who took the money.</summary>
        public int? ProcessedByUserId { get; set; }
        public string ProcessedBy { get; set; } = string.Empty;

        /// <summary>Cash handed over, when this payment was taken in cash.</summary>
        public decimal? AmountTendered { get; set; }

        /// <summary><see cref="AmountTendered"/> minus <see cref="Amount"/>.</summary>
        public decimal? ChangeGiven { get; set; }
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
