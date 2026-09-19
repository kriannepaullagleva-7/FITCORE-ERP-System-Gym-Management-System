using System.ComponentModel.DataAnnotations;

namespace ERP_api.DTOs
{
    public class CreateSubscriptionDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "A valid member must be selected.")]
        public int MemberId { get; set; }

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
        public int MemberId { get; set; }
        public int PlanId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = "";
    }
}
