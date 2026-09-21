namespace ERP_Project1.Api
{
    public class CreateMembershipPlanDto
    {
        public string PlanName { get; set; } = string.Empty;
        public int DurationMonths { get; set; }
        public decimal Price { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public class UpdateMembershipPlanDto
    {
        public string PlanName { get; set; } = string.Empty;
        public int DurationMonths { get; set; }
        public decimal Price { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public class MembershipPlanDto
    {
        public int PlanId { get; set; }
        public string PlanName { get; set; } = string.Empty;
        public int DurationMonths { get; set; }
        public decimal Price { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}

