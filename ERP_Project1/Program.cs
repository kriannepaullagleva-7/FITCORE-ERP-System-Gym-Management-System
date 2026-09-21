using ERP_Project1.Api;
using Microsoft.Extensions.Configuration;

namespace ERP_Project1
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                ApplicationConfiguration.Initialize();

                // The desktop client needs one setting: where ERP_api is. It holds no
                // connection string and never opens a database - every byte of business data
                // arrives over HTTP from the API, which owns tenancy, authorisation and the
                // business rules.
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(System.AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
                    .Build();

                var apiBaseUrl = configuration["ApiBaseUrl"];

                if (string.IsNullOrWhiteSpace(apiBaseUrl))
                {
                    apiBaseUrl = "https://localhost:7214/";
                }

                // Sign in, work, sign out, sign in again - without restarting the process.
                while (true)
                {
                    var session = new FitCoreSession(apiBaseUrl);

                    using (var login = new LoginForm(session))
                    {
                        if (login.ShowDialog() != DialogResult.OK)
                        {
                            return;   // user closed the sign-in window
                        }
                    }

                    using var shell = new ShellForm(session);
                    Application.Run(shell);

                    // Anything other than "sign out" ends the application.
                    if (shell.DialogResult != DialogResult.Retry)
                    {
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"FitCore could not start.\r\n\r\n{ex.Message}\r\n\r\n{ex.InnerException?.Message}",
                    "FitCore ERP", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
