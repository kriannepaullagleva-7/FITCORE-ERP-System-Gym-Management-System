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
                return fromEnvironment;

            foreach (var settingsFile in FindSettingsFiles())
            {
                var configuration = new ConfigurationBuilder()
                    .AddJsonFile(settingsFile, optional: true)
                    .Build();

                var connectionString = configuration.GetConnectionString(connectionName);
                if (!string.IsNullOrWhiteSpace(connectionString))
                    return connectionString;
            }

            return fallback;
        }

        // dotnet ef runs from whichever project directory the command was issued in, so the
        // search walks up the tree and looks inside the two application projects that carry
        // an appsettings.json.
        private static IEnumerable<string> FindSettingsFiles()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

            for (var depth = 0; depth < 4 && directory != null; depth++, directory = directory.Parent)
            {
                var own = Path.Combine(directory.FullName, "appsettings.json");
                if (File.Exists(own)) yield return own;

                foreach (var project in new[] { "ERP_Project1", "ERP_api" })
                {
                    var candidate = Path.Combine(directory.FullName, project, "appsettings.json");
                    if (File.Exists(candidate)) yield return candidate;
                }
            }
        }
    }
}
