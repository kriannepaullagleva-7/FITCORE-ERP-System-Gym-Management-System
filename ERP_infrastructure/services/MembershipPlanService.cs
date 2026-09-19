using ERP_domain.entities;
using ERP_infrastructure.repositories;

namespace ERP_infrastructure.services
{
    public class MembershipPlanService : IMembershipPlanService
    {
        private readonly IGenericRepository<MembershipPlan> _repository;
        private readonly ISubscriptionRepository _subscriptionRepository;

        public MembershipPlanService(
            IGenericRepository<MembershipPlan> repository,
            ISubscriptionRepository subscriptionRepository)
        {
            _repository = repository;
            _subscriptionRepository = subscriptionRepository;
        }

        public async Task<MembershipPlan?> GetPlanByIdAsync(int id)
        {
            return await _repository.GetByIdAsync(id);
        }

        public async Task<List<MembershipPlan>> GetAllPlansAsync()
        {
            var plans = await _repository.GetAllAsync();
            return plans.OrderBy(p => p.PlanName).ToList();
        }

        public async Task<List<MembershipPlan>> GetActivePlansAsync()
        {
            var plans = await GetAllPlansAsync();
            return plans.Where(p => p.IsActive).ToList();
        }

        public async Task<MembershipPlan> CreatePlanAsync(string planName, int durationMonths, decimal price, string description)
        {
            planName = Validate(planName, durationMonths, price);
            description = description?.Trim() ?? string.Empty;

            await EnsureNameIsFreeAsync(planName, excludingPlanId: null);

            var plan = new MembershipPlan
            {
                PlanName = planName,
                DurationMonths = durationMonths,
                Price = price,
                Description = description,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            return await _repository.AddAsync(plan);
        }

        public async Task<MembershipPlan?> UpdatePlanAsync(int id, string planName, int durationMonths, decimal price, string description, bool isActive)
        {
            planName = Validate(planName, durationMonths, price);
            description = description?.Trim() ?? string.Empty;

            var plan = await _repository.GetByIdAsync(id);
            if (plan == null) return null;

            // Only checked when the name is actually being changed, so a plan that already
            // shares a name with an older row can still have its price or status edited.
            if (!string.Equals(plan.PlanName, planName, StringComparison.OrdinalIgnoreCase))
                await EnsureNameIsFreeAsync(planName, excludingPlanId: id);

            plan.PlanName = planName;
            plan.DurationMonths = durationMonths;
            plan.Price = price;
            plan.Description = description;
            plan.IsActive = isActive;

            return await _repository.UpdateAsync(plan);
        }

        public async Task<PlanDeleteResult> DeletePlanAsync(int id)
        {
            var plan = await _repository.GetByIdAsync(id);
            if (plan == null) return PlanDeleteResult.NotFound;

            // Checked up front so the caller gets a clear answer rather than a foreign key
            // error out of SQL Server.
            if (await _subscriptionRepository.CountByPlanAsync(id) > 0)
                return PlanDeleteResult.InUse;

            var deleted = await _repository.DeleteAsync(id);
            return deleted ? PlanDeleteResult.Deleted : PlanDeleteResult.NotFound;
        }

        public async Task<int> CountSubscriptionsForPlanAsync(int planId)
        {
            return await _subscriptionRepository.CountByPlanAsync(planId);
        }

        private static string Validate(string planName, int durationMonths, decimal price)
        {
            planName = (planName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(planName))
                throw new ValidationException("Plan name is required.");

            if (planName.Length > 100)
                throw new ValidationException("Plan name cannot be longer than 100 characters.");

            if (durationMonths <= 0)
                throw new ValidationException("Duration must be at least one month.");

            if (durationMonths > 120)
                throw new ValidationException("Duration cannot be longer than 120 months.");

            if (price < 0m)
                throw new ValidationException("Price cannot be negative.");

            return planName;
        }

        // Two plans with the same name make the subscription picker ambiguous, so names are
        // kept unique regardless of casing.
        private async Task EnsureNameIsFreeAsync(string planName, int? excludingPlanId)
        {
            var plans = await _repository.GetAllAsync();

            var clash = plans.Any(p =>
                p.PlanId != excludingPlanId &&
                string.Equals(p.PlanName, planName, StringComparison.OrdinalIgnoreCase));

            if (clash)
                throw new ValidationException($"A plan called '{planName}' already exists.");
        }
    }
}
