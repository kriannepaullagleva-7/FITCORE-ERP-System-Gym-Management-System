# FitCore ERP System - Project Documentation

## Project Overview

FitCore ERP is a multi-tenant SaaS enterprise resource planning system for fitness facilities:

- **ASP.NET Core Web API** (`ERP_api`) exposing REST controllers under `/api/...`
- **Blazor WebAssembly client** (`ERP_UI`) that talks to the API over HTTPS only
- **Windows Forms desktop app** (`ERP_winforms`) that uses the service layer directly
- **Database-per-tenant architecture**, with a master registry mapping a company to its database
- **EF Core 10.0.12** with migrations for both the master and tenant databases

### Request flow

```
Browser
   |
ERP_UI (Blazor WebAssembly)
   |  HTTPS + JSON, via typed API service classes
ERP_api (Controllers)
   |
Services (business logic)
   |
Repositories (data access)
   |
Tenant resolution  ->  ITenantDbContextFactory
   |
TenantErpDbContext
   |
The tenant's own SQL Server database
```

The master database is reached separately:

```
ERP_api  ->  ICompanyDirectoryService  ->  MasterErpDbContext  ->  Master SQL Server database
```

## Project Structure

```
ERP_Project1/
├── ERP_api/                        # ASP.NET Core Web API (main server)
│   ├── Program.cs                  # DI, pipeline, CORS, OpenAPI. No business endpoints.
│   ├── Controllers/                # Thin controllers, one per ERP resource
│   ├── DTOs/                       # Wire contracts + ApiMappings (entity -> DTO)
│   ├── Tenancy/
│   │   └── TenantResolutionMiddleware.cs
│   ├── Infrastructure/
│   │   ├── ApiExceptionHandler.cs        # Exception -> ProblemDetails
│   │   └── CrossTenantAdminAttribute.cs  # Guards company-id-in-URL endpoints
│   └── appsettings.json
│
├── ERP_UI/                         # Blazor WebAssembly client
│   ├── Program.cs                  # HttpClient base address + typed API services
│   ├── Services/                   # MemberApiService, MembershipPlanApiService, ...
│   ├── Pages/                      # Home, Dashboard, Members, MembershipPlans
│   ├── DTOs/                       # Client-side copies of the wire contracts
│   └── wwwroot/appsettings.json    # ApiBaseUrl
│
├── ERP_domain/                     # POCO entities. No dependencies at all.
│   └── entities/
│
├── ERP_infrastructure/             # Data access, services, tenancy
│   ├── data/                       # MasterErpDbContext, TenantErpDbContext, factories
│   ├── repositories/
│   ├── services/                   # Business logic + tenant resolver/factory
│   ├── tenant/                     # Tenant context, options, DI wiring
│   └── Migrations/
│       ├── *.cs                    # Master database migrations
│       └── TenantErpDb/*.cs        # Tenant database migrations
│
├── ERP_Project1/                   # Windows Forms desktop app (ERP_winforms.csproj)
│
├── ERP_Tests/                      # xUnit tests (tenancy, error handling, service rules)
│
└── sql/
    └── FixTenantRegistry.sql       # Repairs the CompanyDatabases rows (master DB)
```

## Multi-tenancy

### How a request finds its database

`TenantResolutionMiddleware` runs once per request, after authentication and before the
controllers, and picks the company in this order:

1. **A company claim on the authenticated user.** Authoritative. Claim types are configurable
   (`company_id`, `companyId`, `CompanyId`, `tenant_id`). This becomes the only source that
   matters once JWT authentication is enabled.
2. **The `X-Company-Id` header**, but only when the host environment is Development *and*
   `Tenancy:AllowHeaderOverride` is true. Both conditions are required, so this cannot be
   switched on in production by configuration alone.
3. **`Tenancy:DefaultCompanyId`**, which keeps the current single-tenant deployment working.

The company id then becomes a connection string:

```
CompanyId
  -> CompanyDatabases row in the master DB (ServerName, DatabaseName, CredentialKey)
  -> appsettings "TenantCredentials:<CredentialKey>" (UserId, Password)
  -> connection string
```

The master database stores only a credential *key*, never a password. The resolved connection
string is held in a scoped `ITenantContext` and is never sent to a client.

### Why every service is tenant-aware without knowing it

`TenantErpDbContext` is registered as a scoped service built from the resolved tenant:

