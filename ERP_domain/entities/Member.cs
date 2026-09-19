using System;
using System.Collections.Generic;

namespace ERP_domain.entities
{
    public class Member : IAuditable
    {
        public int MemberId { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime JoinDate { get; set; }
        public string Status { get; set; } = "Active"; // Active, Inactive, Suspended
        public DateTime CreatedAt { get; set; }

        /// <summary>Stamped by the DbContext whenever this record is changed.</summary>
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
        public ICollection<Sale> Sales { get; set; } = new List<Sale>();
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}