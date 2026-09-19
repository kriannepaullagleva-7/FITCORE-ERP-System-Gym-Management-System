using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ERP_UI.Services
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

        public static ApiResult<T> Success(T? value, HttpStatusCode statusCode) =>
            new() { IsSuccess = true, Value = value, StatusCode = statusCode };

        public static ApiResult<T> Failure(string message, HttpStatusCode statusCode) =>
            new() { IsSuccess = false, ErrorMessage = message, StatusCode = statusCode };
    }

    /// <summary>
    /// Shared plumbing for the typed API services: issues the request and turns a non-success
    /// response into a readable message, reading the ProblemDetails the API returns.
    /// </summary>
    public abstract class ApiServiceBase
    {
        protected readonly HttpClient Http;

        protected ApiServiceBase(HttpClient http)
        {
            Http = http;
        }

        protected async Task<ApiResult<T>> SendAsync<T>(Func<Task<HttpResponseMessage>> send)
        {
            try
            {
                using var response = await send();

                if (!response.IsSuccessStatusCode)
                {
                    return ApiResult<T>.Failure(
                        await ReadErrorAsync(response), response.StatusCode);
                }

                if (response.StatusCode == HttpStatusCode.NoContent ||
                    response.Content.Headers.ContentLength == 0)
                {
                    return ApiResult<T>.Success(default, response.StatusCode);
                }

                var value = await response.Content.ReadFromJsonAsync<T>();
                return ApiResult<T>.Success(value, response.StatusCode);
            }
            catch (HttpRequestException ex)
            {
                return ApiResult<T>.Failure(
                    $"The API could not be reached. {ex.Message}", HttpStatusCode.ServiceUnavailable);
            }
            catch (JsonException)
            {
                return ApiResult<T>.Failure(
                    "The API returned a response that could not be read.",
                    HttpStatusCode.UnsupportedMediaType);
            }
        }

        /// <summary>
        /// The API reports failures as ProblemDetails, and a couple of endpoints return a
        /// plain message object for conflicts. Both shapes are handled here.
        /// </summary>
        private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
        {
            try
            {
                var raw = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(raw))
                {
                    return $"Request failed with status {(int)response.StatusCode}.";
                }

                using var document = JsonDocument.Parse(raw);
                var root = document.RootElement;

                foreach (var name in new[] { "detail", "message", "title", "error" })
                {
                    if (root.TryGetProperty(name, out var property) &&
                        property.ValueKind == JsonValueKind.String)
                    {
                        var text = property.GetString();

                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }
                }

                return $"Request failed with status {(int)response.StatusCode}.";
            }
            catch (JsonException)
            {
                return $"Request failed with status {(int)response.StatusCode}.";
            }
        }
    }
}
