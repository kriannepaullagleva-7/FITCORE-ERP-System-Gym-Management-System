using System;

namespace ERP_infrastructure.services
{
    // Flattened membership row: a member joined to their current subscription, with the
    // derived membership state the Membership module displays.
    public class MembershipView
    {
        public int MemberId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName => $"{FirstName} {LastName}".Trim();
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string MemberStatus { get; set; } = string.Empty;
        public DateTime JoinDate { get; set; }

        public int? SubscriptionId { get; set; }
        public int? PlanId { get; set; }
        public string PlanName { get; set; } = "- none -";
        public decimal PlanPrice { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? ExpiryDate { get; set; }

        // "No Plan", "Active", "Expiring Soon", "Expired" or "Cancelled".
        public string MembershipStatus { get; set; } = "No Plan";
        public int? DaysRemaining { get; set; }

        public decimal AmountPaid { get; set; }
        public decimal Balance { get; set; }
        public string PaymentStatus { get; set; } = "N/A";
    }
}
