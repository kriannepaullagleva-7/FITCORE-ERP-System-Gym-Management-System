using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ERP_Project1.Api
{
    /// <summary>
    /// The desktop client's whole relationship with the server.
    ///
    /// FitCore's WinForms client is a pure API consumer: it holds a bearer token and talks
    /// HTTP/JSON to ERP_api. It never opens a DbContext, never sees a connection string and
    /// never learns which physical database it is served from - the company travels inside the
    /// signed token, so the server decides the tenant and the desktop cannot influence it.
    ///
    /// Authorization is likewise the server's answer, not this client's. <see cref="Can"/>
    /// only decides what to draw; every endpoint re-checks and answers 403 regardless of what
    /// the desktop believes.
    ///
    /// One <see cref="HttpClient"/> is created here and shared by every typed service, so the
    /// application holds exactly one connection pool and one Authorization header.
    /// </summary>
    public sealed class FitCoreSession : IApiFailureSink
    {
        private readonly HttpClient _http;
        private bool _signalledExpiry;

        public FitCoreSession(string apiBaseUrl)
        {
            var baseUri = new Uri(apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/");

            var handler = new HttpClientHandler();

            // ASP.NET Core's local development certificate is self-signed, so a desktop client
            // talking to https://localhost would otherwise fail the chain check before any
            // request is sent. Scoped to loopback only: a deployed server is validated normally.
            if (baseUri.IsLoopback)
            {
                handler.ServerCertificateCustomValidationCallback =
                    (_, _, _, _) => true;
            }

            _http = new HttpClient(handler)
            {
                BaseAddress = baseUri,

                // Shared database hosting can be slow to hand out a connection, and the
                // desktop shows a progress indicator while it waits.
                Timeout = TimeSpan.FromSeconds(120)
            };

            Members = new MemberApiService(_http, this);
            Plans = new MembershipPlanApiService(_http, this);
            Subscriptions = new SubscriptionApiService(_http, this);
            Sales = new SaleApiService(_http, this);
            Payments = new PaymentApiService(_http, this);
            Products = new ProductApiService(_http, this);
            Inventory = new InventoryApiService(_http, this);
            Customers = new CustomerApiService(_http, this);
            Suppliers = new SupplierApiService(_http, this);
            Employees = new EmployeeApiService(_http, this);
            Payroll = new PayrollApiService(_http, this);
            Reports = new ReportsApiService(_http, this);
        }

        public string ApiBaseUrl => _http.BaseAddress!.ToString();

        public CurrentUserDto? CurrentUser { get; private set; }

        public bool IsSignedIn => CurrentUser is not null;

        /// <summary>
        /// Raised the first time the server rejects this session's token. The shell listens
        /// and returns the operator to the sign-in window rather than letting every screen
        /// fail one after another.
        /// </summary>
        public event Action? SessionExpired;

        // Typed clients. Every screen goes through one of these; none of them knows anything
        // about a database.
        public MemberApiService Members { get; }
        public MembershipPlanApiService Plans { get; }
        public SubscriptionApiService Subscriptions { get; }
        public SaleApiService Sales { get; }
        public PaymentApiService Payments { get; }
        public ProductApiService Products { get; }
        public InventoryApiService Inventory { get; }
        public CustomerApiService Customers { get; }
        public SupplierApiService Suppliers { get; }
        public EmployeeApiService Employees { get; }
        public PayrollApiService Payroll { get; }
        public ReportsApiService Reports { get; }

        /// <summary>
        /// Whether this user holds a module. Presentation only - it decides which navigation
        /// entries are drawn. The server answers 403 for a module the caller does not hold
        /// whatever this returns, so it can never be the only thing standing between a user
        /// and another tier's data.
        /// </summary>
        public bool Can(string module) =>
            CurrentUser?.Modules?.Any(m =>
                string.Equals(m, module, StringComparison.OrdinalIgnoreCase)) == true;

        void IApiFailureSink.OnUnauthorized()
        {
            // Only once per session: a dashboard firing several calls at the same time would
            // otherwise raise this for each of them.
            if (_signalledExpiry || CurrentUser is null) return;

            _signalledExpiry = true;
            SessionExpired?.Invoke();
        }

        public async Task<ApiResult<CurrentUserDto>> SignInAsync(string username, string password)
        {
            try
            {
                using var response = await _http.PostAsJsonAsync(
                    "api/auth/login", new LoginRequestDto { Username = username, Password = password });

                if (!response.IsSuccessStatusCode)
                {
                    // 401 here means the credentials were wrong, not that a session lapsed -
                    // there is no session yet - so the generic "signed out" wording would be
                    // actively confusing.
                    var message = response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized => "Invalid email or password.",
                        HttpStatusCode.Forbidden =>
                            "This account is not allowed to sign in. Please contact your administrator.",
                        HttpStatusCode.ServiceUnavailable => ApiErrorText.Unavailable,
                        _ => await ApiErrorText.DescribeAsync(response)
                    };

                    return ApiResult<CurrentUserDto>.Failure(message, response.StatusCode);
                }

                var payload = await response.Content.ReadFromJsonAsync<LoginResponseDto>();

                if (payload is null || string.IsNullOrWhiteSpace(payload.Token))
                {
                    return ApiResult<CurrentUserDto>.Failure(
                        "The server did not return a usable session. Please try again.",
                        response.StatusCode);
                }

                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", payload.Token);

                CurrentUser = payload.User;
                _signalledExpiry = false;

                return ApiResult<CurrentUserDto>.Success(payload.User, response.StatusCode);
            }
            catch (TaskCanceledException)
            {
                return ApiResult<CurrentUserDto>.Failure(
                    ApiErrorText.Timeout, HttpStatusCode.RequestTimeout);
            }
            catch (HttpRequestException)
            {
                return ApiResult<CurrentUserDto>.Failure(
                    ApiErrorText.Unreachable, HttpStatusCode.ServiceUnavailable);
            }
        }

        /// <summary>
        /// Re-reads permissions from the server. A module withdrawn by an administrator takes
        /// effect here rather than only when the token expires.
        /// </summary>
        public async Task RefreshCurrentUserAsync()
        {
            try
            {
                using var response = await _http.GetAsync("api/auth/me");

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    ((IApiFailureSink)this).OnUnauthorized();
                    return;
                }

                if (!response.IsSuccessStatusCode) return;

                var me = await response.Content.ReadFromJsonAsync<CurrentUserDto>();
                if (me is not null) CurrentUser = me;
            }
            catch
            {
                // Keeping the cached user is correct here: a transient failure must not
                // silently strip the operator's navigation mid-session.
            }
        }

        public async Task<ApiResult<object>> ChangePasswordAsync(string current, string replacement)
        {
            try
            {
                using var response = await _http.PostAsJsonAsync("api/auth/change-password",
                    new ChangePasswordRequestDto { CurrentPassword = current, NewPassword = replacement });

                if (!response.IsSuccessStatusCode)
                {
                    var message = response.StatusCode == HttpStatusCode.Unauthorized
                        ? "Your current password is not correct."
                        : await ApiErrorText.DescribeAsync(response);

                    return ApiResult<object>.Failure(message, response.StatusCode);
                }

                if (CurrentUser is not null) CurrentUser.MustChangePassword = false;
                return ApiResult<object>.Success(null, response.StatusCode);
            }
            catch (TaskCanceledException)
            {
                return ApiResult<object>.Failure(ApiErrorText.Timeout, HttpStatusCode.RequestTimeout);
            }
            catch (HttpRequestException)
            {
                return ApiResult<object>.Failure(ApiErrorText.Unreachable, HttpStatusCode.ServiceUnavailable);
            }
        }

        public async Task SignOutAsync()
        {
            try
            {
                using var _ = await _http.PostAsync("api/auth/logout", content: null);
            }
            catch
            {
                // Signing out locally must succeed even when the server is unreachable.
            }
            finally
            {
                _http.DefaultRequestHeaders.Authorization = null;
                CurrentUser = null;
                _signalledExpiry = false;
            }
        }
    }
}
