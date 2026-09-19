using System.ComponentModel.DataAnnotations;

namespace ERP_api.DTOs
{
    public class CreateMembershipPlanDto
    {
        [Required(ErrorMessage = "A plan name is required.")]
        [StringLength(100)]
        public string PlanName { get; set; } = "";

        [Range(1, 120, ErrorMessage = "Duration must be between 1 and 120 months.")]
        public int DurationMonths { get; set; }

        [Range(0, 10000000, ErrorMessage = "Price cannot be negative.")]
        public decimal Price { get; set; }

        [StringLength(500)]
        public string Description { get; set; } = "";
    }

    public class UpdateMembershipPlanDto
    {
        [Required(ErrorMessage = "A plan name is required.")]
        [StringLength(100)]
        public string PlanName { get; set; } = "";

        [Range(1, 120, ErrorMessage = "Duration must be between 1 and 120 months.")]
        public int DurationMonths { get; set; }

        [Range(0, 10000000, ErrorMessage = "Price cannot be negative.")]
        public decimal Price { get; set; }

        [StringLength(500)]
        public string Description { get; set; } = "";

        public bool IsActive { get; set; }
    }

    public class MembershipPlanDto
    {
        public int PlanId { get; set; }
        public string PlanName { get; set; } = "";
        public int DurationMonths { get; set; }
        public decimal Price { get; set; }
        public string Description { get; set; } = "";
        public bool IsActive { get; set; }
    }
}
