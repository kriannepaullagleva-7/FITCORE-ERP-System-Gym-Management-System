using System.Data.Common;
using System.Text.RegularExpressions;
using ERP_infrastructure.tenant;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ValidationException = ERP_infrastructure.services.ValidationException;

namespace ERP_api.Infrastructure
{
    /// <summary>
    /// Turns unhandled exceptions into a consistent ProblemDetails response.
    ///
    /// The rule this enforces: the client gets a message it can act on, and nothing about the
    /// infrastructure. Connection strings, credentials, server names and stack traces stay in
    /// the server log.
    /// </summary>
    public partial class ApiExceptionHandler : IExceptionHandler
    {
        private readonly IProblemDetailsService _problemDetailsService;
        private readonly ILogger<ApiExceptionHandler> _logger;

        public ApiExceptionHandler(
            IProblemDetailsService problemDetailsService,
            ILogger<ApiExceptionHandler> logger)
        {
            _problemDetailsService = problemDetailsService;
            _logger = logger;
        }

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            var (statusCode, title, detail) = Translate(exception);

            // Always log the real exception, with everything, on the server.
            _logger.LogError(
                exception,
                "Unhandled exception on {Method} {Path} mapped to {StatusCode}.",
                httpContext.Request.Method,
                httpContext.Request.Path,
                statusCode);

            httpContext.Response.StatusCode = statusCode;

            return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails
                {
                    Status = statusCode,
                    Title = title,
                    Detail = detail,
                    Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
                }
            });
        }

        private static (int StatusCode, string Title, string? Detail) Translate(Exception exception)
        {
            switch (exception)
            {
                // A rule the caller can fix by changing what they sent.
                case ValidationException validation:
                    return (StatusCodes.Status400BadRequest,
                        "Validation failed",
                        Scrub(validation.Message));

                // The tenant could not be determined or its database configuration is unusable.
                // The message can name servers and databases, so it is never sent to the client.
                case TenantResolutionException:
                    return (StatusCodes.Status503ServiceUnavailable,
                        "Tenant database unavailable",
                        "The database for your organisation could not be reached. " +
                        "Please contact your administrator.");

                // Anything that reached us from the data provider is infrastructure detail.
                case DbException:
                    return (StatusCodes.Status500InternalServerError,
                        "A database error occurred",
                        "The request could not be completed. Please try again later.");

                // Business rules in the service layer are raised as InvalidOperationException.
                // These messages are written for end users, and are scrubbed as a second line
                // of defence in case one ever carries connection detail.
                case InvalidOperationException invalidOperation:
                    return (StatusCodes.Status400BadRequest,
                        "The request could not be completed",
                        Scrub(invalidOperation.Message));

                case OperationCanceledException:
                    return (StatusCodes.Status499ClientClosedRequest,
                        "Request cancelled",
                        null);

                default:
                    return (StatusCodes.Status500InternalServerError,
                        "An unexpected error occurred",
                        "The request could not be completed. Please try again later.");
            }
        }

        /// <summary>
        /// Removes anything that looks like a credential or a connection string fragment from a
        /// message before it is returned to a client.
        /// </summary>
        private static string Scrub(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return message;
            }

            return SensitiveConnectionDetail().Replace(message, "$1=***");
        }

        [GeneratedRegex(
            @"\b(Password|Pwd|User\s*Id|Uid|Server|Data\s*Source|Initial\s*Catalog|Database)\s*=\s*[^;]*",
            RegexOptions.IgnoreCase)]
        private static partial Regex SensitiveConnectionDetail();
    }
}
