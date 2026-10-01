using System.Globalization;
using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    public class PlatformAdminService : IPlatformAdminService
    {
        private readonly MasterErpDbContext _master;
        private readonly ITenantProvisioningService _provisioning;
        private readonly IPasswordHasher<AppUser> _passwordHasher;
        private readonly IAuthAuditService _audit;
        private readonly ICurrentUserAccessor _actor;

        public PlatformAdminService(
            MasterErpDbContext master,
            ITenantProvisioningService provisioning,
            IPasswordHasher<AppUser> passwordHasher,
            IAuthAuditService audit,
            ICurrentUserAccessor actor)
        {
            _master = master;
            _provisioning = provisioning;
            _passwordHasher = passwordHasher;
            _audit = audit;
            _actor = actor;
        }

        // ================================================================== tenants

        /// <summary>
        /// The companies FitCore sells to. The platform company itself is deliberately absent:
        /// it is where the Super Admin's own account lives rather than a gym on a plan, it buys
        /// nothing from itself, and counting it beside the paying tenants makes every subscriber
        /// figure on the panel one too many. Identified by the account it holds rather than by
        /// its code, so a second platform company would be excluded on the same grounds instead
        /// of needing a name added to a list here.
        /// </summary>
        public async Task<List<TenantView>> GetTenantsAsync()
        {
            var companies = await _master.Companies
                .AsNoTracking()
                .Where(c => !_master.AppUsers
                    .Any(u => u.CompanyId == c.CompanyId && u.Role.RoleKey == ErpRoles.SuperAdmin))
                .OrderBy(c => c.CompanyCode)
                .ToListAsync();

            if (companies.Count == 0) return new List<TenantView>();

            var ids = companies.Select(c => c.CompanyId).ToList();

            // One grouped query per related table rather than one per company. A platform with
            // fifty tenants would otherwise open the screen with a hundred round trips.
            var users = await _master.AppUsers
                .AsNoTracking()
                .Where(u => ids.Contains(u.CompanyId))
                .GroupBy(u => u.CompanyId)
                .Select(g => new
                {
                    CompanyId = g.Key,
                    Total = g.Count(),
                    Active = g.Count(u => u.IsActive),
                    LastSignIn = g.Max(u => u.LastLoginAt)
                })
                .ToListAsync();

            var databases = await _master.CompanyDatabases
                .AsNoTracking()
                .Where(d => ids.Contains(d.CompanyId) && d.IsActive)
                .ToListAsync();

            // Ordered ascending so the first match per company, picked in-memory below, is the
            // earliest Admin account the company was ever given - its original owner rather than
            // whichever Admin happens to sort first by name.
            var admins = await _master.AppUsers
                .AsNoTracking()
                .Where(u => ids.Contains(u.CompanyId) && u.Role.RoleKey == ErpRoles.Admin)
                .OrderBy(u => u.CreatedAt)
                .Select(u => new { u.CompanyId, u.FullName, u.Email })
                .ToListAsync();

            var subscriptions = await CurrentSubscriptionsAsync(ids);

            return companies.Select(c =>
            {
                var view = ToView(c);

                var userStats = users.FirstOrDefault(u => u.CompanyId == c.CompanyId);
                view.UserCount = userStats?.Total ?? 0;
                view.ActiveUserCount = userStats?.Active ?? 0;
                view.LastSignIn = userStats?.LastSignIn;

                var admin = admins.FirstOrDefault(a => a.CompanyId == c.CompanyId);
                view.AdminFullName = admin?.FullName ?? "";
                view.AdminEmail = admin?.Email ?? "";

                var database = databases.FirstOrDefault(d => d.CompanyId == c.CompanyId);
                view.HasDatabaseRegistration = database is not null;
                view.ServerName = database?.ServerName ?? "";
                view.DatabaseName = database?.DatabaseName ?? "";

                ApplySubscription(view, subscriptions.FirstOrDefault(s => s.CompanyId == c.CompanyId));

                return view;
            }).ToList();
        }

        public async Task<TenantView?> GetTenantAsync(int companyId)
        {
            var all = await GetTenantsAsync();
            return all.FirstOrDefault(t => t.CompanyId == companyId);
        }

        public async Task<TenantView> CreateTenantAsync(
            string companyCode, string companyName, EnterpriseTier tier,
            string serverName, string databaseName, string credentialKey)
        {
            var code = Clean(companyCode).ToUpperInvariant();
            var name = Clean(companyName);

            if (code.Length == 0) throw new ValidationException("A company code is required.");
            if (name.Length == 0) throw new ValidationException("A company name is required.");

            if (await _master.Companies.AnyAsync(c => c.CompanyCode == code))
            {
                throw new ValidationException($"Company code {code} is already registered.");
            }

            var company = new Company
            {
                CompanyCode = code,
                CompanyName = name,
                EnterpriseTier = tier,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _master.Companies.Add(company);
            await _master.SaveChangesAsync();

            // A tenant with no database registration cannot be served: every request would
            // fail tenant resolution. Registering both together is what makes the tenant real.
            if (!string.IsNullOrWhiteSpace(serverName) && !string.IsNullOrWhiteSpace(databaseName))
            {
                _master.CompanyDatabases.Add(new CompanyDatabase
                {
                    CompanyId = company.CompanyId,
                    ServerName = Clean(serverName),
                    DatabaseName = Clean(databaseName),

                    // The master registry stores a credential *key*, never a password. The
                    // secret lives in server configuration under that key and never leaves it.
                    CredentialKey = Clean(credentialKey),
                    IsActive = true
                });

                await _master.SaveChangesAsync();
            }

            await RecordAsync(AuditActions.Create, company.CompanyId,
                $"Tenant {code} registered on the {tier} plan.");

            return (await GetTenantAsync(company.CompanyId))!;
        }

        public async Task<TenantView?> UpdateTenantAsync(
            int companyId, string companyName, EnterpriseTier tier, bool isActive)
        {
            var company = await _master.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId);
            if (company is null) return null;

            var name = Clean(companyName);
            if (name.Length == 0) throw new ValidationException("A company name is required.");

            company.CompanyName = name;
            company.EnterpriseTier = tier;
            company.IsActive = isActive;

            await _master.SaveChangesAsync();

            return await GetTenantAsync(companyId);
        }

        public async Task<TenantView?> SetTierAsync(int companyId, EnterpriseTier tier, string reason)
        {
            var company = await _master.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId);
            if (company is null) return null;

            var previous = company.EnterpriseTier;

            if (previous == tier)
            {
                throw new ValidationException($"{company.CompanyName} is already on the {tier} plan.");
            }

            company.EnterpriseTier = tier;
            await _master.SaveChangesAsync();

            // This is the single switch that decides which of the nine modules a tenant's users
            // can reach, so it gets its own audit entry rather than being buried in a column
            // diff. Downgrading in particular takes screens away from people mid-session.
            await RecordAsync(AuditActions.StatusChanged, companyId,
                $"{company.CompanyName} moved from {previous} to {tier}" +
                (string.IsNullOrWhiteSpace(reason) ? "." : $": {Clean(reason)}"));

            return await GetTenantAsync(companyId);
        }

        public async Task<TenantView?> SetActiveAsync(int companyId, bool isActive, string reason)
        {
            var company = await _master.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId);
            if (company is null) return null;

            company.IsActive = isActive;
            await _master.SaveChangesAsync();

            await RecordAsync(AuditActions.StatusChanged, companyId,
                $"{company.CompanyName} {(isActive ? "activated" : "deactivated")}" +
                (string.IsNullOrWhiteSpace(reason) ? "." : $": {Clean(reason)}"));

            return await GetTenantAsync(companyId);
        }

        // ================================================================== subscriptions

        public async Task<List<SubscriptionPlanView>> GetPlansAsync()
        {
            await EnsureStandardPlansAsync();

            var plans = await _master.SubscriptionPlans
                .AsNoTracking()
                .OrderBy(p => p.Tier)
                .ThenBy(p => p.PlanName)
                .ToListAsync();

            var counts = await _master.CompanySubscriptions
                .AsNoTracking()
                .Where(s => s.Status == TenantSubscriptionStatuses.Active ||
                            s.Status == TenantSubscriptionStatuses.Trial)
                .GroupBy(s => s.SubscriptionPlanId)
                .Select(g => new { PlanId = g.Key, Count = g.Select(s => s.CompanyId).Distinct().Count() })
                .ToListAsync();

            return plans.Select(p => ToView(
                p, counts.FirstOrDefault(c => c.PlanId == p.SubscriptionPlanId)?.Count ?? 0)).ToList();
        }

        /// <summary>
        /// Every module the given tier includes, read straight from the same catalogue
        /// <c>IsSubmoduleAllowed</c> enforces against. This is deliberately the only place a
        /// plan's module list comes from - a plan row never stores its own copy, so the pricing
        /// page and the actual authorization boundary can never say two different things.
        /// </summary>
        private static List<PlanModuleView> ModulesFor(EnterpriseTier tier) =>
            ErpModules.All
                .Where(m => tier >= m.MinimumTier)
                .Select(m => new PlanModuleView
                {
                    ModuleKey = m.Key,
                    DisplayName = m.DisplayName,
                    Group = m.Group
                })
                .ToList();

        private static SubscriptionPlanView ToView(SubscriptionPlan p, int tenantCount)
        {
            var savings = Math.Max(0m, p.MonthlyPrice * 12m - p.AnnualPrice);
            var yearOfMonthly = p.MonthlyPrice * 12m;

            return new SubscriptionPlanView
            {
                SubscriptionPlanId = p.SubscriptionPlanId,
                PlanCode = p.PlanCode,
                PlanName = p.PlanName,
                Tier = p.Tier.ToString(),
                MonthlyPrice = p.MonthlyPrice,
                AnnualPrice = p.AnnualPrice,
                MaxUsers = p.MaxUsers,
                Description = p.Description,
                Capability = p.Capability,
                IsActive = p.IsActive,
                TenantCount = tenantCount,
                Modules = ModulesFor(p.Tier),
                AnnualSavings = savings,
                AnnualSavingsPercent = yearOfMonthly > 0m
                    ? Math.Round(savings / yearOfMonthly * 100m, 1)
                    : 0m
            };
        }

        public async Task<SubscriptionPlanView> CreatePlanAsync(
            string planCode, string planName, EnterpriseTier tier,
            decimal monthlyPrice, decimal annualPrice, int maxUsers, string description,
            string capability)
        {
            var code = Clean(planCode).ToUpperInvariant();
            var name = Clean(planName);

            if (code.Length == 0) throw new ValidationException("A plan code is required.");
            if (name.Length == 0) throw new ValidationException("A plan name is required.");

            if (monthlyPrice < 0m || annualPrice < 0m)
            {
                throw new ValidationException("A price cannot be negative.");
            }

            if (await _master.SubscriptionPlans.AnyAsync(p => p.PlanCode == code))
            {
                throw new ValidationException($"Plan code {code} is already in use.");
            }

            _master.SubscriptionPlans.Add(new SubscriptionPlan
            {
                PlanCode = code,
                PlanName = name,
                Tier = tier,
                MonthlyPrice = monthlyPrice,
                AnnualPrice = annualPrice,
                MaxUsers = Math.Max(0, maxUsers),
                Description = Clean(description),
                Capability = Clean(capability),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            await _master.SaveChangesAsync();

            var plans = await GetPlansAsync();
            return plans.First(p => p.PlanCode == code);
        }

        public async Task<SubscriptionPlanView?> UpdatePlanAsync(
            int planId, string planName, decimal monthlyPrice, decimal annualPrice,
            int maxUsers, string description, string capability, bool isActive)
        {
            var plan = await _master.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.SubscriptionPlanId == planId);

            if (plan is null) return null;

            var name = Clean(planName);
            if (name.Length == 0) throw new ValidationException("A plan name is required.");

            if (monthlyPrice < 0m || annualPrice < 0m)
            {
                throw new ValidationException("A price cannot be negative.");
            }

            plan.PlanName = name;
            plan.MonthlyPrice = monthlyPrice;
            plan.AnnualPrice = annualPrice;
            plan.MaxUsers = Math.Max(0, maxUsers);
            plan.Description = Clean(description);
            plan.Capability = Clean(capability);
            plan.IsActive = isActive;

            // The plan's tier is deliberately not editable here. Changing it would silently
            // move every tenant on the plan to a different feature set, which is a decision
            // about each of those tenants rather than about the price list.
            await _master.SaveChangesAsync();

            var plans = await GetPlansAsync();
            return plans.FirstOrDefault(p => p.SubscriptionPlanId == planId);
        }

        public async Task<bool> DeletePlanAsync(int planId)
        {
            var plan = await _master.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.SubscriptionPlanId == planId);

            if (plan is null) return false;

            if (await _master.CompanySubscriptions.AnyAsync(s => s.SubscriptionPlanId == planId))
            {
                throw new ValidationException(
                    $"'{plan.PlanName}' has subscription history against it. " +
                    "Deactivate it instead - it will stop being offered and the history stays.");
            }

            _master.SubscriptionPlans.Remove(plan);
            await _master.SaveChangesAsync();

            return true;
        }

        public async Task<List<CompanySubscriptionView>> GetSubscriptionsAsync(
            int? companyId = null, string? status = null)
        {
            var query = _master.CompanySubscriptions
                .AsNoTracking()
                .Include(s => s.Company)
                .Include(s => s.Plan)
                .AsQueryable();

            if (companyId.HasValue) query = query.Where(s => s.CompanyId == companyId.Value);

            if (!string.IsNullOrWhiteSpace(status))
            {
                var trimmed = status.Trim();
                query = query.Where(s => s.Status == trimmed);
            }

            var rows = await query
                .OrderByDescending(s => s.StartDate)
                .ThenByDescending(s => s.CompanySubscriptionId)
                .ToListAsync();

            return rows.Select(ToView).ToList();
        }

        public async Task<CompanySubscriptionView> SubscribeAsync(
            int companyId, int planId, string billingCycle, DateTime startDate, bool autoRenew)
        {
            var company = await _master.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId)
                ?? throw new ValidationException($"No company with id {companyId} exists.");

            var plan = await _master.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.SubscriptionPlanId == planId)
                ?? throw new ValidationException($"No subscription plan with id {planId} exists.");

            if (!plan.IsActive)
            {
                throw new ValidationException($"'{plan.PlanName}' is no longer offered.");
            }

            var cycle = BillingCycles.All.FirstOrDefault(c =>
                string.Equals(c, Clean(billingCycle), StringComparison.OrdinalIgnoreCase))
                ?? BillingCycles.Monthly;

            var start = startDate == default ? DateTime.UtcNow.Date : startDate.Date;

            if (plan.MaxUsers > 0)
            {
                var users = await _master.AppUsers.CountAsync(u => u.CompanyId == companyId && u.IsActive);

                if (users > plan.MaxUsers)
                {
                    throw new ValidationException(
                        $"{company.CompanyName} has {users} active users and '{plan.PlanName}' " +
                        $"allows {plan.MaxUsers}. Deactivate accounts or choose a larger plan.");
                }
            }

            // Whatever term was running is superseded rather than left alongside the new one,
            // or "what is this tenant on?" has two answers.
            var running = await _master.CompanySubscriptions
                .Where(s => s.CompanyId == companyId &&
                            (s.Status == TenantSubscriptionStatuses.Active ||
                             s.Status == TenantSubscriptionStatuses.Trial))
                .ToListAsync();

            foreach (var term in running)
            {
                term.Status = TenantSubscriptionStatuses.Cancelled;
                term.Notes = "Superseded by a new subscription.";
            }

            var subscription = new CompanySubscription
            {
                CompanyId = companyId,
                SubscriptionPlanId = planId,
                StartDate = start,
                EndDate = start.AddMonths(BillingCycles.MonthsIn(cycle)).AddDays(-1),
                BillingCycle = cycle,

                // Copied from the plan rather than referenced, so a later price change does not
                // rewrite what this tenant was historically billed.
                Amount = plan.PriceFor(cycle),

                Status = TenantSubscriptionStatuses.Active,
                AutoRenew = autoRenew,
                CreatedAt = DateTime.UtcNow
            };

            _master.CompanySubscriptions.Add(subscription);

            // Subscribing is what sets the tier: billing and entitlement must not be able to
            // disagree about what a tenant has paid for.
            var previousTier = company.EnterpriseTier;
            company.EnterpriseTier = plan.Tier;

            await _master.SaveChangesAsync();

            await RecordAsync(AuditActions.Create, companyId,
                $"{company.CompanyName} subscribed to {plan.PlanName} ({cycle.ToLowerInvariant()})" +
                (previousTier == plan.Tier ? "." : $", moving from {previousTier} to {plan.Tier}."));

            var saved = await GetSubscriptionsAsync(companyId);
            return saved.First(s => s.CompanySubscriptionId == subscription.CompanySubscriptionId);
        }

        public async Task<CompanySubscriptionView?> RenewSubscriptionAsync(int companySubscriptionId)
        {
            var subscription = await _master.CompanySubscriptions
                .Include(s => s.Plan)
                .Include(s => s.Company)
                .FirstOrDefaultAsync(s => s.CompanySubscriptionId == companySubscriptionId);

            if (subscription is null) return null;

            if (string.Equals(subscription.Status, TenantSubscriptionStatuses.Cancelled,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "This subscription was cancelled. Start a new one rather than renewing it.");
            }

            // Renewing early extends from the existing end date rather than losing paid-for
            // days - the same rule membership renewal follows.
            var renewFrom = subscription.EndDate > DateTime.UtcNow.Date
                ? subscription.EndDate.AddDays(1)
                : DateTime.UtcNow.Date;

            subscription.StartDate = renewFrom;
            subscription.EndDate = renewFrom
                .AddMonths(BillingCycles.MonthsIn(subscription.BillingCycle))
                .AddDays(-1);

            subscription.Status = TenantSubscriptionStatuses.Active;
            subscription.Amount = subscription.Plan.PriceFor(subscription.BillingCycle);

            await _master.SaveChangesAsync();

            await RecordAsync(AuditActions.Update, subscription.CompanyId,
                $"{subscription.Company.CompanyName} renewed to {subscription.EndDate:d MMM yyyy}.");

            var saved = await GetSubscriptionsAsync(subscription.CompanyId);
            return saved.FirstOrDefault(s => s.CompanySubscriptionId == companySubscriptionId);
        }

        public async Task<CompanySubscriptionView?> CancelSubscriptionAsync(
            int companySubscriptionId, string reason)
        {
            var subscription = await _master.CompanySubscriptions
                .Include(s => s.Company)
                .FirstOrDefaultAsync(s => s.CompanySubscriptionId == companySubscriptionId);

            if (subscription is null) return null;

            subscription.Status = TenantSubscriptionStatuses.Cancelled;
            subscription.AutoRenew = false;
            subscription.Notes = Clean(reason);

            await _master.SaveChangesAsync();

            // Cancelling billing does not revoke access. Locking a customer out of their own
            // data is a separate, deliberate act - see SetActiveAsync - so a payment dispute
            // does not become a data outage.
            await RecordAsync(AuditActions.StatusChanged, subscription.CompanyId,
                $"{subscription.Company.CompanyName}'s subscription cancelled" +
                (string.IsNullOrWhiteSpace(reason) ? "." : $": {Clean(reason)}"));

            var saved = await GetSubscriptionsAsync(subscription.CompanyId);
            return saved.FirstOrDefault(s => s.CompanySubscriptionId == companySubscriptionId);
        }

        public async Task<int> ExpireLapsedSubscriptionsAsync()
        {
            var today = DateTime.UtcNow.Date;

            var lapsed = await _master.CompanySubscriptions
                .Where(s => s.EndDate < today &&
                            (s.Status == TenantSubscriptionStatuses.Active ||
                             s.Status == TenantSubscriptionStatuses.Trial))
                .ToListAsync();

            if (lapsed.Count == 0) return 0;

            foreach (var subscription in lapsed)
            {
                subscription.Status = TenantSubscriptionStatuses.Expired;
            }

            await _master.SaveChangesAsync();

            return lapsed.Count;
        }

        // ================================================================== users

        public async Task<List<PlatformUserView>> GetPlatformUsersAsync(
            int? companyId = null, string? search = null)
        {
            var query = _master.AppUsers
                .AsNoTracking()
                .Include(u => u.Company)
                .Include(u => u.Role)
                .AsQueryable();

            if (companyId.HasValue) query = query.Where(u => u.CompanyId == companyId.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                query = query.Where(u =>
                    u.Username.Contains(term) ||
                    u.FullName.Contains(term) ||
                    u.Email.Contains(term));
            }

            var rows = await query
                .OrderBy(u => u.Company.CompanyCode)
                .ThenBy(u => u.Username)
                .ToListAsync();

            return rows.Select(u => new PlatformUserView
            {
                AppUserId = u.AppUserId,
                Username = u.Username,
                FullName = u.FullName,
                Email = u.Email,
                CompanyId = u.CompanyId,
                CompanyName = u.Company?.CompanyName ?? "",
                CompanyCode = u.Company?.CompanyCode ?? "",
                RoleKey = u.Role?.RoleKey ?? "",
                RoleDisplayName = u.Role?.DisplayName ?? "",
                IsActive = u.IsActive,
                MustChangePassword = u.MustChangePassword,
                LastLoginAt = u.LastLoginAt,
                CreatedAt = u.CreatedAt
            }).ToList();
        }

        public async Task<PlatformUserView?> SetUserActiveAsync(int appUserId, bool isActive)
        {
            var user = await _master.AppUsers
                .Include(u => u.Company)
                .FirstOrDefaultAsync(u => u.AppUserId == appUserId);

            if (user is null) return null;

            user.IsActive = isActive;
            await _master.SaveChangesAsync();

            await RecordAsync(AuditActions.StatusChanged, user.CompanyId,
                $"{user.Username} ({user.Company?.CompanyCode}) " +
                $"{(isActive ? "reactivated" : "deactivated")} by the platform administrator.");

            var users = await GetPlatformUsersAsync(user.CompanyId);
            return users.FirstOrDefault(u => u.AppUserId == appUserId);
        }

        public async Task<PlatformUserView?> ResetPasswordAsync(int appUserId, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            {
                throw new ValidationException("A password must be at least 8 characters long.");
            }

            var user = await _master.AppUsers
                .Include(u => u.Company)
                .FirstOrDefaultAsync(u => u.AppUserId == appUserId);

            if (user is null) return null;

            user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);

            // Forced, because the Super Admin now knows this password and the account holder
            // should be the only one who does.
            user.MustChangePassword = true;

            await _master.SaveChangesAsync();

            await RecordAsync(AuditActions.PasswordReset, user.CompanyId,
                $"{user.Username} ({user.Company?.CompanyCode}) had their password reset " +
                "by the platform administrator.");

            var users = await GetPlatformUsersAsync(user.CompanyId);
            return users.FirstOrDefault(u => u.AppUserId == appUserId);
        }

        // ================================================================== settings and health

        public async Task<List<TenantSettingView>> GetPlatformSettingsAsync()
        {
            var stored = await _master.PlatformSettings.AsNoTracking().ToListAsync();

            return PlatformSettingKeys.Catalogue.Select(definition =>
            {
                var row = stored.FirstOrDefault(s =>
                    string.Equals(s.SettingKey, definition.Key, StringComparison.OrdinalIgnoreCase));

                return new TenantSettingView
                {
                    Key = definition.Key,
                    Category = definition.Category,
                    DisplayName = definition.DisplayName,
                    Description = definition.Description,
                    DefaultValue = definition.DefaultValue,
                    Kind = definition.Kind.ToString(),
                    Value = row?.Value ?? definition.DefaultValue,
                    IsOverridden = row is not null,
                    UpdatedBy = row?.UpdatedBy ?? "",
                    UpdatedAt = row?.UpdatedAt ?? row?.CreatedAt
                };
            }).ToList();
        }

        public async Task<List<TenantSettingView>> SetPlatformSettingsAsync(
            IDictionary<string, string> values)
        {
            if (values is null || values.Count == 0) return await GetPlatformSettingsAsync();

            var actor = _actor.Current;

            foreach (var (rawKey, rawValue) in values)
            {
                var definition = PlatformSettingKeys.Find(rawKey)
                    ?? throw new ValidationException($"'{rawKey}' is not a platform setting.");

                var value = ValidateSetting(definition, rawValue);

                var existing = await _master.PlatformSettings
                    .FirstOrDefaultAsync(s => s.SettingKey == definition.Key);

                // Only departures from the default are stored, so changing a shipped default
                // still reaches an installation that never overrode it.
                if (string.Equals(value, definition.DefaultValue, StringComparison.Ordinal))
                {
                    if (existing is not null) _master.PlatformSettings.Remove(existing);
                }
                else if (existing is null)
                {
                    _master.PlatformSettings.Add(new PlatformSetting
                    {
                        SettingKey = definition.Key,
                        Value = value,
                        UpdatedByUserId = actor.AppUserId,
                        UpdatedBy = actor.Username ?? "",
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    existing.Value = value;
                    existing.UpdatedByUserId = actor.AppUserId;
                    existing.UpdatedBy = actor.Username ?? "";
                    existing.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _master.SaveChangesAsync();

            return await GetPlatformSettingsAsync();
        }

        public async Task<List<TenantHealthView>> GetTenantHealthAsync(
            CancellationToken cancellationToken = default)
        {
            var companies = await _master.Companies
                .AsNoTracking()
                .OrderBy(c => c.CompanyCode)
                .Select(c => new { c.CompanyId, c.CompanyName })
                .ToListAsync(cancellationToken);

            var databases = await _master.CompanyDatabases
                .AsNoTracking()
                .Where(d => d.IsActive)
                .ToListAsync(cancellationToken);

            var results = new List<TenantHealthView>();

            foreach (var company in companies)
            {
                var view = new TenantHealthView
                {
                    CompanyId = company.CompanyId,
                    CompanyName = company.CompanyName,
                    DatabaseName = databases
                        .FirstOrDefault(d => d.CompanyId == company.CompanyId)?.DatabaseName ?? "",
                    CheckedAtUtc = DateTime.UtcNow
                };

                try
                {
                    // Sequential rather than parallel: each probe opens a connection to a
                    // different remote server, and a platform with fifty tenants hammering a
                    // shared host all at once is how a health check becomes an outage.
                    var status = await _provisioning.GetStatusAsync(company.CompanyId, cancellationToken);

                    view.CanConnect = status.CanConnect;
                    view.IsUpToDate = status.IsUpToDate;
                    view.PendingMigrations = status.PendingMigrations.Count;
                    view.Problem = status.Problem ?? "";
                }
                catch (Exception ex)
                {
                    view.CanConnect = false;
                    view.Problem = ex.Message;
                }

                results.Add(view);
            }

            return results;
        }

        public async Task<List<AuditEvent>> GetPlatformAuditAsync(
            DateTime? fromUtc = null, DateTime? toUtc = null,
            string? action = null, int? companyId = null, int take = 300)
        {
            var query = _master.AuditEvents.AsNoTracking().AsQueryable();

            if (fromUtc.HasValue) query = query.Where(e => e.OccurredAt >= fromUtc.Value);
            if (toUtc.HasValue) query = query.Where(e => e.OccurredAt < toUtc.Value);
            if (companyId.HasValue) query = query.Where(e => e.CompanyId == companyId.Value);

            if (!string.IsNullOrWhiteSpace(action))
            {
                var trimmed = action.Trim();
                query = query.Where(e => e.Action == trimmed);
            }

            return await query
                .OrderByDescending(e => e.OccurredAt)
                .Take(Math.Clamp(take, 1, 2000))
                .ToListAsync();
        }

        // ================================================================== helpers

        /// <summary>
        /// Creates the three FitCore plans if the installation has none.
        ///
        /// Without them the Super Admin's first act would have to be typing in a price list,
        /// and the tiers the product already enforces would have no billing counterpart.
        /// </summary>
        private async Task EnsureStandardPlansAsync()
        {
            if (await _master.SubscriptionPlans.AnyAsync()) return;

            _master.SubscriptionPlans.AddRange(
                new SubscriptionPlan
                {
                    PlanCode = "MICRO",
                    PlanName = "Micro Gym",
                    Tier = EnterpriseTier.Micro,
                    MonthlyPrice = 399m,
                    AnnualPrice = 3990m,
                    MaxUsers = 0,
                    Description = "2 active modules: Membership Management and Payment Management.",
                    Capability = "Essential front-desk client management and payment tracking " +
                                 "for a single facility.",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                new SubscriptionPlan
                {
                    PlanCode = "SMALL",
                    PlanName = "Small Gym",
                    Tier = EnterpriseTier.Small,
                    MonthlyPrice = 1499m,
                    AnnualPrice = 14990m,
                    MaxUsers = 0,
                    Description = "8 active modules: Membership, Payments, Sales, Inventory, " +
                                  "Employees, Payroll, Finance and Business Intelligence.",
                    Capability = "Full business operations including back-office staff tracking, " +
                                 "inventory, payroll, and financials.",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                new SubscriptionPlan
                {
                    PlanCode = "MEDIUM",
                    PlanName = "Medium Gym",
                    Tier = EnterpriseTier.Medium,
                    MonthlyPrice = 2999m,
                    AnnualPrice = 29990m,
                    MaxUsers = 0,
                    Description = "9 of 9 modules: everything in Small, plus System Administration.",
                    Capability = "Enterprise control with multi-branch networking, advanced " +
                                 "configurations, and Super Admin access.",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });

            await _master.SaveChangesAsync();
        }

        private async Task<List<CompanySubscription>> CurrentSubscriptionsAsync(List<int> companyIds)
        {
            var rows = await _master.CompanySubscriptions
                .AsNoTracking()
                .Include(s => s.Plan)
                .Where(s => companyIds.Contains(s.CompanyId))
                .ToListAsync();

            // The most recent term per tenant, preferring one that is still live. A cancelled
            // term from last year must not be what the tenant list reports.
            return rows
                .GroupBy(s => s.CompanyId)
                .Select(g => g
                    .OrderByDescending(s => TenantSubscriptionStatuses.PermitsAccess(s.Status))
                    .ThenByDescending(s => s.EndDate)
                    .First())
                .ToList();
        }

        private static void ApplySubscription(TenantView view, CompanySubscription? subscription)
        {
            if (subscription is null)
            {
                view.SubscriptionStatus = "None";
                return;
            }

            view.SubscriptionPlan = subscription.Plan?.PlanName ?? "";
            view.SubscriptionStatus = subscription.Status;
            view.SubscriptionEnds = subscription.EndDate;
            view.SubscriptionAmount = subscription.Amount;
            view.DaysUntilRenewal = (int)(subscription.EndDate.Date - DateTime.UtcNow.Date).TotalDays;
        }

        private static string ValidateSetting(TenantSettingDefinition definition, string? raw)
        {
            var value = (raw ?? "").Trim();

            return definition.Kind switch
            {
                TenantSettingKind.Number =>
                    decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
                        && number >= 0m
                        ? number.ToString(CultureInfo.InvariantCulture)
                        : throw new ValidationException(
                            $"{definition.DisplayName} must be a number that is not negative."),

                TenantSettingKind.Boolean =>
                    bool.TryParse(value, out var flag)
                        ? flag ? "true" : "false"
                        : throw new ValidationException($"{definition.DisplayName} must be on or off."),

                _ => value.Length <= 1000
                    ? value
                    : throw new ValidationException(
                        $"{definition.DisplayName} is too long. Keep it under 1000 characters.")
            };
        }

        /// <summary>
        /// Platform actions are recorded in the master trail, not in a tenant's.
        ///
        /// Registering a company, moving its tier or resetting somebody's password are things
        /// done *to* a tenant rather than *by* one, and burying them in that tenant's own audit
        /// log would both hide them from the platform record and show a gym administrative
        /// actions they did not take.
        /// </summary>
        private Task RecordAsync(string action, int? companyId, string summary)
        {
            var actor = _actor.Current;

            return _audit.RecordAsync(
                action,
                actor with { CompanyId = companyId ?? actor.CompanyId },
                summary);
        }

        private static TenantView ToView(Company c) => new()
        {
            CompanyId = c.CompanyId,
            CompanyCode = c.CompanyCode,
            CompanyName = c.CompanyName,
            IsActive = c.IsActive,
            EnterpriseTier = c.EnterpriseTier.ToString(),
            CreatedAt = c.CreatedAt
        };

        private static CompanySubscriptionView ToView(CompanySubscription s) => new()
        {
            CompanySubscriptionId = s.CompanySubscriptionId,
            CompanyId = s.CompanyId,
            CompanyName = s.Company?.CompanyName ?? "",
            SubscriptionPlanId = s.SubscriptionPlanId,
            PlanName = s.Plan?.PlanName ?? "",
            Tier = s.Plan?.Tier.ToString() ?? "",
            StartDate = s.StartDate,
            EndDate = s.EndDate,
            BillingCycle = s.BillingCycle,
            Amount = s.Amount,
            Status = s.Status,
            AutoRenew = s.AutoRenew,
            Notes = s.Notes,
            DaysRemaining = (int)(s.EndDate.Date - DateTime.UtcNow.Date).TotalDays
        };

        private static string Clean(string? value) => (value ?? "").Trim();
    }
}
