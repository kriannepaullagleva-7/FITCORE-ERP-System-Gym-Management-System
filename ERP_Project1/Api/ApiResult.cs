using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ERP_Project1.Api
{
    /// <summary>
    /// Outcome of an API call. Carries either a value or a message that is safe to show the
    /// user, so pages can render failures instead of throwing on a non-success status.
    /// </summary>
    public class ApiResult<T>
    {
        public bool IsSuccess { get; init; }
        public T? Value { get; init; }
        public string? ErrorMessage { get; init; }
        public HttpStatusCode StatusCode { get; init; }

        /// <summary>True when the caller's token was rejected, so the shell can sign them out.</summary>
        public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;

        public static ApiResult<T> Success(T? value, HttpStatusCode statusCode) =>
            new() { IsSuccess = true, Value = value, StatusCode = statusCode };

        public static ApiResult<T> Failure(string message, HttpStatusCode statusCode) =>
            new() { IsSuccess = false, ErrorMessage = message, StatusCode = statusCode };
    }

    /// <summary>
    /// Notified when the server rejects the bearer token, so the session can end the sign-in
    /// rather than leaving every screen quietly failing.
    /// </summary>
    public interface IApiFailureSink
    {
        void OnUnauthorized();
    }

    /// <summary>
    /// Turns anything the server or the network can produce into one sentence a gym
    /// receptionist can act on.
    ///
    /// The rule is deliberate: when the API has gone to the trouble of explaining itself -
    /// "Member cannot be deleted because they have existing payments" - that message is worth
    /// far more than a generic one and is shown verbatim. Only when the response carries
    /// nothing useful, or carries something that would leak infrastructure, does a canned
    /// message stand in.
    /// </summary>
    public static class ApiErrorText
    {
        public const string Unreachable =
            "Unable to connect to the FitCore server. Please make sure the server is running and try again.";

        public const string Timeout =
            "The FitCore server is taking too long to respond. Please try again in a moment.";

        public const string SessionExpired =
            "Your session has expired. Please sign in again.";

        public const string Forbidden =
            "You do not have permission to perform this action.";

        public const string NotFound =
            "The requested record could not be found.";

        public const string Conflict =
            "This operation conflicts with existing data.";

        public const string ServerError =
            "Something went wrong on the server. Please try again.";

        public const string Unavailable =
            "The FitCore server cannot reach its database right now. Please try again shortly.";

        public const string Unreadable =
            "The server sent a response FitCore could not read. Please try again.";

        /// <summary>The message for a status code when the body offered nothing better.</summary>
        public static string ForStatus(HttpStatusCode status) => status switch
        {
            HttpStatusCode.BadRequest => "Some of the details supplied are not valid. Please check them and try again.",
            HttpStatusCode.Unauthorized => SessionExpired,
            HttpStatusCode.Forbidden => Forbidden,
            HttpStatusCode.NotFound => NotFound,
            HttpStatusCode.Conflict => Conflict,
            HttpStatusCode.RequestTimeout => Timeout,
            HttpStatusCode.UnprocessableEntity => "Some of the details supplied are not valid. Please check them and try again.",
            HttpStatusCode.ServiceUnavailable => Unavailable,
            HttpStatusCode.GatewayTimeout => Timeout,
            _ => (int)status >= 500 ? ServerError : $"The request could not be completed ({(int)status})."
        };

        // Framework-generated ProblemDetails titles. They restate the status code and tell the
        // operator nothing, so they are not worth showing in place of a real sentence.
        private static readonly string[] GenericTitles =
        {
            "bad request", "unauthorized", "forbidden", "not found", "conflict",
            "internal server error", "service unavailable", "an error occurred while processing your request.",
            "one or more validation errors occurred.", "unprocessable entity", "request timeout"
        };

        // Anything matching these is infrastructure detail that must never reach a user.
        private static readonly string[] Leaky =
        {
            "password=", "pwd=", "server=", "data source=", "initial catalog=", "user id=",
            "connection string", "connectionstring", "trustservercertificate",
            "stack trace", "   at ", "system.data.sqlclient", "microsoft.data.sqlclient",
            "microsoft.entityframeworkcore", "c:\\", "/users/", "bearer ", "eyj"
        };

        /// <summary>
        /// True when the text is a real, safe business message rather than boilerplate or
        /// something that would leak a connection string, a token or a stack trace.
        /// </summary>
        public static bool IsUsable(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            var trimmed = text.Trim();

            // A wall of text is a dump, not a message.
            if (trimmed.Length > 400) return false;
            if (trimmed.StartsWith("<", StringComparison.Ordinal)) return false;   // an HTML error page

            var lower = trimmed.ToLowerInvariant();

            if (GenericTitles.Contains(lower)) return false;
            if (Leaky.Any(marker => lower.Contains(marker))) return false;

            return true;
        }

        /// <summary>
        /// Reads a failed response and produces the best available message. Prefers the API's
        /// own wording, falls back to the status code.
        /// </summary>
        public static async Task<string> DescribeAsync(HttpResponseMessage response)
        {
            string raw;

            try
            {
                raw = await response.Content.ReadAsStringAsync();
            }
            catch
            {
                return ForStatus(response.StatusCode);
            }

            var fromBody = Extract(raw);

            return IsUsable(fromBody) ? fromBody! : ForStatus(response.StatusCode);
        }

        /// <summary>
        /// Pulls the human part out of an RFC 9457 ProblemDetails document, a validation
        /// ProblemDetails with an "errors" dictionary, or a plain { "message": "..." } object.
        /// </summary>
        public static string? Extract(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // Some endpoints answer with a bare string rather than a document.
            if (!raw.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                return raw.Trim().Trim('"');
            }

            try
            {
                using var document = JsonDocument.Parse(raw);
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object) return null;

                // Validation failures carry the useful part under "errors", one array per field.
                if (root.TryGetProperty("errors", out var errors) &&
                    errors.ValueKind == JsonValueKind.Object)
                {
                    var collected = new List<string>();

                    foreach (var field in errors.EnumerateObject())
                    {
                        if (field.Value.ValueKind != JsonValueKind.Array) continue;

                        foreach (var message in field.Value.EnumerateArray())
                        {
                            var text = message.GetString();
                            if (!string.IsNullOrWhiteSpace(text)) collected.Add(text.Trim());
                        }
                    }

                    if (collected.Count > 0)
                    {
                        var builder = new StringBuilder();

                        foreach (var message in collected.Distinct().Take(6))
                        {
                            if (builder.Length > 0) builder.Append("\r\n");
                            builder.Append("• ").Append(message);
                        }

                        return builder.ToString();
                    }
                }

                foreach (var name in new[] { "detail", "message", "error", "title" })
                {
                    if (root.TryGetProperty(name, out var property) &&
                        property.ValueKind == JsonValueKind.String)
                    {
                        var text = property.GetString();
                        if (IsUsable(text)) return text!.Trim();
                    }
                }
            }
            catch (JsonException)
            {
                return null;
            }

            return null;
        }
    }

    /// <summary>
    /// Shared plumbing for the typed API services: issues the request, and turns a non-success
    /// response - or a dead network - into a message the UI can show as-is.
    /// </summary>
    public abstract class ApiServiceBase
    {
        protected readonly HttpClient Http;
        private readonly IApiFailureSink? _failures;

        protected ApiServiceBase(HttpClient http, IApiFailureSink? failures = null)
        {
            Http = http;
            _failures = failures;
        }

        protected async Task<ApiResult<T>> SendAsync<T>(Func<Task<HttpResponseMessage>> send)
        {
            try
            {
                using var response = await send();

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        _failures?.OnUnauthorized();
                    }

                    return ApiResult<T>.Failure(
                        await ApiErrorText.DescribeAsync(response), response.StatusCode);
                }

                if (response.StatusCode == HttpStatusCode.NoContent ||
                    response.Content.Headers.ContentLength == 0)
                {
                    return ApiResult<T>.Success(default, response.StatusCode);
                }

                var value = await response.Content.ReadFromJsonAsync<T>();
                return ApiResult<T>.Success(value, response.StatusCode);
            }
            catch (TaskCanceledException)
            {
                // HttpClient surfaces its own timeout as a cancellation.
                return ApiResult<T>.Failure(ApiErrorText.Timeout, HttpStatusCode.RequestTimeout);
            }
            catch (HttpRequestException)
            {
                return ApiResult<T>.Failure(ApiErrorText.Unreachable, HttpStatusCode.ServiceUnavailable);
            }
            catch (JsonException)
            {
                return ApiResult<T>.Failure(ApiErrorText.Unreadable, HttpStatusCode.UnsupportedMediaType);
            }
        }
    }
}