```csharp
services.AddScoped<TenantErpDbContext>(sp =>
    sp.GetRequiredService<ITenantDbContextFactory>().CreateForCurrentTenant());
```

Every repository and service already takes a `TenantErpDbContext` through its constructor, so
this one registration makes the whole stack tenant-aware with no change to any of them. It is
resolved lazily, so endpoints that never touch tenant data never need a tenant.

### Tenant security rules

- A client must never choose a tenant. No business route contains a company id.
- The endpoints that *do* take a company id in the URL are SaaS administration
  (`/api/companies*`, `/api/tenant/probe/{companyId}`). They carry `[CrossTenantAdmin]` and
  return 403 unless `Tenancy:EnableCrossTenantAdminApi` is true. They should also be placed
  behind an administrator authorization policy once authentication exists.
- Connection strings and credentials never reach `ERP_UI`, the browser, or JavaScript.

### The fallback

`Tenancy:AllowConnectionStringFallback` is now **false**: strict tenancy is in force. When a
company has no usable `CompanyDatabases` row the request fails with 503 rather than quietly
being served the default database.

The registry was repaired by `sql/FixTenantRegistry.sql`, which is kept for reference and is
safe to re-run. Both companies now resolve to their own database, and
`GET /api/tenant/current` reports `"usingFallbackConnection": false`.

Turn the flag back on only as a temporary measure while onboarding, and expect a warning in
the log on every request that uses it.

### Onboarding a new tenant

1. `POST /api/companies` to register the company.
2. `POST /api/companies/databases` with its server, database name and a `credentialKey` that
   names an entry under `TenantCredentials` in server configuration.
3. `POST /api/companies/{companyId}/provision` to apply the tenant migrations to that
   database. It is idempotent, so it is safe to re-run.
4. `GET /api/companies/{companyId}/provisioning` to confirm there are no pending migrations.

Step 3 matters: registering a company only records where its database lives. Without it the
database exists but has no tables, and every request for that tenant fails with
"Invalid object name".

## Enterprise tiers and the three tenants

FitCore is sold in tiers, and the tier decides which modules a company has at all. The tier is
a column on the master `Companies` row, not something inferred from the connection string, so a
tenant can be promoted without a code change.

| Tier | Company | Database | Credential key | Modules |
|---|---|---|---|---|
| **Micro** | `COMP002` (id 3) | `db68433` | `TenantA` | Membership, Sales, Payments, Inventory (+ Expenses, Reports, User Access) |
| **Small** | `COMP003` (id 4) | `db68484` | `TenantB` | Everything in Micro **plus Employees and Payroll** |
| **Medium** | `COMP001` (id 1) | `db68521` | `FitcoreCredential` | Reserved for future development. Registered but `IsActive = false` for sign-in, and no accounts are seeded for it. |

`ERP_domain/entities/ErpModule.cs` is the single catalogue: it names every module, the group it
appears under in the sidebar, and the minimum tier that includes it. Only `employees` and
`payroll` require Small.

The tenant *schema* is shared - one `TenantErpDbContext`, one migration chain, applied to each
tenant database. Isolation is physical: Tenant A data lives in `db68433` and Tenant B data lives
in `db68484`, and no request can reach across because the company comes from the signed token.

## Configuration

```jsonc
{
  "Tenancy": {
    "DefaultCompanyId": 0,          // off: no anonymous request is served a real company
    "AllowHeaderOverride": false,   // and ignored entirely for an authenticated caller
    "AllowConnectionStringFallback": false
  },
  "Jwt": {
    "Issuer": "FitCoreERP",
    "Audience": "FitCoreERP.Client",
    "SigningKey": "",               // required; the API refuses to start without it
    "TokenLifetimeMinutes": 480
  },
  "Bootstrap": {
    "Enabled": true,
    "SeedPassword": "...",
    "Tenants": [ /* company code, tier, server, database, credential key, users */ ]
  }
}
```

**Security note:** `Jwt:SigningKey` is the thing that keeps one tenant out of another tenant's
database - anyone who can mint a token can name any company. The development key in
`appsettings.Development.json` must never be used in a deployed environment. Move it, the
connection strings and the `TenantCredentials` passwords to user secrets, environment variables
or a key vault, and rotate them, since they have been committed.
## API

All business routes are tenant-scoped and carry no company id.

