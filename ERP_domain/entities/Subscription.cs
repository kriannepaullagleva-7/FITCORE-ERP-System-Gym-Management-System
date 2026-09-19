using System;
using System.Collections.Generic;

namespace ERP_domain.entities
{
    public class Subscription : IAuditable
    {
        public int SubscriptionId { get; set; }
        public int MemberId { get; set; }
        public int PlanId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = "Active"; // Active, Expired, Cancelled
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Stamped by the DbContext whenever this record is changed.</summary>
        public DateTime? UpdatedAt { get; set; }

        // Foreign keys
        public Member Member { get; set; } = null!;
        public MembershipPlan Plan { get; set; } = null!;

        // Navigation properties
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}