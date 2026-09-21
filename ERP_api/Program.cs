using System.Text;
using System.Text.Json.Serialization;
using ERP_api.Infrastructure;
using Scalar.AspNetCore;
using ERP_api.Tenancy;
using ERP_infrastructure.data;
using ERP_infrastructure.services;
using ERP_infrastructure.tenant;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------------------
// Master database. Holds the tenant registry (companies, their databases, their devices),
// the sign-in accounts, and the roles and permissions those accounts carry. There is exactly
// one of these for the whole platform.
// ---------------------------------------------------------------------------------------
builder.Services.AddDbContext<MasterErpDbContext>(options =>
    // Single query is deliberate: the sign-in path loads a user with both its role
    // permissions and its per-user overrides, and one round trip to a remote database beats
    // three for collections this small. Stating it also silences EF's advisory warning.
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("MasterErp"),
        sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery)));

builder.Services.AddScoped<ICompanyDirectoryService, CompanyDirectoryService>();

// ---------------------------------------------------------------------------------------
// Who is acting, for the audit trail.
//
// Registered before the infrastructure wiring below, which adds its own no-op accessor with
// TryAdd: this one therefore wins in the API, while the desktop application and the tests
// keep attributing their writes to the system.
// ---------------------------------------------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, HttpCurrentUserAccessor>();

// ---------------------------------------------------------------------------------------
// Tenant infrastructure. Registers the tenant resolution chain and a request-scoped
// TenantErpDbContext bound to whichever tenant the server resolved for this request, so the
// repositories and services below are tenant-aware without knowing anything about tenancy.
// ---------------------------------------------------------------------------------------
builder.Services.AddErpTenancy(builder.Configuration);

// Repositories and application services (unchanged implementations).
builder.Services.AddErpApplicationServices();

// Sign-in, User Access and the start-up bootstrapper, all against the master database.
builder.Services.AddErpAccessControl(builder.Configuration);

// ---------------------------------------------------------------------------------------
// Authentication and authorization.
//
// A bearer token carries the company id, the role and one claim per permitted module. The
// tenant middleware reads the company straight out of it, so the signing key is the thing
// that keeps one tenant out of another tenant's database. An unsigned or absent key would
// let anyone mint a token for any company, so the API refuses to start without one.
// ---------------------------------------------------------------------------------------
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey is missing or shorter than 32 characters. Set it in configuration " +
        "(user secrets, an environment variable or a key vault) before starting the API. " +
        "Tenant isolation depends on it.");
}

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,

            ValidateAudience = true,
            ValidAudience = jwt.Audience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),

            ValidateLifetime = true,

            // The default five minutes of slack would keep a withdrawn token working past its
            // stated expiry, which is not what an operator expects when they revoke access.
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------------------
// MVC, error handling and API documentation.
// ---------------------------------------------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Entities still reach the wire in the administration endpoints, and those carry
        // navigation properties in both directions.
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.AddOpenApi();

// ---------------------------------------------------------------------------------------
// CORS.
//
// A browser client sends its bearer token to this API from another origin, so the allowed
// origins have to be named. Outside Development an empty list is a configuration mistake
// rather than an instruction to allow everyone, and it stops the server the same way a
// missing signing key does - an API that answers any origin with credentials enabled is
// exactly the hole this policy exists to close.
// ---------------------------------------------------------------------------------------
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

if (allowedOrigins.Length == 0 && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Cors:AllowedOrigins is empty. Name the origins that may call this API " +
        "(for example the deployed Blazor client's URL) before starting outside Development.");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
        else
        {
            // Development only, by the guard above: a local client on a shifting port.
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

var app = builder.Build();

// ---------------------------------------------------------------------------------------
// Start-up bootstrap.
//
// Creates the role catalogue, registers each configured company against its tenant database,
// and creates one account per role so somebody can sign in. Entirely additive and idempotent:
// it matches on company code and username, and never deletes or overwrites anything.
// ---------------------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        var bootstrapper = scope.ServiceProvider.GetRequiredService<IMasterBootstrapper>();
        await bootstrapper.RunAsync();
    }
    catch (Exception ex)
    {
        // A master database that is briefly unreachable should not stop the process; sign-in
        // will fail loudly on its own and the next restart will finish the job.
        logger.LogError(ex, "Master bootstrap did not complete. Sign-in may not work yet.");
    }
}

// ---------------------------------------------------------------------------------------
// Pipeline.
// ---------------------------------------------------------------------------------------

// Converts unhandled exceptions into ProblemDetails without leaking infrastructure detail.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
    // Interactive API reference over the OpenAPI document, for testing endpoints by hand.
    app.MapScalarApiReference(options => options.WithTitle("FitCore ERP API"));
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

// Must run after authentication so the company claim is visible, and before the controllers
// that read tenant data.
app.UseTenantResolution();

app.MapControllers();

app.Run();

/// <summary>
/// Exposed so the integration tests can build the same host the application runs.
/// </summary>
public partial class Program;
