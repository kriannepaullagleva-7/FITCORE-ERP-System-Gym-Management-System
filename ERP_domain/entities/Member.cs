using System;
using System.Collections.Generic;

namespace ERP_domain.entities
{
    public class Member : IAuditable, IBranchScoped
    {
        public int MemberId { get; set; }

        /// <summary>The branch this member joined at. Null on a single-site tenant.</summary>
        public int? BranchId { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime JoinDate { get; set; }

        /// <summary>Active, Inactive, Suspended or Archived.</summary>
        public string Status { get; set; } = "Active";

        /// <summary>
        /// Why the membership is suspended, and until when. Suspension is a pause rather than
        /// an ending - an injury, a long trip - so the reason and the expected return are worth
        /// keeping, and the member keeps their history either way.
        /// </summary>
        public string SuspensionReason { get; set; } = string.Empty;
        public DateTime? SuspendedUntil { get; set; }

        public string Address { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }

        /// <summary>Who to call. A gym holds people doing strenuous things unsupervised.</summary>
        public string EmergencyContactName { get; set; } = string.Empty;
        public string EmergencyContactPhone { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        /// <summary>Stamped by the DbContext whenever this record is changed.</summary>
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
        public ICollection<Sale> Sales { get; set; } = new List<Sale>();
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
        public ICollection<MemberNote> Notes { get; set; } = new List<MemberNote>();
    }
}