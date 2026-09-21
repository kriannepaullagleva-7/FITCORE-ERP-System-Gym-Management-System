using Microsoft.Extensions.Configuration;

namespace ERP_infrastructure.data
{
    // The EF CLI builds its own context, so without this the migration tools would run
    // against a hard-coded local server while the applications talk to the server named in
    // appsettings.json - and "database update" would report success against the wrong
    // database. Both design-time factories resolve their connection string from here so the
    // tools always target the same database the apps do.
    internal static class DesignTimeConnectionStrings
    {
        public static string Resolve(string environmentVariable, string connectionName, string fallback)
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return Announce(fromEnvironment, environmentVariable);
            }

            foreach (var directory in CandidateDirectories())
            {
                var baseFile = Path.Combine(directory, "appsettings.json");
                var developmentFile = Path.Combine(directory, "appsettings.Development.json");
                var localFile = Path.Combine(directory, "appsettings.MonsterASP.json");

                if (!File.Exists(baseFile) && !File.Exists(developmentFile) && !File.Exists(localFile))
                {
                    continue;
                }

                // Layered exactly as the applications layer them. appsettings.json is committed
                // and ships with empty passwords; the gitignored files carry the real ones. Without
                // this layering the tools read the password-less entry, drop to Named Pipes and
                // fail with "the server was not found" against a server that is up.
                var configuration = new ConfigurationBuilder()
                    .AddJsonFile(baseFile, optional: true)
                    .AddJsonFile(developmentFile, optional: true)
                    .AddJsonFile(localFile, optional: true)
                    .Build();

                var connectionString = configuration.GetConnectionString(connectionName);

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    continue;
                }

                if (HasNoPassword(connectionString) && !IsLocal(connectionString))
                {
                    throw new InvalidOperationException(
                        $"The '{connectionName}' connection string found in {baseFile} has no " +
                        "password, and no gitignored appsettings.Development.json supplied one. " +
                        $"Either fill it in there, or set the {environmentVariable} environment " +
                        "variable before running the EF tools.");
                }

                return Announce(connectionString, directory);
            }

            return Announce(fallback, "built-in fallback");
        }

        // Design-time only, and the single most useful thing the tools can say: a migration
        // applied to the wrong tenant database is the expensive mistake here.
        private static string Announce(string connectionString, string source)
        {
            try
            {
                var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
                Console.WriteLine(
                    $"[EF design-time] target -> {builder.DataSource} / {builder.InitialCatalog}  (from {source})");
            }
            catch
            {
                // A malformed string is the provider's problem to report, not this helper's.
            }

            return connectionString;
        }

        private static bool HasNoPassword(string connectionString)
        {
            try
            {
                var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
                return !builder.IntegratedSecurity && string.IsNullOrEmpty(builder.Password);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsLocal(string connectionString) =>
            connectionString.Contains("(localdb)", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Trusted_Connection", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Integrated Security", StringComparison.OrdinalIgnoreCase);

        // dotnet ef runs from whichever project directory the command was issued in, and the
        // Package Manager Console runs from the solution directory, so the search walks up the
        // tree and also looks inside the two application projects that carry an appsettings.json.
        private static IEnumerable<string> CandidateDirectories()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

            for (var depth = 0; depth < 4 && directory != null; depth++, directory = directory.Parent)
            {
                yield return directory.FullName;

                // ERP_api holds the Web API settings; ERP_Project1 is the folder the WinForms
                // project (ERP_winforms.csproj) lives in.
                foreach (var project in new[] { "ERP_api", "ERP_Project1" })
                {
                    var candidate = Path.Combine(directory.FullName, project);
                    if (Directory.Exists(candidate)) yield return candidate;
                }
            }
        }
    }
}