| Module | Routes |
|---|---|
| Members | `GET`/`POST /api/members`, `GET`/`PUT`/`DELETE /api/members/{id}`, `GET /api/members/search?term=`, `GET /api/members/{id}/subscriptions`, `/payments`, `/sales` |
| Membership plans | `GET`/`POST /api/membership-plans`, `GET /api/membership-plans/active`, `GET`/`PUT`/`DELETE /api/membership-plans/{id}` |
| Subscriptions | `GET`/`POST /api/subscriptions`, `GET /api/subscriptions/active`, `GET`/`DELETE /api/subscriptions/{id}`, `POST /api/subscriptions/{id}/renew`, `/cancel`, `POST /api/subscriptions/expire-overdue`, `GET /api/subscriptions/{id}/payments` |
| Payments | `GET`/`POST /api/payments`, `GET`/`PUT`/`DELETE /api/payments/{id}`, `PATCH /api/payments/{id}/status` |
| Sales | `GET`/`POST /api/sales`, `GET`/`DELETE /api/sales/{id}`, `GET /api/sales/{id}/items` |
| Products | `GET`/`POST /api/products`, `GET /api/products/active`, `GET`/`PUT`/`DELETE /api/products/{id}` |
| Inventory | `GET /api/inventory`, `GET /api/inventory/movements`, `GET /api/inventory/{productId}`, `POST /api/inventory/{productId}/stock-in`, `/stock-out`, `/adjust`, `PUT /api/inventory/{productId}/reorder-level` |
| Customers / Suppliers | `GET`/`POST /api/customers`, `GET`/`POST /api/suppliers`, plus `GET`/`DELETE` by id |
| Reports | `GET /api/reports/dashboard`, `GET /api/reports/membership-overview` |
| Employees | `GET`/`POST /api/employees`, `GET /api/employees/active`, `/search?term=`, `GET`/`PUT`/`DELETE /api/employees/{id}`, `GET /api/employees/{id}/payrolls` |
| Payroll | `GET`/`POST /api/payroll`, `GET /api/payroll/summary`, `GET`/`PUT`/`DELETE /api/payroll/{id}`, `PATCH /api/payroll/{id}/status` |
| Authentication | `POST /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/refresh`, `/change-password`, `/logout` |
| User Access | `GET`/`POST /api/users`, `GET /api/users/roles`, `/modules`, `GET`/`PUT /api/users/{id}`, `GET`/`PUT /api/users/{id}/permissions`, `PATCH /api/users/{id}/role`, `/status`, `POST /api/users/{id}/reset-password` |
| Tenant | `GET /api/tenant/current`, `GET /api/tenant/probe/{companyId}` (admin) |
| System administration | `GET`/`POST /api/companies`, `/api/companies/databases`, `/api/companies/devices`, `GET /api/companies/{id}/provisioning`, `POST /api/companies/{id}/provision` (all admin) |

Status codes in use: 200, 201, 204, 400, 401, 403, 404, 409, 500, 503.

Every business route requires a bearer token and the module it belongs to. `POST /api/auth/login`
is the only anonymous endpoint.

Errors come back as RFC 9457 ProblemDetails. `ApiExceptionHandler` maps `ValidationException`
and `InvalidOperationException` to 400, tenant resolution failures to 503 with a generic
message, and database provider errors to 500. Messages are scrubbed of anything resembling a
connection string, and the full exception is logged server-side.

### API documentation UI

In Development, `/openapi/v1.json` serves the OpenAPI document and `/scalar/v1` serves an
interactive reference (`Scalar.AspNetCore`). The root path redirects there. Neither is exposed
outside Development.

## Databases

1. **MasterErp** - `Companies`, `CompanyDatabases`, `Devices`, plus the ASP.NET Identity
   tables. `MasterErpDbContext` derives from `IdentityDbContext`; no user records exist yet.
2. **TenantErp** (one per tenant) - `Members`, `MembershipPlans`, `Subscriptions`, `Payments`,
   `Sales`, `SaleItems`, `Products`, `Inventories`, `StockMovements`, `Customers`,
   `Suppliers`.

There is no `CompanyId` column on tenant tables: isolation comes from connecting to a
different physical database.

## Building & Running

```bash
# Build everything
cd C:\Users\USER\source\repos\ERP_Project1
dotnet build

# API           -> https://localhost:7214 (docs at /scalar/v1)
cd ERP_api && dotnet run

# Blazor client -> https://localhost:7031
cd ERP_UI && dotnet run

# Desktop app
cd ERP_Project1 && dotnet run
```

### Migrations

