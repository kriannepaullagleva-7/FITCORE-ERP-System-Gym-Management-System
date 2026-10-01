using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Whether every tenant's database is reachable and up to date with the schema.
    ///
    /// The Super Admin's alone, and deliberately diagnostic rather than operational: it reports
    /// what it found and offers the one repair the platform actually supports - applying the
    /// pending migrations a tenant is behind on, which is the same provisioning step the
    /// Tenants screen runs. There is no other button, because the remaining failure modes are a
    /// wrong server name or a rotated password, and neither is fixable from inside the
    /// application.
    ///
    /// Nothing here names a server, a database user or a credential. The master registry stores
    /// only a credential key and the connection string never leaves the API, so the worst this
    /// screen can disclose is a database name the platform administrator provisioned themselves.
    /// </summary>
    internal sealed class PlatformHealthPage : CrudPageBase<TenantHealthDto>
    {
        private readonly Label _summary;

        public PlatformHealthPage(FitCoreSession session)
            : base(session, "Platform Monitoring",
                   "Tenant database reachability and schema state, checked live.",
                   "tenant", "Company or database name")
        {
            // Re-check probes every tenant at once, so it needs no row selected; applying
            // migrations acts on whichever one is selected, so it follows the row.
            AddAction("Re-check", ButtonTone.Secondary, () => LoadAsync(), 96);
            AddRowAction("Apply migrations", ButtonTone.Primary, ProvisionAsync, 142);

            _summary = new Label
            {
                AutoSize = true,
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                Margin = new Padding(16, 10, 0, 0),
                UseMnemonic = false
            };
            FilterBar.Controls.Add(_summary);
        }

        protected override bool SupportsAdd => false;
        protected override bool SupportsEdit => false;
        protected override bool SupportsDelete => false;

        protected override string EmptyHeadline => "No tenants registered";
        protected override string EmptyDetail =>
            "Register a company under System Administration → Tenants, and it is checked here.";

        protected override async Task<List<TenantHealthDto>?> FetchAsync()
        {
            var all = Unwrap(await Session.Platform.GetHealthAsync());

            // Problems first. A healthy list is the common case and a platform administrator
            // opening this screen is looking for the exception, not the roll call.
            return all?
                .OrderBy(t => t.CanConnect)
                .ThenBy(t => t.IsUpToDate)
                .ThenBy(t => t.CompanyName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        protected override void DefineColumns()
        {
            Column(nameof(TenantHealthDto.CompanyId), "Id", 45, rightAlign: true);
            Column(nameof(TenantHealthDto.CompanyName), "Company", 190);
            Column(nameof(TenantHealthDto.DatabaseName), "Database", 120);
            StatusColumn(nameof(TenantHealthDto.Status), "Status", 110);
            Column(nameof(TenantHealthDto.PendingMigrations), "Pending", 70, rightAlign: true);
            Column(nameof(TenantHealthDto.Problem), "Problem", 220);
            Column(nameof(TenantHealthDto.CheckedAtUtc), "Checked", 120, "d MMM yyyy HH:mm");
        }

        protected override bool Matches(TenantHealthDto t, string term) =>
            (t.CompanyName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (t.DatabaseName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (t.Status ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override void AfterLoad()
        {
            var unreachable = Items.Count(t => !t.CanConnect);
            var behind = Items.Count(t => t.CanConnect && !t.IsUpToDate);
            var healthy = Items.Count - unreachable - behind;

            _summary.Text =
                $"{Items.Count:N0} tenant(s)      " +
                $"{healthy:N0} healthy      " +
                $"{behind:N0} behind on migrations      " +
                $"{unreachable:N0} unreachable";
        }

        /// <summary>
        /// Applies the pending migrations for the selected tenant.
        ///
        /// Confirmed first, and named: this writes to a live tenant's own database, and the
        /// operator should be in no doubt which one. A tenant that cannot be connected to is
        /// refused here rather than attempted, because the failure would be the same one the
        /// Status column is already reporting.
        /// </summary>
        private async Task ProvisionAsync()
        {
            var tenant = Selected;

            if (tenant is null)
            {
                ShowError("Select a tenant to bring its database up to date.");
                return;
            }

            if (!tenant.CanConnect)
            {
                ShowError($"{tenant.CompanyName} cannot be reached, so its schema cannot be " +
                          "updated. Fix the connection first - the server log has the reason.");
                return;
            }

            if (tenant.IsUpToDate)
            {
                ShowError($"{tenant.CompanyName} is already up to date.");
                return;
            }

            if (!PlatformProvisioning.Confirm(tenant.DatabaseName, tenant.PendingMigrations)) return;

            await GuardAsync(async () =>
            {
                var outcome = await PlatformProvisioning.ApplyAsync(
                    Session, tenant.CompanyId, tenant.DatabaseName);

                // Re-probed either way: a failed attempt may still have applied some of the
                // batch, and the Status column has to say where the tenant actually stands now.
                await LoadAsync();

                if (!outcome.Succeeded)
                {
                    ShowError(outcome.Message);
                    return;
                }

                Notify(outcome.Message ?? $"{tenant.CompanyName} is up to date.");
            }, "Applying migrations…");
        }
    }
}
