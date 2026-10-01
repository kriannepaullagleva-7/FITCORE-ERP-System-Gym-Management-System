using System.ComponentModel.DataAnnotations;

namespace ERP_api.DTOs
{
    public class CreateSubscriptionDto
    {
        /// <summary>
        /// Null for a walk-in membership - one sold to somebody with no Member record. See
        /// <see cref="WalkInName"/>.
        /// </summary>
        public int? MemberId { get; set; }

        /// <summary>The walk-in's name, used only when <see cref="MemberId"/> is null.</summary>
        [StringLength(150)]
        public string? WalkInName { get; set; }

        [StringLength(20)]
        public string? WalkInPhone { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "A valid plan must be selected.")]
        public int PlanId { get; set; }

        /// <summary>Defaults to now when omitted.</summary>
        public DateTime? StartDate { get; set; }
    }

    public class UpdateSubscriptionDto
    {
        [Required]
        [RegularExpression("^(Active|Expired|Cancelled)$",
            ErrorMessage = "Status must be Active, Expired or Cancelled.")]
        public string Status { get; set; } = "Active";

        public DateTime EndDate { get; set; }
    }

    public class SubscriptionDto
    {
        public int SubscriptionId { get; set; }
        public int? MemberId { get; set; }
        public string? WalkInName { get; set; }
        public string? WalkInPhone { get; set; }
        public int PlanId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = "";
    }
}
