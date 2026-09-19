using Blazored.LocalStorage;
using ERP_UI;
using ERP_UI.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The API address comes from wwwroot/appsettings.json so it can be changed per deployment
// without a rebuild. It falls back to the host the UI itself was served from.
var apiBaseUrl = builder.Configuration["ApiBaseUrl"];

if (string.IsNullOrWhiteSpace(apiBaseUrl))
{
    apiBaseUrl = builder.HostEnvironment.BaseAddress;
}

// One HttpClient for the whole application. A WebAssembly host runs in a single scope, so a
// scoped registration is a single shared instance - which is what lets the bearer token that
// AuthApiService sets after sign-in be seen by every other typed service below.
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });

builder.Services.AddBlazoredLocalStorage();

// Authentication. The state provider publishes the signed-in user to <AuthorizeView> and
// friends; AuthApiService owns the token and is the only thing that writes to storage.
//
// Both are scoped rather than singleton. Local storage is registered scoped by Blazored, and
// the container refuses to build a singleton that captures a scoped dependency - correctly,
// since that is how a shorter-lived service ends up pinned for the life of the process.
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, FitCoreAuthStateProvider>();
builder.Services.AddScoped<AuthApiService>();

// Typed API clients. Pages depend on these rather than on HttpClient directly, so the URLs
// and error handling live in one place. The browser never sees a connection string: every
// database call happens server-side inside ERP_api, against whichever tenant database the
// signed-in user's company resolves to.
builder.Services.AddScoped<MemberApiService>();
builder.Services.AddScoped<MembershipPlanApiService>();
builder.Services.AddScoped<ReportsApiService>();
builder.Services.AddScoped<ProductApiService>();
builder.Services.AddScoped<InventoryApiService>();
builder.Services.AddScoped<SubscriptionApiService>();
builder.Services.AddScoped<PaymentApiService>();
builder.Services.AddScoped<SaleApiService>();
builder.Services.AddScoped<EmployeeApiService>();
builder.Services.AddScoped<PayrollApiService>();
builder.Services.AddScoped<ExpenseApiService>();
builder.Services.AddScoped<AdminApiService>();
builder.Services.AddScoped<UserAccessApiService>();

// Shared UI state. Singleton so a toast raised by a page survives the navigation that
// follows the save which raised it.
builder.Services.AddSingleton<ToastService>();

await builder.Build().RunAsync();