```bash
cd ERP_infrastructure
dotnet ef database update --context TenantErpDbContext
dotnet ef database update --context MasterErpDbContext
dotnet ef migrations add "DescriptiveName" --context TenantErpDbContext
```

Both contexts are in sync with their snapshots: `dotnet ef migrations
has-pending-model-changes` reports no changes for either.

## Windows Forms app

`ERP_winforms` is unchanged by the API migration. It builds its own `ServiceCollection`,
registers `AddDbContext<TenantErpDbContext>` against the fixed `TenantErp` connection string,
and consumes the same `ERP_infrastructure` services the API does. It does **not** call the
Web API and has no tenant concept.

Database-backed forms: `Form1` (members), `MembershipForm`, `MembershipPlanForm`,
`SubscriptionForm`, `DashboardForm`. Static in-memory previews: `SalesForm`, `PaymentForm`,
`InventoryForm`.

Because both the API and the desktop app bind to `ERP_infrastructure`, changing a service
interface or an entity breaks both. Change them together.

### Color scheme

- Primary Blue RGB(77, 130, 222), Light Blue RGB(204, 224, 247), Pale Blue RGB(230, 237, 251)
- Action buttons: green add, orange update, red delete

## Development Guidelines

- Controllers stay thin: no EF Core, no business rules. Controller to service to repository.
- Return DTOs, not entities. `ApiMappings` holds the `ToDto()` projections.
- Add a controller per ERP resource, not per table. Child entities such as `SaleItem` are
  reached through their parent.
- Declare literal routes (`active`, `search`, `movements`) before parameter routes so the
  literal wins the match, and constrain id segments with `:int`.
- Never accept a tenant identifier from the client on a business endpoint.

### Adding a feature

1. Entity in `ERP_domain/entities/`
2. `DbSet` and configuration in `TenantErpDbContext`
3. Repository in `ERP_infrastructure/repositories/`
4. Service in `ERP_infrastructure/services/`
5. Register both in `InfrastructureServiceCollectionExtensions.AddErpApplicationServices`
6. DTOs and a `ToDto()` in `ERP_api/DTOs/`
7. Controller in `ERP_api/Controllers/`
8. Migration, then a typed service and page in `ERP_UI`

## Authentication

Implemented. JWT bearer, issued by `POST /api/auth/login`.

```
AppUsers (master DB)
  -> password verified with PBKDF2 (ASP.NET Core PasswordHasher)
  -> account active?  company active?
  -> role  +  per-user overrides  +  company tier
  -> effective module list
  -> signed token carrying company_id, role_key and one "module" claim per module
```

The token is what makes tenancy safe. `TenantResolutionMiddleware` reads `company_id` from it
and turns that into a connection string, so a client cannot choose a tenant: there is no request
field that selects one.

Accounts live in the **master** database rather than in a tenant database, because a user is the
thing that *selects* a tenant. Storing accounts per tenant would require knowing the tenant
before authenticating, which is the wrong way round.

### Seeded accounts

`MasterBootstrapper` runs at start-up, driven by the `Bootstrap` section of configuration. It is
idempotent and purely additive: it matches companies by code and users by username, and never
deletes a row or overwrites a password that has been set.

| Username | Name | Role | Tenant |
|---|---|---|---|
| `admin` | Kris Santos | Admin / Owner | B (Small) |
| `manager` | John Doe | Manager | B (Small) |
| `staff` | Maria Santos | Receptionist / Staff | B (Small) |
| `micro.admin` | Elena Reyes | Admin / Owner | A (Micro) |
| `micro.manager` | Paolo Cruz | Manager | A (Micro) |
| `micro.staff` | Ana Lim | Receptionist / Staff | A (Micro) |

All are created with `Bootstrap:SeedPassword` and `MustChangePassword = true`.

## Roles and permissions

```
AppUser  ->  AppRole  ->  AppRolePermission   (what the role grants by default)
   |
   +------->  AppUserPermission               (a departure from the role, either way)
                    |
                    +-- narrowed by the company's EnterpriseTier
                                 |
                                 v
                         effective modules
```

Defaults: Admin and Manager hold every module their tier includes. Staff holds Dashboard,
Membership, Sales, Payments and Inventory, and *not* Employees, Payroll or User Access.

Overrides are stored rather than the whole effective set, so a change to a role still reaches
everyone who has not been singled out. Ticking a box back in step with the role deletes the
override rather than recording a redundant one.

