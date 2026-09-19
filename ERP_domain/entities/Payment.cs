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

        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
        public string Method { get; set; } = "Cash"; // Cash, Card, Transfer, Check, GCash
        public string ReferenceNo { get; set; } = string.Empty;
        public string Status { get; set; } = "Completed"; // Completed, Pending, Failed, Refunded
        public string Notes { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Stamped by the DbContext whenever this record is changed.</summary>
        public DateTime? UpdatedAt { get; set; }

        // Foreign keys
        public Member Member { get; set; } = null!;
        public Subscription? Subscription { get; set; }
        public Sale? Sale { get; set; }
    }
}
