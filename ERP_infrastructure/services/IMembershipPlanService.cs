using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    // A plan that subscriptions already point at is protected by a foreign key, so the
    // delete path reports that case instead of letting SQL Server raise a constraint error.
    public enum PlanDeleteResult
    {
        Deleted,
        NotFound,
        InUse
    }

    public interface IMembershipPlanService
    {
        Task<MembershipPlan?> GetPlanByIdAsync(int id);
        Task<List<MembershipPlan>> GetAllPlansAsync();
        Task<List<MembershipPlan>> GetActivePlansAsync();
        Task<MembershipPlan> CreatePlanAsync(string planName, int durationMonths, decimal price, string description);
        Task<MembershipPlan?> UpdatePlanAsync(int id, string planName, int durationMonths, decimal price, string description, bool isActive);
        Task<PlanDeleteResult> DeletePlanAsync(int id);
        Task<int> CountSubscriptionsForPlanAsync(int planId);
    }
}
