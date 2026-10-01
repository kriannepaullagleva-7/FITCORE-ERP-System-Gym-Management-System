using System.ComponentModel;
using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Tenant management: the companies on the platform, what each is licensed for, and
    /// whether their database is reachable.
    ///
    /// A subfeature of System Administration reserved for the Super Admin. Nothing here reads
    /// a tenant's business data - a gym's members and takings are theirs, and the platform
    /// administers tenants without reading inside them.
    /// </summary>
    internal sealed class TenantsPage : CrudPageBase<TenantDto>
    {
        private readonly Button _toggleActive;
        private readonly Label _total;
        private readonly Label _totalHint;
        private readonly Label _active;
        private readonly Label _activeHint;
        private readonly Label _users;
        private readonly Label _usersHint;
        private readonly Label _renewals;
        private readonly Label _renewalsHint;

        public TenantsPage(FitCoreSession session)
            : base(session, "Tenants",
                   "Every company on this installation, what they are licensed for, and where their data lives.",
                   "tenant", "Code, company, tier or plan")
        {
            AddRowAction("View details", ButtonTone.Secondary, ViewDetailsAsync, 100);
            _toggleActive = AddRowAction("Deactivate", ButtonTone.Secondary, ToggleActiveAsync, 110);
            AddRowAction("Provision", ButtonTone.Secondary, ProvisionAsync, 100);

            // Health probes every tenant at once, so it needs no row selected.
            AddAction("Health", ButtonTone.Secondary, ShowHealthAsync, 84);

            StatsRow.Controls.Add(UiKit.StatCard("Companies", out _total, out _totalHint, UiTheme.Primary));
            StatsRow.Controls.Add(UiKit.StatCard("Active", out _active, out _activeHint, UiTheme.Success));
            StatsRow.Controls.Add(UiKit.StatCard("Accounts", out _users, out _usersHint, UiTheme.Info));
            StatsRow.Controls.Add(UiKit.StatCard("Renewing soon", out _renewals, out _renewalsHint, UiTheme.Warning));
        }

        protected override bool SupportsDelete => false;

        /// <summary>
        /// The one button whose wording has to follow the row: labelling it "Activate" while it
        /// is about to deactivate a live company is how an operator locks a paying tenant's
        /// users out believing they were turning them on.
        /// </summary>
        protected override void OnSelectionChanged() =>
            _toggleActive.Text = Selected is { IsActive: false } ? "Activate" : "Deactivate";

        protected override string EmptyHeadline => "No tenants registered";
        protected override string EmptyDetail =>
            "Register a company and point it at its database. Until then nobody can sign in to it.";

        protected override async Task<List<TenantDto>?> FetchAsync()
        {
            var tenants = Unwrap(await Session.Platform.GetTenantsAsync());

            if (tenants is not null)
            {
                _total.Text = tenants.Count.ToString("N0");
                _totalHint.Text = $"{tenants.Count(t => t.EnterpriseTier == "Medium")} on Medium";

                _active.Text = tenants.Count(t => t.IsActive).ToString("N0");
                _activeHint.Text = "able to sign in";

                _users.Text = tenants.Sum(t => t.ActiveUserCount).ToString("N0");
                _usersHint.Text = $"{tenants.Sum(t => t.UserCount)} in total";

                var soon = tenants.Count(t => t.DaysUntilRenewal is >= 0 and <= 30);
                _renewals.Text = soon.ToString("N0");
                _renewals.ForeColor = soon > 0 ? UiTheme.Warning : UiTheme.TextPrimary;
                _renewalsHint.Text = soon == 0 ? "nothing due" : "within 30 days";
            }

            return tenants;
        }

        protected override void DefineColumns()
        {
            Column(nameof(TenantDto.CompanyCode), "Code", 65);

            // "Client Name" rather than "Company": same binding, same data (the company's own
            // name) - the Super Admin's tenants are its paying clients, and the column header
            // says so instead of restating "Company" next to a Code column that already does.
            Column(nameof(TenantDto.CompanyName), "Client Name", 170);

            StatusColumn(nameof(TenantDto.EnterpriseTier), "Tier", 70);
            Column(nameof(TenantDto.ActiveUserCount), "Users", 50, rightAlign: true);
            Column(nameof(TenantDto.AdminFullName), "Admin", 150);
            Column(nameof(TenantDto.AdminEmail), "Admin email", 190);
            Column(nameof(TenantDto.SubscriptionPlan), "Plan", 110);
            StatusColumn(nameof(TenantDto.SubscriptionStatus), "Subscription", 90);
            DateColumn(nameof(TenantDto.SubscriptionEnds), "Renews", 80);
            Column(nameof(TenantDto.LastSignIn), "Last sign-in", 100, "d MMM yyyy");
        }

        protected override bool Matches(TenantDto t, string term) =>
            t.CompanyCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            t.CompanyName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            t.EnterpriseTier.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            t.SubscriptionPlan.Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override Task<bool> OnAddAsync()
        {
            var saved = EditDialog.Run(this, "Register a tenant",
                "The credential key names an entry in the server's own configuration - the " +
                "master registry never stores a password.",
                new List<FieldSpec>
                {
                    new("code", "Company code")
                        { Required = true, MaxLength = 50, Hint = "Unique. Shown on every screen." },
                    new("name", "Company name") { Required = true, MaxLength = 200 },
                    new("tier", "Enterprise tier", FieldKind.Combo)
                    {
                        Required = true,
                        Value = EnterpriseTiers.Micro,
                        Options = EnterpriseTiers.All
                            .Select(t => new KeyValuePair<string, string>(t, t + " Enterprise"))
                            .ToList()
                    },
                    new("server", "Database server") { MaxLength = 200 },
                    new("database", "Database name") { MaxLength = 200 },
                    new("credential", "Credential key")
                    {
                        MaxLength = 100,
                        Hint = "Matches an entry under TenantCredentials in server configuration."
                    }
                },
                async f =>
                {
                    var result = await Session.Platform.CreateTenantAsync(new CreateTenantDto
                    {
                        CompanyCode = f.First(x => x.Key == "code").Text,
                        CompanyName = f.First(x => x.Key == "name").Text,
                        Tier = EnterpriseTiers.ValueOf(f.First(x => x.Key == "tier").Text),
                        ServerName = f.First(x => x.Key == "server").Text,
                        DatabaseName = f.First(x => x.Key == "database").Text,
                        CredentialKey = f.First(x => x.Key == "credential").Text
                    });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Register tenant");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(TenantDto tenant)
        {
            var saved = EditDialog.Run(this, $"Edit {tenant.CompanyName}",
                "Moving the tier down takes screens away from their users; moving it up gives " +
                "them new ones. Either way it takes effect the next time each of them loads a " +
                "page, and a reason is kept on record for it.",
                new List<FieldSpec>
                {
                    new("code", "Company code") { Value = tenant.CompanyCode, ReadOnly = true },
                    new("name", "Company name")
                        { Required = true, Value = tenant.CompanyName, MaxLength = 200 },
                    new("tier", "Enterprise tier", FieldKind.Combo)
                    {
                        Required = true,
                        Value = tenant.EnterpriseTier,
                        Options = EnterpriseTiers.All
                            .Select(t => new KeyValuePair<string, string>(t, t + " Enterprise"))
                            .ToList()
                    },
                    new("reason", "Why the tier is changing", FieldKind.Multiline)
                    {
                        MaxLength = 300,
                        Hint = "Only needed if you change the tier above."
                    },
                    new("active", "Active", FieldKind.Check) { Value = tenant.IsActive }
                },
                async f =>
                {
                    var currentTier = EnterpriseTiers.ValueOf(tenant.EnterpriseTier);
                    var newTier = EnterpriseTiers.ValueOf(f.First(x => x.Key == "tier").Text);
                    var reason = f.First(x => x.Key == "reason").Text;

                    if (newTier != currentTier && string.IsNullOrWhiteSpace(reason))
                    {
                        return "Say why the tier is changing.";
                    }

                    // Name/active go through the ordinary update; the tier itself always goes
                    // through SetTierAsync instead, since that is the one path that keeps the
                    // reason on record - UpdateTenantAsync is sent the unchanged tier so it
                    // cannot race the dedicated call into writing it without a reason.
                    var result = await Session.Platform.UpdateTenantAsync(
                        tenant.CompanyId, new UpdateTenantDto
                        {
                            CompanyName = f.First(x => x.Key == "name").Text,
                            Tier = currentTier,
                            IsActive = f.First(x => x.Key == "active").Flag
                        });

                    if (!result.IsSuccess) return result.ErrorMessage;

                    if (newTier != currentTier)
                    {
                        var tierResult = await Session.Platform.SetTierAsync(
                            tenant.CompanyId, newTier, reason);

                        if (!tierResult.IsSuccess) return tierResult.ErrorMessage;
                    }

                    return null;
                }, "Save changes");

            return Task.FromResult(saved);
        }

        protected override Task<string?> OnDeleteAsync(TenantDto tenant) =>
            Task.FromResult<string?>(
                "A tenant is never deleted - their data and their billing history outlive the " +
                "relationship. Deactivate them instead.");

        private Task ViewDetailsAsync()
        {
            var tenant = Selected;

            if (tenant is null)
            {
                ShowError("Select a tenant to view.");
                return Task.CompletedTask;
            }

            ListDialog.Show(this, $"{tenant.CompanyName} ({tenant.CompanyCode})",
                $"{tenant.EnterpriseTier} Enterprise · {tenant.Status}",
                new List<object>
                {
                    new
                    {
                        Field = "Admin",
                        Value = string.IsNullOrWhiteSpace(tenant.AdminFullName)
                            ? "— no account yet —" : tenant.AdminFullName
                    },
                    new
                    {
                        Field = "Admin email",
                        Value = string.IsNullOrWhiteSpace(tenant.AdminEmail) ? "—" : tenant.AdminEmail
                    },
                    new { Field = "Accounts", Value = $"{tenant.ActiveUserCount} active of {tenant.UserCount} total" },
                    new { Field = "Database registered", Value = tenant.HasDatabaseRegistration ? "Yes" : "No" },
                    new { Field = "Server", Value = string.IsNullOrWhiteSpace(tenant.ServerName) ? "—" : tenant.ServerName },
                    new { Field = "Database", Value = string.IsNullOrWhiteSpace(tenant.DatabaseName) ? "—" : tenant.DatabaseName },
                    new { Field = "Subscription plan", Value = string.IsNullOrWhiteSpace(tenant.SubscriptionPlan) ? "—" : tenant.SubscriptionPlan },
                    new { Field = "Subscription status", Value = tenant.SubscriptionStatus },
                    new { Field = "Renews", Value = tenant.SubscriptionEnds.HasValue ? UiKit.Date(tenant.SubscriptionEnds.Value) : "—" },
                    new { Field = "Last sign-in", Value = tenant.LastSignIn.HasValue ? UiKit.Date(tenant.LastSignIn.Value) : "Never" },
                    new { Field = "Registered", Value = UiKit.Date(tenant.CreatedAt) }
                });

            return Task.CompletedTask;
        }

        private async Task ToggleActiveAsync()
        {
            var tenant = Selected;

            if (tenant is null)
            {
                ShowError("Select a tenant to activate or deactivate.");
                return;
            }

            var activating = !tenant.IsActive;

            if (!activating && !UiKit.ConfirmDelete(this,
                    $"Deactivate {tenant.CompanyName}?",
                    $"All {tenant.ActiveUserCount} of their accounts will be refused at sign-in " +
                    "immediately. Their data is untouched and the company can be reactivated.",
                    "Deactivate"))
            {
                return;
            }

            var reason = "";

            if (!activating)
            {
                var given = EditDialog.Run(this, $"Deactivate {tenant.CompanyName}", "",
                    new List<FieldSpec>
                    {
                        new("reason", "Why?", FieldKind.Multiline) { Required = true, MaxLength = 300 }
                    },
                    f =>
                    {
                        reason = f.First(x => x.Key == "reason").Text;
                        return Task.FromResult<string?>(null);
                    }, "Deactivate");

                if (!given) return;
            }

            await GuardAsync(async () =>
            {
                var result = await Session.Platform.SetTenantStatusAsync(
                    tenant.CompanyId, activating, reason);

                if (!result.IsSuccess)
                {
                    ShowError(result.ErrorMessage);
                    return;
                }

                await LoadAsync();
                Notify($"{tenant.CompanyName} has been " +
                       $"{(activating ? "reactivated" : "deactivated")}.");
            }, "Saving…");
        }

        /// <summary>
        /// Brings a tenant's database up to the current schema.
        ///
        /// Registering a company says where its data lives; this is what makes the database
        /// usable. Safe to run again - it applies only what is missing.
        /// </summary>
        private async Task ProvisionAsync()
        {
            var tenant = Selected;

            if (tenant is null)
            {
                ShowError("Select the tenant whose database should be provisioned.");
                return;
            }

            if (!tenant.HasDatabaseRegistration)
            {
                ShowError($"{tenant.CompanyName} has no database registered, so there is nothing " +
                          "to provision. Register one first.");
                return;
            }

            // Shared with Platform Monitoring, which reaches the same repair from the other
            // direction - there, the tenant is behind rather than new.
            if (!PlatformProvisioning.Confirm(tenant.DatabaseName, 0)) return;

            await GuardAsync(async () =>
            {
                var outcome = await PlatformProvisioning.ApplyAsync(
                    Session, tenant.CompanyId, tenant.DatabaseName);

                if (!outcome.Succeeded)
                {
                    ShowError(outcome.Message);
                    return;
                }

                Notify(outcome.Message ?? $"{tenant.DatabaseName} is up to date.");
            }, "Provisioning…");
        }

        private async Task ShowHealthAsync()
        {
            await GuardAsync(async () =>
            {
                var health = Unwrap(await Session.Platform.GetHealthAsync());
                if (health is null) return;

                ListDialog.Show(this,
                    "Tenant health",
                    $"{health.Count(h => h.CanConnect)} of {health.Count} databases reachable",
                    health.Select(h => new
                    {
                        Company = h.CompanyName,
                        Database = string.IsNullOrWhiteSpace(h.DatabaseName) ? "—" : h.DatabaseName,
                        h.Status,
                        Pending = h.PendingMigrations,
                        Problem = string.IsNullOrWhiteSpace(h.Problem) ? "—" : h.Problem
                    }).ToList());
            }, "Probing tenant databases…");
        }
    }

    /// <summary>
    /// Subscriptions and tiers: what FitCore sells, to whom, and for how long.
    ///
    /// Subscribing a company is what sets their tier, so billing and entitlement cannot
    /// silently disagree. Cancelling billing does not revoke access - locking a customer out
    /// of their own data is a separate, deliberate act, so a payment dispute does not become a
    /// data outage.
    /// </summary>
    internal sealed class PlatformSubscriptionsPage : CrudPageBase<CompanySubscriptionDto>
    {
        private readonly ComboBox _status;

        private readonly Label _active;
        private readonly Label _activeHint;
        private readonly Label _recurring;
        private readonly Label _recurringHint;
        private readonly Label _expiring;
        private readonly Label _expiringHint;
        private readonly Label _lapsed;
        private readonly Label _lapsedHint;

        private List<TenantDto> _tenants = new();
        private List<SubscriptionPlanDto> _plans = new();

        public PlatformSubscriptionsPage(FitCoreSession session)
            : base(session, "Subscriptions & Tiers",
                   "What each tenant is paying, on which plan, and when it renews.",
                   "subscription", "Company, plan or status")
        {
            _status = UiKit.Select(140);
            _status.Items.Add("All statuses");
            _status.Items.AddRange(SubscriptionStatuses.All.Cast<object>().ToArray());
            _status.SelectedIndex = 0;
            _status.SelectedIndexChanged += async (_, _) => await LoadAsync();

            FilterBar.Controls.Add(UiKit.FilterLabel("Status"));
            FilterBar.Controls.Add(_status);

            AddRowAction("Renew", ButtonTone.Success, RenewAsync, 84);
            AddRowAction("Cancel", ButtonTone.Warning, CancelAsync, 86);

            StatsRow.Controls.Add(UiKit.StatCard("Active subscriptions", out _active, out _activeHint, UiTheme.Success));
            StatsRow.Controls.Add(UiKit.StatCard("Monthly recurring", out _recurring, out _recurringHint, UiTheme.Primary));
            StatsRow.Controls.Add(UiKit.StatCard("Expiring soon", out _expiring, out _expiringHint, UiTheme.Warning));
            StatsRow.Controls.Add(UiKit.StatCard("Lapsed", out _lapsed, out _lapsedHint, UiTheme.Danger));
        }

        protected override bool SupportsEdit => false;
        protected override bool SupportsDelete => false;

        protected override string CreatedMessage => "Subscription created and the tenant's tier set to match.";

        protected override string EmptyHeadline => "No subscriptions yet";
        protected override string EmptyDetail =>
            "Put a tenant on a plan. Their enterprise tier is set to whatever the plan grants, " +
            "so billing and what they can actually reach stay in step.";

        protected override async Task<List<CompanySubscriptionDto>?> FetchAsync()
        {
            if (_tenants.Count == 0)
            {
                _tenants = Unwrap(await Session.Platform.GetTenantsAsync()) ?? new List<TenantDto>();
            }

            if (_plans.Count == 0)
            {
                _plans = Unwrap(await Session.Platform.GetPlansAsync()) ?? new List<SubscriptionPlanDto>();
            }

            var subscriptions = Unwrap(await Session.Platform.GetSubscriptionsAsync(
                status: _status.SelectedIndex <= 0 ? null : (string)_status.SelectedItem!));

            if (subscriptions is not null)
            {
                var live = subscriptions.Where(s => s.Status == SubscriptionStatuses.Active).ToList();

                _active.Text = live.Count.ToString("N0");
                _activeHint.Text = $"{subscriptions.Count} terms on record";

                // Annual and monthly plans are normalised to a month, or one would look twelve
                // times the size of the other.
                _recurring.Text = live
                    .Sum(s => s.BillingCycle == BillingCycles.Annual ? s.Amount / 12m : s.Amount)
                    .ToString("N2");
                _recurringHint.Text = "normalised per month";

                var soon = live.Count(s => s.DaysRemaining is >= 0 and <= 30);
                _expiring.Text = soon.ToString("N0");
                _expiring.ForeColor = soon > 0 ? UiTheme.Warning : UiTheme.TextPrimary;
                _expiringHint.Text = soon == 0 ? "nothing due" : "within 30 days";

                var lapsed = subscriptions.Count(s => s.Status == SubscriptionStatuses.Expired);
                _lapsed.Text = lapsed.ToString("N0");
                _lapsed.ForeColor = lapsed > 0 ? UiTheme.Danger : UiTheme.TextPrimary;
                _lapsedHint.Text = "ran out without renewing";
            }

            return subscriptions;
        }

        protected override void DefineColumns()
        {
            Column(nameof(CompanySubscriptionDto.CompanyName), "Company", 170);
            Column(nameof(CompanySubscriptionDto.PlanName), "Plan", 140);
            StatusColumn(nameof(CompanySubscriptionDto.Tier), "Tier", 65);
            Column(nameof(CompanySubscriptionDto.BillingCycle), "Cycle", 70);
            MoneyColumn(nameof(CompanySubscriptionDto.Amount), "Amount", 85);
            DateColumn(nameof(CompanySubscriptionDto.StartDate), "From", 75);
            DateColumn(nameof(CompanySubscriptionDto.EndDate), "To", 75);
            Column(nameof(CompanySubscriptionDto.DaysRemaining), "Days left", 65, rightAlign: true);
            StatusColumn(nameof(CompanySubscriptionDto.Status), "Status", 75);
            FlagColumn(nameof(CompanySubscriptionDto.AutoRenew), "Auto-renew", 70);
        }

        protected override bool Matches(CompanySubscriptionDto s, string term) =>
            s.CompanyName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            s.PlanName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            s.Status.Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override Task<bool> OnAddAsync()
        {
            if (_tenants.Count == 0 || _plans.Count == 0)
            {
                ShowError("A tenant and a plan are both needed before a subscription can start.");
                return Task.FromResult(false);
            }

            var saved = EditDialog.Run(this, "Start a subscription",
                "Whatever term the tenant is on is superseded, and their enterprise tier is set " +
                "to whatever this plan grants.",
                new List<FieldSpec>
                {
                    new("company", "Company", FieldKind.Combo)
                    {
                        Required = true,
                        Options = _tenants
                            .Select(t => new KeyValuePair<string, string>(
                                t.CompanyId.ToString(), $"{t.CompanyCode} · {t.CompanyName}"))
                            .ToList()
                    },
                    new("plan", "Plan", FieldKind.Combo)
                    {
                        Required = true,
                        Options = _plans
                            .Where(p => p.IsActive)
                            .Select(p => new KeyValuePair<string, string>(
                                p.SubscriptionPlanId.ToString(),
                                $"{p.PlanName} · {p.Tier} · {p.MonthlyPrice:N2}/mo"))
                            .ToList()
                    },
                    new("cycle", "Billing cycle", FieldKind.Combo)
                    {
                        Required = true,
                        Value = BillingCycles.Monthly,
                        Options = BillingCycles.All
                            .Select(c => new KeyValuePair<string, string>(c, c)).ToList()
                    },
                    new("start", "Starts", FieldKind.Date)
                        { Required = true, Value = DateTime.Today },
                    new("autoRenew", "Renew automatically", FieldKind.Check) { Value = true }
                },
                async f =>
                {
                    var result = await Session.Platform.SubscribeAsync(new SubscribeDto
                    {
                        CompanyId = int.Parse(f.First(x => x.Key == "company").ComboValue!),
                        SubscriptionPlanId = int.Parse(f.First(x => x.Key == "plan").ComboValue!),
                        BillingCycle = f.First(x => x.Key == "cycle").Text,
                        StartDate = f.First(x => x.Key == "start").Date,
                        AutoRenew = f.First(x => x.Key == "autoRenew").Flag
                    });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Subscribe");

            return Task.FromResult(saved);
        }

        private async Task RenewAsync()
        {
            var subscription = Selected;

            if (subscription is null)
            {
                ShowError("Select the subscription to renew.");
                return;
            }

            if (UiKit.Confirm(
                    $"Renew {subscription.CompanyName}'s {subscription.PlanName} subscription " +
                    $"for another {subscription.BillingCycle.ToLowerInvariant()} term?\r\n\r\n" +
                    "Renewing early extends from the existing end date, so no paid-for days are lost.")
                != DialogResult.Yes)
            {
                return;
            }

            await GuardAsync(async () =>
            {
                var result = await Session.Platform.RenewSubscriptionAsync(
                    subscription.CompanySubscriptionId);

                if (!result.IsSuccess)
                {
                    ShowError(result.ErrorMessage);
                    return;
                }

                await LoadAsync();
                Notify($"{subscription.CompanyName}'s subscription renewed.");
            }, "Renewing…");
        }

        private async Task CancelAsync()
        {
            var subscription = Selected;

            if (subscription is null)
            {
                ShowError("Select the subscription to cancel.");
                return;
            }

            var cancelled = EditDialog.Run(this, $"Cancel {subscription.CompanyName}'s subscription",
                "Billing stops. Their access does not - deactivating a tenant is a separate " +
                "decision, so a payment dispute does not lock a customer out of their own data.",
                new List<FieldSpec>
                {
                    new("reason", "Why?", FieldKind.Multiline) { Required = true, MaxLength = 300 }
                },
                async f =>
                {
                    var result = await Session.Platform.CancelSubscriptionAsync(
                        subscription.CompanySubscriptionId, f.First(x => x.Key == "reason").Text);

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Cancel subscription");

            if (!cancelled) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify($"{subscription.CompanyName}'s subscription cancelled.");
            }, "Refreshing…");
        }

    }

    /// <summary>
    /// The Micro, Small and Medium price list - what each tier costs and includes.
    ///
    /// A plan's tier decides its modules; the module list and the annual savings shown here are
    /// computed server-side from that tier against the same catalogue <c>IsSubmoduleAllowed</c>
    /// enforces, never stored on the plan itself. That is what keeps this screen from ever
    /// promising a module the tier ceiling would refuse.
    /// </summary>
    internal sealed class SubscriptionPlansPage : CrudPageBase<SubscriptionPlanDto>
    {
        private readonly Button _toggleActive;
        private readonly Label _offered;
        private readonly Label _offeredHint;
        private readonly Label _tenants;
        private readonly Label _tenantsHint;
        private readonly Label _entry;
        private readonly Label _entryHint;

        public SubscriptionPlansPage(FitCoreSession session)
            : base(session, "Plans",
                   "The Micro, Small and Medium price list - what each tier costs and includes.",
                   "plan", "Plan name, code or tier")
        {
            AddRowAction("View details", ButtonTone.Secondary, ViewDetailsAsync, 100);
            AddRowAction("View modules", ButtonTone.Secondary, ViewModulesAsync, 104);
            _toggleActive = AddRowAction("Deactivate", ButtonTone.Secondary, ToggleActiveAsync, 110);

            StatsRow.Controls.Add(UiKit.StatCard("Plans offered", out _offered, out _offeredHint, UiTheme.Primary));
            StatsRow.Controls.Add(UiKit.StatCard("Tenants subscribed", out _tenants, out _tenantsHint, UiTheme.Info));
            StatsRow.Controls.Add(UiKit.StatCard("Entry price", out _entry, out _entryHint, UiTheme.Success));
        }

        protected override bool SupportsDelete => false;

        protected override void OnSelectionChanged() =>
            _toggleActive.Text = Selected is { IsActive: false } ? "Activate" : "Deactivate";

        protected override string CreatedMessage => "Plan created.";
        protected override string UpdatedMessage => "Plan updated.";

        protected override string EmptyHeadline => "No plans configured";
        protected override string EmptyDetail =>
            "Create the Micro, Small and Medium plans a tenant can subscribe to. Each one's " +
            "modules follow from its tier automatically - there is nothing to pick.";

        protected override async Task<List<SubscriptionPlanDto>?> FetchAsync()
        {
            var plans = Unwrap(await Session.Platform.GetPlansAsync());

            if (plans is not null)
            {
                _offered.Text = plans.Count(p => p.IsActive).ToString("N0");
                _offeredHint.Text = $"{plans.Count} configured";

                _tenants.Text = plans.Sum(p => p.TenantCount).ToString("N0");
                _tenantsHint.Text = "across every plan";

                var entry = plans.Where(p => p.IsActive).OrderBy(p => p.MonthlyPrice).FirstOrDefault();
                _entry.Text = entry is null ? "—" : UiKit.Money(entry.MonthlyPrice);
                _entryHint.Text = entry is null ? "no active plan" : $"{entry.PlanName} per month";
            }

            return plans;
        }

        protected override void DefineColumns()
        {
            StatusColumn(nameof(SubscriptionPlanDto.Tier), "Tier", 70);
            Column(nameof(SubscriptionPlanDto.PlanName), "Plan", 130);
            Column(nameof(SubscriptionPlanDto.ModuleCount), "Modules", 60, rightAlign: true);
            Column(nameof(SubscriptionPlanDto.Capability), "Capability", 300);
            MoneyColumn(nameof(SubscriptionPlanDto.MonthlyPrice), "Monthly", 85);
            MoneyColumn(nameof(SubscriptionPlanDto.AnnualPrice), "Annual", 85);
            StatusColumn(nameof(SubscriptionPlanDto.Status), "Status", 70);
        }

        protected override bool Matches(SubscriptionPlanDto p, string term) =>
            p.PlanName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            p.PlanCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            p.Tier.Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override Task<bool> OnAddAsync()
        {
            var takenTiers = new HashSet<string>(Items.Select(p => p.Tier), StringComparer.OrdinalIgnoreCase);
            var availableTiers = EnterpriseTiers.All.Where(t => !takenTiers.Contains(t)).ToList();

            if (availableTiers.Count == 0)
            {
                ShowError("Micro, Small and Medium already each have a plan. Edit one of them " +
                          "instead of adding a fourth.");
                return Task.FromResult(false);
            }

            var saved = EditDialog.Run(this, "Create a plan",
                "The tier decides which modules this plan includes - Membership and Payments " +
                "alone on Micro, rising to every module on Medium. That follows from the tier " +
                "automatically and is not chosen here.",
                new List<FieldSpec>
                {
                    new("code", "Plan code")
                        { Required = true, MaxLength = 40, Hint = "Unique, e.g. MICRO-2026." },
                    new("name", "Plan name") { Required = true, MaxLength = 150, Hint = "e.g. Micro Gym." },
                    new("tier", "Tier", FieldKind.Combo)
                    {
                        Required = true,
                        Value = availableTiers[0],
                        Options = availableTiers
                            .Select(t => new KeyValuePair<string, string>(t, t + " Enterprise"))
                            .ToList()
                    },
                    new("monthly", "Monthly price", FieldKind.Money) { Required = true, Minimum = 0 },
                    new("annual", "Annual price", FieldKind.Money)
                    {
                        Required = true, Minimum = 0,
                        Hint = "Usually less than twelve months at the monthly rate."
                    },
                    new("maxUsers", "Max users", FieldKind.Integer)
                        { Minimum = 0, Value = 0, Hint = "0 means unlimited." },
                    new("description", "Description", FieldKind.Multiline) { MaxLength = 500 },
                    new("capability", "Capability", FieldKind.Multiline)
                    {
                        Required = true, MaxLength = 500,
                        Hint = "The one sentence a pricing page shows under the tier name."
                    }
                },
                async f =>
                {
                    var result = await Session.Platform.CreatePlanAsync(new CreateSubscriptionPlanDto
                    {
                        PlanCode = f.First(x => x.Key == "code").Text,
                        PlanName = f.First(x => x.Key == "name").Text,
                        Tier = EnterpriseTiers.ValueOf(f.First(x => x.Key == "tier").Text),
                        MonthlyPrice = f.First(x => x.Key == "monthly").Decimal,
                        AnnualPrice = f.First(x => x.Key == "annual").Decimal,
                        MaxUsers = f.First(x => x.Key == "maxUsers").Int,
                        Description = f.First(x => x.Key == "description").Text,
                        Capability = f.First(x => x.Key == "capability").Text
                    });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Create plan");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(SubscriptionPlanDto plan)
        {
            var saved = EditDialog.Run(this, $"Edit {plan.PlanName}",
                "The tier is fixed once a plan exists, because changing it would silently move " +
                "every subscribed tenant to a different set of modules. Retire this plan and " +
                "create another if the tier itself needs to change.",
                new List<FieldSpec>
                {
                    new("tier", "Tier") { Value = plan.Tier + " Enterprise", ReadOnly = true },
                    new("name", "Plan name") { Required = true, Value = plan.PlanName, MaxLength = 150 },
                    new("monthly", "Monthly price", FieldKind.Money)
                        { Required = true, Minimum = 0, Value = plan.MonthlyPrice },
                    new("annual", "Annual price", FieldKind.Money)
                        { Required = true, Minimum = 0, Value = plan.AnnualPrice },
                    new("maxUsers", "Max users", FieldKind.Integer)
                        { Minimum = 0, Value = plan.MaxUsers, Hint = "0 means unlimited." },
                    new("description", "Description", FieldKind.Multiline)
                        { Value = plan.Description, MaxLength = 500 },
                    new("capability", "Capability", FieldKind.Multiline)
                        { Required = true, Value = plan.Capability, MaxLength = 500 },
                    new("active", "Offered", FieldKind.Check) { Value = plan.IsActive }
                },
                async f =>
                {
                    var result = await Session.Platform.UpdatePlanAsync(
                        plan.SubscriptionPlanId, new UpdateSubscriptionPlanDto
                        {
                            PlanName = f.First(x => x.Key == "name").Text,
                            MonthlyPrice = f.First(x => x.Key == "monthly").Decimal,
                            AnnualPrice = f.First(x => x.Key == "annual").Decimal,
                            MaxUsers = f.First(x => x.Key == "maxUsers").Int,
                            Description = f.First(x => x.Key == "description").Text,
                            Capability = f.First(x => x.Key == "capability").Text,
                            IsActive = f.First(x => x.Key == "active").Flag
                        });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Save changes");

            return Task.FromResult(saved);
        }

        protected override Task<string?> OnDeleteAsync(SubscriptionPlanDto plan) =>
            Task.FromResult<string?>(
                "A plan is never deleted - deactivate it instead. Existing subscribers keep " +
                "what they are paying for, and it simply drops off the list a new subscription " +
                "can pick from.");

        private Task ViewDetailsAsync()
        {
            var plan = Selected;

            if (plan is null)
            {
                ShowError("Select a plan to view.");
                return Task.CompletedTask;
            }

            ListDialog.Show(this, $"{plan.PlanName} · {plan.Tier} Enterprise", plan.Capability,
                new List<object>
                {
                    new { Field = "Plan code", Value = plan.PlanCode },
                    new { Field = "Tier", Value = plan.Tier + " Enterprise" },
                    new { Field = "Monthly price", Value = UiKit.Money(plan.MonthlyPrice) },
                    new { Field = "Annual price", Value = UiKit.Money(plan.AnnualPrice) },
                    new
                    {
                        Field = "Annual savings",
                        Value = $"{UiKit.Money(plan.AnnualSavings)} ({plan.AnnualSavingsPercent:N1}%)"
                    },
                    new { Field = "Max users", Value = plan.MaxUsers == 0 ? "Unlimited" : plan.MaxUsers.ToString("N0") },
                    new { Field = "Modules included", Value = $"{plan.ModuleCount} of 9" },
                    new { Field = "Tenants subscribed", Value = plan.TenantCount.ToString("N0") },
                    new { Field = "Status", Value = plan.Status },
                    new
                    {
                        Field = "Description",
                        Value = string.IsNullOrWhiteSpace(plan.Description) ? "—" : plan.Description
                    }
                });

            return Task.CompletedTask;
        }

        private Task ViewModulesAsync()
        {
            var plan = Selected;

            if (plan is null)
            {
                ShowError("Select a plan to see its modules.");
                return Task.CompletedTask;
            }

            if (plan.Modules.Count == 0)
            {
                ShowError($"{plan.PlanName} includes no modules.");
                return Task.CompletedTask;
            }

            ListDialog.Show(this, $"Modules included in {plan.PlanName}",
                $"{plan.Modules.Count} of 9 modules - computed from the {plan.Tier} tier",
                plan.Modules
                    .OrderBy(m => m.Group)
                    .ThenBy(m => m.DisplayName)
                    .Select(m => new { Module = m.DisplayName, Group = m.Group })
                    .ToList());

            return Task.CompletedTask;
        }

        private async Task ToggleActiveAsync()
        {
            var plan = Selected;

            if (plan is null)
            {
                ShowError("Select a plan to activate or deactivate.");
                return;
            }

            var activating = !plan.IsActive;

            if (!activating && !UiKit.ConfirmDelete(this,
                    $"Deactivate {plan.PlanName}?",
                    plan.TenantCount > 0
                        ? $"{plan.TenantCount} tenant(s) already on this plan keep their access " +
                          "and their billing - deactivating only takes it off the list a new " +
                          "subscription can pick from."
                        : "It drops off the list a new subscription can pick from. Nobody is " +
                          "subscribed to it yet, so nothing else changes."))
            {
                return;
            }

            await GuardAsync(async () =>
            {
                var result = await Session.Platform.UpdatePlanAsync(
                    plan.SubscriptionPlanId, new UpdateSubscriptionPlanDto
                    {
                        PlanName = plan.PlanName,
                        MonthlyPrice = plan.MonthlyPrice,
                        AnnualPrice = plan.AnnualPrice,
                        MaxUsers = plan.MaxUsers,
                        Description = plan.Description,
                        Capability = plan.Capability,
                        IsActive = activating
                    });

                if (!result.IsSuccess)
                {
                    ShowError(result.ErrorMessage);
                    return;
                }

                await LoadAsync();
                Notify($"{plan.PlanName} has been {(activating ? "activated" : "deactivated")}.");
            }, "Saving…");
        }
    }

    /// <summary>
    /// Every account on the platform, across all tenants.
    ///
    /// This is the recovery path: when a gym's only owner is locked out, the Super Admin is
    /// who can reset their password. Every such reset forces a change on next sign-in, because
    /// the platform operator now knows that password and should not.
    /// </summary>
    internal sealed class PlatformUsersPage : CrudPageBase<PlatformUserDto>
    {
        private readonly Button _toggleActive;
        private readonly ComboBox _company;
        private List<TenantDto> _tenants = new();

        public PlatformUsersPage(FitCoreSession session)
            : base(session, "Platform Users",
                   "Every account on this installation, whichever tenant it belongs to.",
                   "account", "Username, name, email or company")
        {
            _company = UiKit.Select(220);
            _company.Items.Add("All companies");
            _company.SelectedIndex = 0;
            _company.SelectedIndexChanged += async (_, _) => await LoadAsync();

            FilterBar.Controls.Add(UiKit.FilterLabel("Company"));
            FilterBar.Controls.Add(_company);

            AddRowAction("Reset password", ButtonTone.Warning, ResetPasswordAsync, 128);
            _toggleActive = AddRowAction("Deactivate", ButtonTone.Secondary, ToggleActiveAsync, 110);
        }

        protected override bool SupportsAdd => false;
        protected override bool SupportsEdit => false;
        protected override bool SupportsDelete => false;

        protected override void OnSelectionChanged() =>
            _toggleActive.Text = Selected is { IsActive: false } ? "Activate" : "Deactivate";

        protected override string EmptyHeadline => "No accounts found";
        protected override string EmptyDetail =>
            "Accounts are created inside each tenant, or by the Bootstrap configuration section.";

        protected override async Task<List<PlatformUserDto>?> FetchAsync()
        {
            if (_tenants.Count == 0)
            {
                _tenants = Unwrap(await Session.Platform.GetTenantsAsync()) ?? new List<TenantDto>();

                _company.Items.AddRange(_tenants
                    .Select(t => (object)$"{t.CompanyCode} · {t.CompanyName}")
                    .ToArray());
            }

            var companyId = _company.SelectedIndex <= 0
                ? (int?)null
                : _tenants[_company.SelectedIndex - 1].CompanyId;

            return Unwrap(await Session.Platform.GetUsersAsync(companyId));
        }

        protected override void DefineColumns()
        {
            Column(nameof(PlatformUserDto.CompanyCode), "Company", 70);
            Column(nameof(PlatformUserDto.Username), "Username", 110);
            Column(nameof(PlatformUserDto.FullName), "Name", 150);
            Column(nameof(PlatformUserDto.Email), "Email", 170);
            Column(nameof(PlatformUserDto.RoleDisplayName), "Role", 120);
            StatusColumn(nameof(PlatformUserDto.Status), "Status", 65);
            Column(nameof(PlatformUserDto.LastLoginAt), "Last signed in", 110, "d MMM yyyy HH:mm");
        }

        protected override bool Matches(PlatformUserDto u, string term) =>
            u.Username.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            u.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            u.Email.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            u.CompanyName.Contains(term, StringComparison.OrdinalIgnoreCase);

        private async Task ResetPasswordAsync()
        {
            var user = Selected;

            if (user is null)
            {
                ShowError("Select the account whose password should be reset.");
                return;
            }

            var reset = EditDialog.Run(this,
                $"Reset {user.Username}'s password",
                $"{user.FullName} at {user.CompanyName}. They will be required to choose their " +
                "own password the next time they sign in.",
                new List<FieldSpec>
                {
                    new("password", "Temporary password", FieldKind.Password)
                    {
                        Required = true,
                        Validate = f => f.Text.Length < 8
                            ? "A password must be at least 8 characters long."
                            : null
                    }
                },
                async f =>
                {
                    var result = await Session.Platform.ResetPasswordAsync(
                        user.AppUserId, f.First(x => x.Key == "password").Text);

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Reset password");

            if (!reset) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify($"{user.Username}'s password has been reset.");
            }, "Refreshing…");
        }

        private async Task ToggleActiveAsync()
        {
            var user = Selected;

            if (user is null)
            {
                ShowError("Select an account to activate or deactivate.");
                return;
            }

            var activating = !user.IsActive;

            if (!activating && !UiKit.ConfirmDelete(this,
                    $"Deactivate {user.Username} at {user.CompanyName}?",
                    "They will be refused at sign-in immediately. Nothing they recorded is " +
                    "affected, and the account can be reactivated."))
            {
                return;
            }

            await GuardAsync(async () =>
            {
                var result = await Session.Platform.SetUserStatusAsync(user.AppUserId, activating);

                if (!result.IsSuccess)
                {
                    ShowError(result.ErrorMessage);
                    return;
                }

                await LoadAsync();
                Notify($"{user.Username} has been " +
                       $"{(activating ? "reactivated" : "deactivated")}.");
            }, "Saving…");
        }
    }

    /// <summary>
    /// The platform's own audit trail: sign-in, permission and company changes, across every
    /// tenant.
    ///
    /// Kept in the master database because these events happen before a tenant is known - a
    /// failed sign-in has no company to attribute to, and registering one is done *to* a tenant
    /// rather than by it.
    /// </summary>
    internal sealed class PlatformAuditPage : CrudPageBase<AuditEventDto>
    {
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;
        private readonly ComboBox _company;

        private List<TenantDto> _tenants = new();

        public PlatformAuditPage(FitCoreSession session)
            : base(session, "Platform Audit",
                   "Sign-in, permission and company changes across the whole installation.",
                   "event", "Account, action or summary")
        {
            _from = UiKit.DatePicker(DateTime.Today.AddDays(-30));
            _from.ValueChanged += async (_, _) => await LoadAsync();

            _to = UiKit.DatePicker(DateTime.Today);
            _to.ValueChanged += async (_, _) => await LoadAsync();

            _company = UiKit.Select(220);
            _company.Items.Add("All companies");
            _company.SelectedIndex = 0;
            _company.SelectedIndexChanged += async (_, _) => await LoadAsync();

            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            FilterBar.Controls.Add(_from);
            FilterBar.Controls.Add(UiKit.FilterLabel("To"));
            FilterBar.Controls.Add(_to);
            FilterBar.Controls.Add(UiKit.FilterLabel("Company"));
            FilterBar.Controls.Add(_company);
        }

        protected override bool SupportsAdd => false;
        protected override bool SupportsEdit => false;
        protected override bool SupportsDelete => false;

        protected override string EmptyHeadline => "No platform events in this period";
        protected override string EmptyDetail =>
            "Sign-ins, password resets and company changes appear here. Widen the date range to " +
            "see more.";

        protected override async Task<List<AuditEventDto>?> FetchAsync()
        {
            if (_tenants.Count == 0)
            {
                _tenants = Unwrap(await Session.Platform.GetTenantsAsync()) ?? new List<TenantDto>();

                _company.Items.AddRange(_tenants
                    .Select(t => (object)$"{t.CompanyCode} · {t.CompanyName}")
                    .ToArray());
            }

            var companyId = _company.SelectedIndex <= 0
                ? (int?)null
                : _tenants[_company.SelectedIndex - 1].CompanyId;

            return Unwrap(await Session.Platform.GetAuditAsync(
                _from.Value.Date, _to.Value.Date.AddDays(1).AddTicks(-1),
                companyId: companyId, take: 500));
        }

        protected override void DefineColumns()
        {
            Column(nameof(AuditEventDto.OccurredAt), "When", 110, "d MMM yyyy HH:mm");
            StatusColumn(nameof(AuditEventDto.Action), "Action", 110);
            Column(nameof(AuditEventDto.Username), "Account", 120);
            Column(nameof(AuditEventDto.RoleKey), "Role", 80);
            Column(nameof(AuditEventDto.EntityName), "Entity", 90);
            Column(nameof(AuditEventDto.Summary), "Detail", 280);
        }

        protected override bool Matches(AuditEventDto e, string term) =>
            (e.Username ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (e.Action ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (e.Summary ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}
