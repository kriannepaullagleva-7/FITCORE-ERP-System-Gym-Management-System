using System;
using System.Collections.Generic;

namespace ERP_domain.entities
{
    public class MembershipPlan : IAuditable
    {
        public int PlanId { get; set; }
        public string PlanName { get; set; } = string.Empty;
        public int DurationMonths { get; set; }
        public decimal Price { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Stamped by the DbContext whenever this record is changed.</summary>
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    }
}