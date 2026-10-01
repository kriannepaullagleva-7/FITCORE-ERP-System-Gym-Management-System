using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// Platform-wide Business Intelligence: how FitCore itself is doing.
    ///
    /// Every figure comes from the master database - companies, subscriptions, accounts and the
    /// platform audit trail. None of it opens a tenant database, and none of it reports a
    /// tenant's business data. "Platform revenue" here means what FitCore bills its customers,
    /// not what its customers take at their tills, and the two must never be confused: one is
    /// the platform's, the other belongs to the gym.
    /// </summary>
    public interface IPlatformAnalyticsService
    {
        /// <summary>The Super Admin's dashboard: tenants, subscriptions, users and activity.</summary>
        Task<AnalyticsView> GetPlatformDashboardAsync(DateTime? fromUtc, DateTime? toUtc);

        /// <summary>Growth, tier mix, subscription revenue and usage over time.</summary>
        Task<AnalyticsView> GetPlatformAnalyticsAsync(DateTime? fromUtc, DateTime? toUtc);
    }

    public class PlatformAnalyticsService : IPlatformAnalyticsService
    {
        private readonly MasterErpDbContext _master;
        private readonly IPlatformAdminService _platform;

        public PlatformAnalyticsService(
            MasterErpDbContext master, IPlatformAdminService platform)
        {
            _master = master;
            _platform = platform;
        }

        public async Task<AnalyticsView> GetPlatformDashboardAsync(DateTime? fromUtc, DateTime? toUtc)
        {
            var window = Resolve(fromUtc, toUtc);

            // Terms that have run out are marked before anything is counted, or "active
            // subscriptions" includes ones that lapsed last month.
            await _platform.ExpireLapsedSubscriptionsAsync();

            var view = NewView("platform-dashboard", window);
            view.Title = "Platform Dashboard";
            view.Description = "Tenants, subscriptions, accounts and activity across the whole installation.";

            view.Groups.Add(await TenantKpisAsync(window));
            view.Groups.Add(await SubscriptionKpisAsync(window));
            view.Groups.Add(await UsageKpisAsync(window));

            view.Charts.Add(await CompaniesByTierAsync());
            view.Charts.Add(await SubscriptionStatusAsync());
            view.Charts.Add(await CompanyGrowthAsync(window));

            var tenants = await _platform.GetTenantsAsync();

            view.Tables.Add(new AnalyticsTable
            {
                Key = "tenants",
                Title = "Tenants",
                Columns = new List<string>
                {
                    "Code", "Company", "Tier", "Status", "Users", "Plan", "Renews", "Last sign-in"
                },
                Rows = tenants.Select(t => new List<string>
                {
                    t.CompanyCode,
                    t.CompanyName,
                    t.EnterpriseTier,
                    t.IsActive ? "Active" : "Inactive",
                    t.ActiveUserCount.ToString("N0"),
                    string.IsNullOrWhiteSpace(t.SubscriptionPlan) ? "—" : t.SubscriptionPlan,
                    t.SubscriptionEnds?.ToString("d MMM yyyy") ?? "—",
                    t.LastSignIn?.ToString("d MMM yyyy") ?? "never"
                }).ToList()
            });

            return view;
        }

        public async Task<AnalyticsView> GetPlatformAnalyticsAsync(DateTime? fromUtc, DateTime? toUtc)
        {
            var window = Resolve(fromUtc, toUtc);

            await _platform.ExpireLapsedSubscriptionsAsync();

            var view = NewView("platform-analytics", window);
            view.Title = "Platform Analytics";
            view.Description = "Company growth, tier mix, subscription revenue and usage.";

            view.Groups.Add(await TenantKpisAsync(window));
            view.Groups.Add(await SubscriptionKpisAsync(window));

            view.Charts.Add(await CompanyGrowthAsync(window));
            view.Charts.Add(await SubscriptionRevenueAsync(window));
            view.Charts.Add(await CompaniesByTierAsync());
            view.Charts.Add(await SubscriptionStatusAsync());
            view.Charts.Add(await PlatformActivityAsync(window));

            var expiring = await _master.CompanySubscriptions
                .AsNoTracking()
                .Include(s => s.Company)
                .Include(s => s.Plan)
                .Where(s => s.Status == TenantSubscriptionStatuses.Active &&
                            s.EndDate >= DateTime.UtcNow.Date &&
                            s.EndDate <= DateTime.UtcNow.Date.AddDays(60))
                .OrderBy(s => s.EndDate)
                .ToListAsync();

            view.Tables.Add(new AnalyticsTable
            {
                Key = "expiring-subscriptions",
                Title = "Subscriptions expiring in the next 60 days",
                Columns = new List<string> { "Company", "Plan", "Cycle", "Ends", "Amount", "Auto-renew" },
                Rows = expiring.Select(s => new List<string>
                {
                    s.Company?.CompanyName ?? "",
                    s.Plan?.PlanName ?? "",
                    s.BillingCycle,
                    s.EndDate.ToString("d MMM yyyy"),
                    s.Amount.ToString("N2"),
                    s.AutoRenew ? "Yes" : "No"
                }).ToList()
            });

            return view;
        }

        // ================================================================== KPI groups

        private async Task<KpiGroup> TenantKpisAsync(AnalyticsWindow window)
        {
            var companies = await _master.Companies
                .AsNoTracking()
                .Select(c => new { c.CompanyId, c.IsActive, c.EnterpriseTier, c.CreatedAt })
                .ToListAsync();

            var newInWindow = companies.Count(c =>
                c.CreatedAt >= window.FromUtc && c.CreatedAt < window.ToUtc);

            var newInPrevious = companies.Count(c =>
                c.CreatedAt >= window.ComparisonFromUtc && c.CreatedAt < window.ComparisonToUtc);

            return new KpiGroup
            {
                Key = "tenants",
                Title = "Tenants",
                Cards = new List<KpiCard>
                {
                    Count("total-companies", "Total companies",
                        companies.Count, companies.Count - newInWindow),
                    Count("active-companies", "Active companies",
                        companies.Count(c => c.IsActive), companies.Count(c => c.IsActive)),
                    Count("inactive-companies", "Inactive companies",
                        companies.Count(c => !c.IsActive), companies.Count(c => !c.IsActive),
                        riseIsGood: false),
                    Count("new-companies", "New companies", newInWindow, newInPrevious),
                    Count("medium-tenants", "On the Medium plan",
                        companies.Count(c => c.EnterpriseTier == EnterpriseTier.Medium),
                        companies.Count(c => c.EnterpriseTier == EnterpriseTier.Medium))
                }
            };
        }

        private async Task<KpiGroup> SubscriptionKpisAsync(AnalyticsWindow window)
        {
            var today = DateTime.UtcNow.Date;

            var subscriptions = await _master.CompanySubscriptions
                .AsNoTracking()
                .Select(s => new { s.Status, s.EndDate, s.StartDate, s.Amount, s.BillingCycle })
                .ToListAsync();

            var active = subscriptions.Count(s => s.Status == TenantSubscriptionStatuses.Active);

            // Recurring revenue normalised to a month, so an annual plan and a monthly one can
            // be added together without one of them being twelve times the other.
            var monthly = subscriptions
                .Where(s => s.Status == TenantSubscriptionStatuses.Active)
                .Sum(s => s.Amount / BillingCycles.MonthsIn(s.BillingCycle));

            var billedInWindow = subscriptions
                .Where(s => s.StartDate >= window.FromUtc && s.StartDate < window.ToUtc)
                .Sum(s => s.Amount);

            var billedPreviously = subscriptions
                .Where(s => s.StartDate >= window.ComparisonFromUtc &&
                            s.StartDate < window.ComparisonToUtc)
                .Sum(s => s.Amount);

            return new KpiGroup
            {
                Key = "subscriptions",
                Title = "Subscriptions",
                Cards = new List<KpiCard>
                {
                    Count("active-subscriptions", "Active subscriptions", active, active),
                    Count("trial-subscriptions", "On trial",
                        subscriptions.Count(s => s.Status == TenantSubscriptionStatuses.Trial),
                        subscriptions.Count(s => s.Status == TenantSubscriptionStatuses.Trial)),
                    Count("expiring-subscriptions", "Expiring within 30 days",
                        subscriptions.Count(s => s.Status == TenantSubscriptionStatuses.Active &&
                                                 s.EndDate >= today && s.EndDate <= today.AddDays(30)),
                        0, riseIsGood: false),
                    Count("expired-subscriptions", "Expired",
                        subscriptions.Count(s => s.Status == TenantSubscriptionStatuses.Expired),
                        subscriptions.Count(s => s.Status == TenantSubscriptionStatuses.Expired),
                        riseIsGood: false),
                    Money("subscription-revenue", "Billed this period", billedInWindow, billedPreviously),
                    Money("recurring-revenue", "Monthly recurring revenue", monthly, monthly)
                }
            };
        }

        private async Task<KpiGroup> UsageKpisAsync(AnalyticsWindow window)
        {
            var users = await _master.AppUsers
                .AsNoTracking()
                .Select(u => new { u.IsActive, u.LastLoginAt, u.CreatedAt })
                .ToListAsync();

            var events = await _master.AuditEvents
                .AsNoTracking()
                .Where(e => e.OccurredAt >= window.FromUtc && e.OccurredAt < window.ToUtc)
                .Select(e => new { e.Action })
                .ToListAsync();

            var previousEvents = await _master.AuditEvents
                .AsNoTracking()
                .CountAsync(e => e.OccurredAt >= window.ComparisonFromUtc &&
                                 e.OccurredAt < window.ComparisonToUtc);

            var signedIn = users.Count(u =>
                u.LastLoginAt >= window.FromUtc && u.LastLoginAt < window.ToUtc);

            return new KpiGroup
            {
                Key = "usage",
                Title = "Platform usage",
                Cards = new List<KpiCard>
                {
                    Count("total-users", "Accounts", users.Count, users.Count),
                    Count("active-users", "Active accounts",
                        users.Count(u => u.IsActive), users.Count(u => u.IsActive)),
                    Count("signed-in", "Signed in this period", signedIn, signedIn),
                    Count("platform-events", "Platform events", events.Count, previousEvents),
                    Count("failed-sign-ins", "Failed sign-ins",
                        events.Count(e => e.Action == AuditActions.LoginFailed), 0, riseIsGood: false)
                }
            };
        }

        // ================================================================== charts

        private async Task<ChartDefinition> CompaniesByTierAsync()
        {
            var rows = await _master.Companies
                .AsNoTracking()
                .GroupBy(c => c.EnterpriseTier)
                .Select(g => new { Tier = g.Key, Count = g.Count() })
                .ToListAsync();

            var ordered = rows.OrderBy(r => r.Tier).ToList();

            return new ChartDefinition
            {
                Key = "companies-by-tier",
                Title = "Companies by plan",
                Caption = "Which tier each tenant is licensed for.",
                ChartType = ChartTypes.Donut,
                ValueFormat = KpiFormats.Number,
                Labels = ordered.Select(r => r.Tier.ToString()).ToList(),
                Series = new List<ChartSeries>
                {
                    new() { Name = "Companies", Values = ordered.Select(r => (decimal)r.Count).ToList() }
                }
            };
        }

        private async Task<ChartDefinition> SubscriptionStatusAsync()
        {
            var rows = await _master.CompanySubscriptions
                .AsNoTracking()
                .GroupBy(s => s.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToListAsync();

            return new ChartDefinition
            {
                Key = "subscription-status",
                Title = "Subscriptions by status",
                Caption = "Every term on record, live or otherwise.",
                ChartType = ChartTypes.Donut,
                ValueFormat = KpiFormats.Number,
                Labels = rows.Select(r => r.Status).ToList(),
                Series = new List<ChartSeries>
                {
                    new() { Name = "Subscriptions", Values = rows.Select(r => (decimal)r.Count).ToList() }
                }
            };
        }

        private async Task<ChartDefinition> CompanyGrowthAsync(AnalyticsWindow window)
        {
            var months = window.TrailingMonths(12);

            var companies = await _master.Companies
                .AsNoTracking()
                .Select(c => new { c.CreatedAt })
                .ToListAsync();

            var registered = months
                .Select(m => (decimal)companies.Count(c =>
                    c.CreatedAt.Year == m.Year && c.CreatedAt.Month == m.Month))
                .ToList();

            // The cumulative line is what actually answers "is the platform growing?" - a bar
            // per month shows recruitment, not size.
            var running = 0m;
            var cumulative = new List<decimal>();

            var before = companies.Count(c => c.CreatedAt < months[0]);
            running = before;

            foreach (var count in registered)
            {
                running += count;
                cumulative.Add(running);
            }

            return new ChartDefinition
            {
                Key = "company-growth",
                Title = "Company growth",
                Caption = "New tenants each month, and the total on the platform.",
                ChartType = ChartTypes.Bar,
                ValueFormat = KpiFormats.Number,
                Labels = months.Select(m => m.ToString("MMM yy")).ToList(),
                Series = new List<ChartSeries>
                {
                    new() { Name = "New tenants", Values = registered },
                    new() { Name = "Total tenants", Values = cumulative }
                }
            };
        }

        private async Task<ChartDefinition> SubscriptionRevenueAsync(AnalyticsWindow window)
        {
            var months = window.TrailingMonths(12);

            var subscriptions = await _master.CompanySubscriptions
                .AsNoTracking()
                .Include(s => s.Plan)
                .Where(s => s.StartDate >= months[0])
                .Select(s => new { s.StartDate, s.Amount, Tier = s.Plan.Tier })
                .ToListAsync();

            var series = new List<ChartSeries>();

            foreach (var tier in new[] { EnterpriseTier.Micro, EnterpriseTier.Small, EnterpriseTier.Medium })
            {
                series.Add(new ChartSeries
                {
                    Name = tier.ToString(),
                    Values = months.Select(m => subscriptions
                        .Where(s => s.Tier == tier &&
                                    s.StartDate.Year == m.Year && s.StartDate.Month == m.Month)
                        .Sum(s => s.Amount)).ToList()
                });
            }

            return new ChartDefinition
            {
                Key = "subscription-revenue",
                Title = "Subscription revenue by plan",
                Caption = "What FitCore billed its tenants, by the plan they were on. " +
                          "This is the platform's revenue, not the gyms'.",
                ChartType = ChartTypes.StackedBar,
                ValueFormat = KpiFormats.Money,
                Labels = months.Select(m => m.ToString("MMM yy")).ToList(),
                Series = series
            };
        }

        private async Task<ChartDefinition> PlatformActivityAsync(AnalyticsWindow window)
        {
            var events = await _master.AuditEvents
                .AsNoTracking()
                .Where(e => e.OccurredAt >= window.FromUtc && e.OccurredAt < window.ToUtc)
                .Select(e => new { e.OccurredAt, e.Action })
                .ToListAsync();

            var days = window.Days();

            return new ChartDefinition
            {
                Key = "platform-activity",
                Title = "Platform activity",
                Caption = "Sign-ins against failed attempts, from the master audit trail.",
                ChartType = ChartTypes.Line,
                ValueFormat = KpiFormats.Number,
                Labels = days.Select(d => d.ToString("d MMM")).ToList(),
                Series = new List<ChartSeries>
                {
                    new()
                    {
                        Name = "Sign-ins",
                        Values = days.Select(d => (decimal)events.Count(e =>
                            e.OccurredAt.Date == d && e.Action == AuditActions.Login)).ToList()
                    },
                    new()
                    {
                        Name = "Failed attempts",
                        Values = days.Select(d => (decimal)events.Count(e =>
                            e.OccurredAt.Date == d && e.Action == AuditActions.LoginFailed)).ToList()
                    }
                }
            };
        }

        // ================================================================== plumbing

        private sealed record AnalyticsWindow(
            DateTime FromUtc, DateTime ToUtc, DateTime ComparisonFromUtc, DateTime ComparisonToUtc)
        {
            public List<DateTime> Days()
            {
                var days = new List<DateTime>();
                var total = (int)(ToUtc - FromUtc).TotalDays;
                var start = total > 90 ? ToUtc.AddDays(-90) : FromUtc;

                for (var day = start.Date; day < ToUtc.Date; day = day.AddDays(1)) days.Add(day);

                return days.Count == 0 ? new List<DateTime> { FromUtc.Date } : days;
            }

            public List<DateTime> TrailingMonths(int count)
            {
                var last = new DateTime(ToUtc.Year, ToUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                if (ToUtc.Day == 1) last = last.AddMonths(-1);

                return Enumerable.Range(0, count)
                    .Select(offset => last.AddMonths(-(count - 1 - offset)))
                    .ToList();
            }
        }

        private static AnalyticsWindow Resolve(DateTime? from, DateTime? to)
        {
            var range = ReportRange.Resolve(from, to);
            var length = range.ToUtc - range.FromUtc;

            if (length <= TimeSpan.Zero) length = TimeSpan.FromDays(1);

            return new AnalyticsWindow(
                range.FromUtc, range.ToUtc, range.FromUtc - length, range.FromUtc);
        }

        private static AnalyticsView NewView(string key, AnalyticsWindow window) => new()
        {
            Key = key,
            FromUtc = window.FromUtc,
            ToUtc = window.ToUtc,
            ComparisonFromUtc = window.ComparisonFromUtc,
            ComparisonToUtc = window.ComparisonToUtc
        };

        private static KpiCard Card(
            string key, string label, decimal value, decimal previous, string format, bool riseIsGood)
        {
            var hasComparison = previous != 0m;

            return new KpiCard
            {
                Key = key,
                Label = label,
                Value = value,
                PreviousValue = previous,
                Format = format,
                RiseIsGood = riseIsGood,
                HasComparison = hasComparison,
                DeltaPercent = hasComparison
                    ? Math.Round((value - previous) / Math.Abs(previous) * 100m, 1)
                    : 0m
            };
        }

        private static KpiCard Count(
            string key, string label, int value, int previous, bool riseIsGood = true) =>
            Card(key, label, value, previous, KpiFormats.Number, riseIsGood);

        private static KpiCard Money(
            string key, string label, decimal value, decimal previous, bool riseIsGood = true) =>
            Card(key, label, value, previous, KpiFormats.Money, riseIsGood);
    }
}
