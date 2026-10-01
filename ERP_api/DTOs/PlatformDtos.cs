using System.ComponentModel.DataAnnotations;
using ERP_domain.entities;

namespace ERP_api.DTOs
{
    public class CreateTenantDto
    {
        [Required(ErrorMessage = "A company code is required.")]
        [StringLength(50, MinimumLength = 2)]
        public string CompanyCode { get; set; } = "";

        [Required(ErrorMessage = "A company name is required.")]
        [StringLength(200)]
        public string CompanyName { get; set; } = "";

        public EnterpriseTier Tier { get; set; } = EnterpriseTier.Micro;

        /// <summary>Where the tenant's database lives. Without it the tenant cannot be served.</summary>
        [StringLength(200)]
        public string ServerName { get; set; } = "";

        [StringLength(200)]
        public string DatabaseName { get; set; } = "";

        /// <summary>
        /// The key its credentials are stored under in server configuration - never the
        /// password itself. The master registry holds a key and the secret stays on the server.
        /// </summary>
        [StringLength(100)]
        public string CredentialKey { get; set; } = "";
    }

    public class UpdateTenantDto
    {
        [Required]
        [StringLength(200)]
        public string CompanyName { get; set; } = "";

        public EnterpriseTier Tier { get; set; } = EnterpriseTier.Micro;

        public bool IsActive { get; set; } = true;
    }

    public class SetTierDto
    {
        public EnterpriseTier Tier { get; set; } = EnterpriseTier.Micro;

        /// <summary>
        /// Required. Moving a tenant's tier takes screens away from people mid-session or hands
        /// them new ones, so the reason belongs on the record.
        /// </summary>
        [Required(ErrorMessage = "Say why the tier is changing.")]
        [StringLength(300)]
        public string Reason { get; set; } = "";
    }

    public class SetTenantStatusDto
    {
        public bool IsActive { get; set; }

        [StringLength(300)]
        public string Reason { get; set; } = "";
    }

    public class SetUserStatusDto
    {
        public bool IsActive { get; set; }
    }

    public class CreateSubscriptionPlanDto
    {
        [Required(ErrorMessage = "A plan code is required.")]
        [StringLength(40)]
        public string PlanCode { get; set; } = "";

        [Required(ErrorMessage = "A plan name is required.")]
        [StringLength(150)]
        public string PlanName { get; set; } = "";

        public EnterpriseTier Tier { get; set; } = EnterpriseTier.Micro;

        [Range(0, 9999999)]
        public decimal MonthlyPrice { get; set; }

        [Range(0, 9999999)]
        public decimal AnnualPrice { get; set; }

        /// <summary>Zero means no ceiling.</summary>
        [Range(0, 100000)]
        public int MaxUsers { get; set; }

        [StringLength(500)]
        public string Description { get; set; } = "";

        /// <summary>The one-line "what this buys you" a pricing card shows under the tier name.</summary>
        [StringLength(500)]
        public string Capability { get; set; } = "";
    }

    /// <summary>
    /// The tier is deliberately absent. Changing it would silently move every tenant on the
    /// plan to a different feature set, which is a decision about each of those tenants rather
    /// than about the price list.
    /// </summary>
    public class UpdateSubscriptionPlanDto
    {
        [Required]
        [StringLength(150)]
        public string PlanName { get; set; } = "";

        [Range(0, 9999999)]
        public decimal MonthlyPrice { get; set; }

        [Range(0, 9999999)]
        public decimal AnnualPrice { get; set; }

        [Range(0, 100000)]
        public int MaxUsers { get; set; }

        [StringLength(500)]
        public string Description { get; set; } = "";

        [StringLength(500)]
        public string Capability { get; set; } = "";

        public bool IsActive { get; set; } = true;
    }

    public class SubscribeDto
    {
        [Range(1, int.MaxValue)]
        public int CompanyId { get; set; }

        [Range(1, int.MaxValue)]
        public int SubscriptionPlanId { get; set; }

        /// <summary>Monthly or Annual.</summary>
        [StringLength(20)]
        public string BillingCycle { get; set; } = BillingCycles.Monthly;

        public DateTime StartDate { get; set; }

        public bool AutoRenew { get; set; } = true;
    }
}
