using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>What applying a tenant's pending migrations came to.</summary>
    internal sealed record ProvisioningOutcome(bool Succeeded, string? Message);

    /// <summary>
    /// Bringing a tenant's database up to the current schema, shared by Tenants and Platform
    /// Monitoring.
    ///
    /// Both screens need it for the same reason from different directions - Tenants because a
    /// newly registered company has an empty database, Monitoring because an existing one has
    /// fallen behind - and it writes to a live tenant's database, so the question put to the
    /// operator and the account of what happened afterwards are worded in exactly one place.
    ///
    /// The confirmation and the call are separate methods on purpose: the confirmation is a
    /// modal and must be answered before the caller enters its busy state, or the operator is
    /// asked a question by a screen that is already telling them to wait.
    /// </summary>
    internal static class PlatformProvisioning
    {
        /// <summary>
        /// Asks before writing to a live tenant database, naming the database and saying what is
        /// and is not at risk. Safe to run again, and nothing is dropped - which the operator
        /// cannot be expected to know unless it is said.
        /// </summary>
        public static bool Confirm(string databaseName, int pendingMigrations)
        {
            var what = pendingMigrations > 0
                ? $"Apply {UiKit.Plural(pendingMigrations, "pending migration")} to {databaseName}?"
                : $"Apply any missing schema changes to {databaseName}?";

            return UiKit.Confirm(
                $"{what}\r\n\r\n" +
                "Only migrations that have not already been applied are run. Nothing is dropped " +
                "and no existing data is changed, but this is a live tenant database and its " +
                "users may see errors while it runs.") == DialogResult.Yes;
        }

        /// <summary>
        /// Applies whatever is missing. The caller reloads and reports, because each screen has
        /// its own status strip and its own idea of what to refresh afterwards.
        /// </summary>
        public static async Task<ProvisioningOutcome> ApplyAsync(
            FitCoreSession session, int companyId, string databaseName)
        {
            var result = await session.Platform.ProvisionAsync(companyId);

            if (!result.IsSuccess || result.Value is null)
            {
                return new ProvisioningOutcome(false, result.ErrorMessage);
            }

            var applied = result.Value;

            if (!applied.Succeeded)
            {
                return new ProvisioningOutcome(
                    false, applied.Message ?? "The database could not be provisioned.");
            }

            return new ProvisioningOutcome(true,
                applied.MigrationsApplied.Count == 0
                    ? $"{databaseName} was already up to date."
                    : $"Applied {UiKit.Plural(applied.MigrationsApplied.Count, "migration")} " +
                      $"to {databaseName}.");
        }
    }
}
