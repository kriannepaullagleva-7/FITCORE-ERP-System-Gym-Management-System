using System;

namespace ERP_domain.entities
{
    public class Payment : IAuditable
    {
        public int PaymentId { get; set; }

        // A payment always belongs to a member. It optionally settles a specific subscription
        // (membership dues) or a specific sale (goods); a standalone member payment leaves
        // both null. A payment never settles a subscription and a sale at the same time.
        public int MemberId { get; set; }
        public int? SubscriptionId { get; set; }
        public int? SaleId { get; set; }

        /// <summary>
        /// What kind of transaction this money settles: <c>Membership</c>, <c>Sales</c> or
        /// <c>General</c>.
        ///
        /// It is stored rather than derived from whether SubscriptionId or SaleId is set,
        /// because the link can be cleared later - cancelling a sale detaches its payments -
        /// and the takings must still be classifiable afterwards.
        /// </summary>
        public string Category { get; set; } = PaymentCategories.General;

        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
        public string Method { get; set; } = "Cash"; // Cash, Card, Transfer, Check, GCash
        public string ReferenceNo { get; set; } = string.Empty;
        public string Status { get; set; } = "Completed"; // Completed, Pending, Failed, Refunded
        public string Notes { get; set; } = string.Empty;

        /// <summary>
        /// The signed-in user who took the money, captured from the token at the moment the
        /// payment was recorded. Held on the row rather than only in the audit trail so a
        /// receipt reprinted years later still names the cashier.
        /// </summary>
        public int? ProcessedByUserId { get; set; }

        /// <summary>Their display name, denormalised so the receipt survives the account.</summary>
        public string ProcessedBy { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Stamped by the DbContext whenever this record is changed.</summary>
        public DateTime? UpdatedAt { get; set; }

        // Foreign keys
        public Member Member { get; set; } = null!;
        public Subscription? Subscription { get; set; }
        public Sale? Sale { get; set; }
    }
}