`PermissionResolver` is the only place this is computed. The sidebar, the route guard, the API
filter and the permission editor all call it, so they cannot drift apart.

### Backend enforcement

`[RequireModule("payroll")]` on a controller answers **403** for a caller without the module.
Hiding a sidebar entry is a courtesy; this is the boundary. Where a controller-wide rule and an
action rule disagree, the action wins - which is how Staff reaches
`GET /api/reports/dashboard` on a controller otherwise reserved for Reports.

`/api/companies` additionally requires the Admin role and the cross-tenant flag.

## Testing

```bash
dotnet test ERP_Tests/ERP_Tests.csproj
```

`ERP_Tests` covers the parts where a mistake is expensive:

- **Tenant resolution precedence** - a claim beats the header, the header is ignored outside
  Development, and an unauthenticated claim is not trusted. These encode the rule that a
  client cannot select another company's database.
- **Connection string resolution** - registry lookup, the fallback, and the failure cases
  (missing credential key, no matching credentials, fallback disabled).
- **Tenant context** - reading a connection string before a tenant is resolved throws, so no
  code path can silently open the wrong database.
- **Error translation and scrubbing** - business messages survive, while anything resembling
  a credential or connection string never reaches a client.
- **Service rules** - deleting a member with history, duplicate product codes, and stock
  movement arithmetic, run against a real SQLite database rather than a fake.
- **Access rules** - that a Micro tenant never reaches Employees or Payroll however senior the
  account, that a per-user override beats the role in both directions, and that an override
  cannot grant a module the tier withholds.

The service tests use SQLite in memory so the repository queries genuinely execute. They do
not touch the remote databases.

## Known issues and follow-ups

1. **Plaintext credentials** in both `appsettings.json` files. Rotate and move to a secret
   store. This is the largest remaining risk.
2. **The JWT signing key is in `appsettings.Development.json`.** It is a development value and
   must be replaced before deployment; anyone holding it can mint a token for any company.
3. **Permissions are carried in the token**, so withdrawing a module reaches a user who is
   already signed in only when their token is refreshed or they sign in again. The client calls
   `/api/auth/me` on every page load and refreshes after an administrator edits their own
   access, so in practice the window is one page load - but it is not instant revocation.
4. **The desktop app has no tenant concept.** `ERP_winforms` still binds `TenantErpDbContext`
   to the fixed `TenantErp` connection string, so it always talks to Tenant A and knows nothing
   about sign-in, roles or the Small Enterprise modules.
5. **Empty migration** `20260915173453_AddSuppliersToTenantErp` is a no-op left in the chain.
   Harmless; leave it, since it is already applied.
6. **`ERP_UI/DTOs` duplicates `ERP_api/DTOs`.** Deliberate, so the Blazor client keeps zero
   project references. A shared contracts project would remove the duplication.
7. **Email validation is now enforced on write.** One existing member record holds a
   malformed email, so that member cannot be re-saved until it is corrected. The API replies
   with a field-level message saying so.
8. **Tenant B carries one demonstration Employee and one Payroll run** (`EMP-112806`, Liza
   Manalo), created through the API while verifying the Small Enterprise expansion. They are
   real rows, not fixtures; delete them from the Employees screen if they are not wanted.
9. **`SaleService` opens its own transaction** through the DbContext, bypassing the
   repositories. That is correct today but means the repository layer has no unit of work.

## Troubleshooting

**Every request logs a tenant fallback warning** - expected until `sql/FixTenantRegistry.sql`
has been run. Check `GET /api/tenant/current`.

**503 "Tenant database unavailable"** - the company has no usable `CompanyDatabases` row and
the fallback is disabled. The server log has the real reason.

**403 on `/api/companies`** - `Tenancy:EnableCrossTenantAdminApi` is false. It is enabled in
`appsettings.Development.json` only.

**Blazor shows "The API could not be reached"** - check that `ERP_UI/wwwroot/appsettings.json`
`ApiBaseUrl` matches the API port, and that the UI origin is in `Cors:AllowedOrigins`.

**Cannot connect to the database** - the databases are remote (`*.public.databaseasp.net`),
not a local SQL Server instance. There is no local SQL Server on this machine.

---

**Last architecture migration:** 2026-09-18. Minimal APIs became MVC controllers, dependency
injection became tenant-aware, strict tenancy was switched on with per-tenant provisioning,
request validation was added, the report queries were batched, and a test suite was added.

**Team:** FitCore Development Team
