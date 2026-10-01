using System;
using System.Collections.Generic;

namespace ERP_domain.entities
{
    public class Subscription : IAuditable, IBranchScoped
    {
        public int SubscriptionId { get; set; }

        /// <summary>The branch that sold this subscription. Null on a single-site tenant.</summary>
        public int? BranchId { get; set; }
        /// <summary>
        /// Null for a walk-in membership sold to nobody with a Member record yet - see
        /// <see cref="WalkInName"/>. Exactly one of a real member or a walk-in name is present.
        /// </summary>
        public int? MemberId { get; set; }
        public int PlanId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = "Active"; // Active, Expired, Cancelled
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Stamped by the DbContext whenever this record is changed.</summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>The walk-in's name, used only when <see cref="MemberId"/> is null.</summary>
        public string? WalkInName { get; set; }

        /// <summary>The walk-in's phone, optional even for a walk-in.</summary>
        public string? WalkInPhone { get; set; }

        // Foreign keys
        public Member? Member { get; set; }
        public MembershipPlan Plan { get; set; } = null!;

        // Navigation properties
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}