# FitCore ERP System

Multi-tenant SaaS ERP for gyms. ASP.NET Core 10 Web API, Blazor WebAssembly client,
EF Core 10, SQL Server, database-per-company.

Everything below describes what the code actually does. Where a feature is not built, it says
so. Nothing here is aspirational.

---

## Status at a glance

| | |
|---|---|
| Build | ✅ 0 errors, 0 warnings |
| Tests | ✅ 135 passing |
| EF model vs migrations | ✅ both contexts in sync |
| Micro Enterprise | ✅ working end to end against the live database |
| Small Enterprise | ✅ working end to end, including Employees and Payroll |
| Medium Enterprise | ❌ tier is defined and enforced; none of its features are built |

---

## Projects

```
ERP_domain          POCO entities and the module catalogue. No dependencies.
ERP_infrastructure  DbContexts, repositories, services, tenancy. No ASP.NET Core reference.
ERP_api             Thin MVC controllers, JWT, tenant middleware.        https://localhost:7214
ERP_UI              Blazor WebAssembly client.                           https://localhost:7031
ERP_Tests           xUnit: unit, service (SQLite) and HTTP integration tests.
ERP_Project1        WinForms desktop app (ERP_winforms.csproj). Legacy, single-tenant.
```

`ERP_infrastructure` deliberately has no ASP.NET Core reference — it is consumed by the desktop
application as well as the API. Anything HTTP-aware lives in `ERP_api` behind an interface.

### Request flow

```
Browser → ERP_UI → HTTPS/JSON → ERP_api controller
                                      ↓
                                   Service  (business rules)
                                      ↓
                                 Repository  (queries)
                                      ↓
                              TenantErpDbContext  (bound to the caller's company)
                                      ↓
                               that tenant's own database
```

The master database is reached separately, for sign-in and the tenant registry.

---

## Enterprise tiers

The tier is a column on the master `Companies` row, so a tenant can be promoted without a code
change. `ERP_domain/entities/ErpModule.cs` is the single catalogue.

| Module | Micro | Small | Medium |
|---|:--:|:--:|:--:|
| Dashboard | ✅ | ✅ | ✅ |
| Membership Management | ✅ | ✅ | ✅ |
| Sales Management | ✅ | ✅ | ✅ |
| Payment Management | ✅ | ✅ | ✅ |
| Inventory Management | ✅ | ✅ | ✅ |
| Reports | ✅ | ✅ | ✅ |
| Employee Management | — | ✅ | ✅ |
| Payroll Management | — | ✅ | ✅ |
| Expenses | — | — | ❌ not built |
| Finance Management | — | — | ❌ not built |
| Business Intelligence | — | — | ❌ not built |
| User Access | — | — | ❌ screen exists, tier-gated off |
| System Administration | — | — | ❌ API exists, tier-gated off |

**The tier is a hard ceiling.** `PermissionResolver` seeds its result from the tier's module
list, so a permission row granting a module the company's tier does not include can never take
effect — not for an override, not for an administrator, not for a Super Admin. This is covered
by tests, including one that grants a Medium module to a Super Admin on a Micro tenant and
asserts they still get nothing.

### Consequence worth knowing

Moving User Access to Medium means **Micro and Small tenants have no in-app user management**.
Accounts for those tiers are provisioned by the `Bootstrap` configuration section at start-up,
which is idempotent and additive. Self-service password change is *not* module-gated and keeps
working for everyone.

If this proves impractical, it is one line in `ErpModule.cs`: change `useraccess` back to
`EnterpriseTier.Micro`. Nothing else needs to change — the sidebar, the route guard and the API
filter all read the catalogue.

### The three tenants

| Tier | Company | Id | Database | Credential key |
|---|---|---|---|---|
| Micro | `COMP002` | 3 | `db68433` | `TenantA` |
| Small | `COMP003` | 4 | `db68484` | `TenantB` |
| Medium | `COMP001` | 1 | `db68521` | `FitcoreCredential` |

The Medium company is registered but `Enabled: false`, with no accounts seeded.

The tenant *schema* is shared: one `TenantErpDbContext`, one migration chain, applied to each
database. Isolation is physical — Tenant A's data is in `db68433`, Tenant B's in `db68484`, and
no request can cross because the company comes from the signed token.

---

## Roles

| Role | Level | Default modules (before the tier ceiling) |
|---|:--:|---|
| Super Admin | 0 | everything |
| Admin / Owner | 1 | everything except System Administration |
| Manager | 2 | everything except System Administration and User Access |
| Receptionist / Staff | 3 | Dashboard, Membership, Sales, Payments, Inventory |

Lower level is more senior. Defaults are computed from the catalogue, so adding a module cannot
silently leave a role behind. They are only a seed: they are written to `AppRolePermissions` on
first run and an administrator owns them afterwards.

Per-user departures live in `AppUserPermissions` and can grant or withhold in either direction —
within the tier. Storing the *departure* rather than the whole effective set means a change to a
role still reaches everyone who has not been singled out.

### Seeded accounts

All created with `Bootstrap:SeedPassword` and `MustChangePassword = true`.

| Username | Name | Role | Tenant |
|---|---|---|---|
| `admin` | Kris Santos | Admin / Owner | B (Small) |
| `manager` | John Doe | Manager | B (Small) |
| `staff` | Maria Santos | Receptionist / Staff | B (Small) |
| `micro.admin` | Elena Reyes | Admin / Owner | A (Micro) |
| `micro.manager` | Paolo Cruz | Manager | A (Micro) |
| `micro.staff` | Ana Lim | Receptionist / Staff | A (Micro) |

No Super Admin account is seeded — platform administration is granted deliberately, not by
default.

---

## Multi-tenancy

### How a request finds its database

`TenantResolutionMiddleware` runs after authentication, before the controllers:

1. **The company claim on the token.** Authoritative.
2. **The `X-Company-Id` header** — only when the host is Development *and*
   `Tenancy:AllowHeaderOverride` is true. Both are required, so it cannot be switched on in
   production by configuration alone.
3. **`Tenancy:DefaultCompanyId`**, which is `0` (none) by default.

Then:

```
CompanyId → CompanyDatabases row (ServerName, DatabaseName, CredentialKey)
          → TenantCredentials:<CredentialKey> in server configuration
          → connection string, held in a scoped ITenantContext
```

The master database stores only a credential *key*, never a password. The connection string
never leaves the server.

### Why every service is tenant-aware without knowing it

```csharp
services.AddScoped<TenantErpDbContext>(sp =>
    sp.GetRequiredService<ITenantDbContextFactory>().CreateForCurrentTenant());
```

Every repository and service already takes a `TenantErpDbContext` through its constructor, so
this one registration makes the whole stack tenant-aware. It resolves lazily, so endpoints that
never touch tenant data never need a tenant.

### Connection strings

Built with `SqlConnectionStringBuilder`, not concatenated — a hosting-panel password containing
a semicolon or quote would otherwise end the string early.

Host names from the registry get an explicit `tcp:` prefix and port `1433`. Without this,
SqlClient can fall back to Named Pipes against a remote host and report "the server was not
found" even though the name resolves and the port is open. A value that already names a
protocol, port or instance is left alone.

### Defaults fail closed

`TenantOptions` defaults are the strict values — `DefaultCompanyId = 0`,
`AllowConnectionStringFallback = false`, `AllowHeaderOverride = false`. Configuration can relax
them for a development machine, but a deployment that ships without a `Tenancy` section, or
with it mistyped, gets strict tenancy rather than a quietly permissive server.

---

## Authentication and authorization

JWT bearer, issued by `POST /api/auth/login` — the only anonymous endpoint.

```
AppUsers (master DB)
  → password verified with PBKDF2 (ASP.NET Core PasswordHasher)
  → account active? company active?
  → role + per-user overrides + company tier
  → effective module list
  → signed token carrying company_id, role_key and one "module" claim per module
```

Accounts live in the **master** database because a user is the thing that *selects* a tenant.
Storing them per tenant would mean knowing the tenant before authenticating, which is the wrong
way round.

### Enforcement

`[RequireModule("payroll")]` answers **403** for a caller without the module. Hiding a sidebar
entry is a courtesy; this is the boundary. Where a controller rule and an action rule disagree,
the action wins — which is how Staff reach `GET /api/reports/dashboard` on a controller
otherwise reserved for Reports.

`/api/companies` additionally requires the Admin or Super Admin role and the cross-tenant flag.

**Known limitation:** permissions travel in the token, so withdrawing a module reaches a
signed-in user when their token refreshes, not instantly. The client calls `/api/auth/me` on
every page load, so in practice the window is one page load.

---

## Audit trail

`AuditEvent` is mapped into **both** databases:

- **Tenant database** — member, sale, payment, inventory, payroll, employee actions, beside the
  rows they describe, so a tenant's history travels with its data.
- **Master database** — sign-in, failed sign-in, password change, permission and company
  changes. A failed sign-in has no company, so there is no tenant database to write it to.

Capture is hybrid:

- A **change-tracker sweep** in `SaveChanges` records create, update and delete automatically,
  and cannot be forgotten. Updates serialise only the properties that actually changed.
- Services raise **named actions** (`Login`, `PayrollPaid`, `StockAdjusted`) explicitly, because
  the tracker sees a column change rather than a business event.

Anything named like a credential is dropped before serialisation, as is any property ending in
`Hash`. Updates that move nothing but `UpdatedAt`, `LastLoginAt` or `CreatedAt` are not
recorded. The table has no foreign keys — an audit row must outlive what it describes.

Who performed an action comes from `ICurrentUserAccessor`, implemented over `IHttpContextAccessor`
in `ERP_api`. It is a constructor argument on the DbContexts rather than an interceptor on
`DbContextOptions`, because those options are cached per connection string for the life of the
process — an interceptor attached there would outlive the request and could attribute one
tenant's write to another tenant's user.

---

## Databases

**Master** — `Companies`, `CompanyDatabases`, `Devices`, `AppUsers`, `AppRoles`,
`AppRolePermissions`, `AppUserPermissions`, `AuditEvents`, plus the ASP.NET Identity tables.

**Tenant** (one per company) — `Members`, `MembershipPlans`, `Subscriptions`, `Payments`,
`Sales`, `SaleItems`, `Products`, `Inventories`, `StockMovements`, `Employees`, `Payrolls`,
`Expenses`, `Customers`, `Suppliers`, `AuditEvents`.

There is no `CompanyId` column on tenant tables: isolation comes from connecting to a different
physical database.

### Money and precision

Every monetary column is `decimal` with `HasPrecision(18, 2)`. There is no `double` or `float`
anywhere in the domain.

### Deleting things

`Subscriptions`, `Sales` and `Payments` are **restricted** against `Members`. A member who has
ever paid, subscribed or bought something cannot be deleted, because that would take the gym's
takings with them. `MemberService` refuses it first with a clear message; the foreign key
enforces the same rule against any write that goes around the service.

The alternative offered instead is **archiving**: `POST /api/members/{id}/archive` sets the
existing `Status` column to `Archived`. That reuses a column the application already filters on,
so no report, dashboard or revenue figure changes meaning, and archived members still count in
anything historical. `POST /api/members/{id}/restore` reverses it.

---

## API

All business routes are tenant-scoped and carry no company id.

| Module | Routes |
|---|---|
| Members | `GET`/`POST /api/members`, `GET`/`PUT`/`DELETE /api/members/{id}`, `POST /api/members/{id}/archive`, `/restore`, `GET /api/members/search?term=`, `/{id}/subscriptions`, `/payments`, `/sales` |
| Membership plans | `GET`/`POST /api/membership-plans`, `GET /api/membership-plans/active`, `GET`/`PUT`/`DELETE /api/membership-plans/{id}` |
| Subscriptions | `GET`/`POST /api/subscriptions`, `/active`, `GET`/`DELETE /{id}`, `POST /{id}/renew`, `/cancel`, `POST /api/subscriptions/expire-overdue`, `GET /{id}/payments` |
| Payments | `GET`/`POST /api/payments`, `GET`/`PUT`/`DELETE /{id}`, `PATCH /{id}/status` |
| Sales | `GET`/`POST /api/sales`, `GET`/`DELETE /{id}`, `GET /{id}/items` |
| Products | `GET`/`POST /api/products`, `/active`, `GET`/`PUT`/`DELETE /{id}` |
| Inventory | `GET /api/inventory`, `/movements`, `/{productId}`, `POST /{productId}/stock-in`, `/stock-out`, `/adjust`, `PUT /{productId}/reorder-level` |
| Customers / Suppliers | `GET`/`POST /api/customers`, `/api/suppliers`, plus `GET`/`DELETE` by id. **API only — no UI page.** |
| Reports | `GET /api/reports/dashboard`, `/membership-overview` |
| Employees | `GET`/`POST /api/employees`, `/active`, `/search?term=`, `GET`/`PUT`/`DELETE /{id}`, `GET /{id}/payrolls` |
| Payroll | `GET`/`POST /api/payroll`, `/summary`, `GET`/`PUT`/`DELETE /{id}`, `PATCH /{id}/status` |
| Expenses | `GET`/`POST /api/expenses`, … — Medium only, so 403 on every current tenant |
| Authentication | `POST /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/refresh`, `/change-password`, `/logout` |
| User Access | `GET`/`POST /api/users`, … — Medium only, so 403 on every current tenant |
| Tenant | `GET /api/tenant/current`, `GET /api/tenant/probe/{companyId}` (admin) |
| System administration | `GET`/`POST /api/companies`, `/databases`, `/devices`, `GET /{id}/provisioning`, `POST /{id}/provision` — Medium + Super Admin + flag |

Errors are RFC 9457 ProblemDetails. `ApiExceptionHandler` maps `ValidationException` and
`InvalidOperationException` to 400, tenant resolution failures to 503 with a generic message,
and database provider errors to 500. Messages are scrubbed of anything resembling a connection
string, and the full exception is logged server-side.

In Development, `/openapi/v1.json` serves the document and `/scalar/v1` an interactive
reference. Neither is exposed outside Development.

---

## Business rules worth knowing

- **Sales** run in a database transaction. Prices come from the catalogue, never from the
  client; totals are computed server-side; duplicate lines are merged before the stock check;
  stock cannot go negative. Cancel and delete return stock, also transactionally.
- **Inventory** stock-in, stock-out and adjustment all write a `StockMovement` carrying the
  balance before and after. Adjustment records an absolute counted quantity and the difference.
- **Payroll** refuses any run whose period **overlaps** an existing run for the same employee —
  not merely one with identical dates. 1–15 March and 10–20 March share six days, and paying
  both pays those days twice. Consecutive periods are fine; two employees may share a period.
  Paid runs cannot be edited or deleted.
- **Subscriptions** derive their end date from the plan's duration. Renewing extends from the
  existing end date when it is still in the future.

---

## Configuration

```jsonc
{
  "Tenancy": {
    "DefaultCompanyId": 0,                 // none
    "AllowHeaderOverride": false,          // and ignored outside Development regardless
    "AllowConnectionStringFallback": false,
    "EnableCrossTenantAdminApi": false,
    "EncryptTenantConnections": false,     // see the note below
    "TrustServerCertificate": true,
    "ConnectTimeoutSeconds": 60
  },
  "Jwt": {
    "Issuer": "FitCoreERP",
    "Audience": "FitCoreERP.Client",
    "SigningKey": "",                      // required; the API refuses to start without it
    "TokenLifetimeMinutes": 480
  },
  "Cors": { "AllowedOrigins": [] },        // required outside Development; empty stops start-up
  "Bootstrap": { "Enabled": true, "SeedPassword": "", "Tenants": [ /* ... */ ] }
}
```

### Secrets

**No credentials are committed.** `appsettings.json` ships the keys with empty values.

Local development reads them from `appsettings.Development.json`, which is **gitignored**, or
from .NET user-secrets. `setup-secrets.ps1` at the repo root loads user-secrets; fill in the
placeholder values first. Note that user-secrets are loaded *after* `appsettings.Development.json`
and therefore win, so do not set the same key in both.

Production reads them from environment variables:

```
ConnectionStrings__MasterErp
ConnectionStrings__TenantErp
TenantCredentials__TenantA__Password
TenantCredentials__TenantB__Password
Bootstrap__SeedPassword
Jwt__SigningKey
Cors__AllowedOrigins__0
```

The credentials that were previously committed are preserved in `_backup_preclaude/`
(gitignored). **They should be rotated** in the MonsterASP control panel — they sat in plain
text in the repository and are in the shell history of the session that removed them.

---

## Building and running

```bash
dotnet build
dotnet test ERP_Tests/ERP_Tests.csproj

cd ERP_api && dotnet run     # https://localhost:7214, docs at /scalar/v1
cd ERP_UI  && dotnet run     # https://localhost:7031
```

The databases are remote (`*.public.databaseasp.net`). There is no local SQL Server.

### Migrations

```bash
cd ERP_infrastructure
dotnet ef migrations add "Name" --context TenantErpDbContext --output-dir Migrations/TenantErpDb
dotnet ef database update --context TenantErpDbContext
dotnet ef database update --context MasterErpDbContext
```

Point them at a specific database with `TENANT_ERP_CONNECTION` / `MASTER_ERP_CONNECTION`.

**Always `dotnet build` before `dotnet ef ... --no-build`.** A stale assembly makes EF report
pending model changes that do not exist, and makes it run against the wrong schema.

Both contexts are in sync with their snapshots.

---

## Testing

135 tests. `dotnet test ERP_Tests/ERP_Tests.csproj`

- **Authorization over real HTTP** (`ApiAuthorizationTests`) — anonymous callers refused
  everywhere but sign-in; Micro refused every module above its tier even as Admin; Small reaches
  Employees and Payroll but not User Access; Staff held to the front desk; the dashboard
  reachable on a controller otherwise reserved for Reports; tokens signed with the wrong key and
  expired tokens rejected. These drive the real pipeline and touch no database — a refusal is
  decided before anything reaches a tenant connection, which is itself the property being
  asserted.
- **Tier and permission resolution** — the full matrix, including that an explicit grant of a
  Medium module to a Super Admin on a Micro tenant still yields nothing.
- **Tenant resolution** — claim beats header, header ignored outside Development, an
  unauthenticated claim is not trusted.
- **Connection strings** — registry lookup, fallback, failure cases, and that a password
  containing connection-string syntax survives intact.
- **Audit trail** — attribution, generated keys, changed-properties-only updates, that the trail
  does not record itself, and that no password hash ever reaches it.
- **Service rules** — member delete guards, duplicate product codes, stock arithmetic, sale
  transactions, payroll calculation and overlapping periods. Run against real SQLite.

There are no browser-driven UI tests. The Blazor client is verified as serving and wired
(host page, WASM assembly, config, CORS preflight from both configured origins), but its pages
have **not** been clicked through in a browser in this session.

---

## Deployment (MonsterASP)

`appsettings.Production.json` exists for both the API and the client.

- The API's leaves `Cors:AllowedOrigins` empty **on purpose**: outside Development an empty list
  now stops start-up, so the origins must be named rather than defaulted to allowing everyone.
- The client's leaves `ApiBaseUrl` empty, which falls back to the origin it was served from. Set
  it if the API is hosted separately.

Not yet done: no publish profile, no `web.config`.

---

## Known issues and follow-ups

1. **Rotate the database credentials.** They were committed in plain text. 🔴
2. **Tenant connections are unencrypted.** `EncryptTenantConnections` defaults to `false`
   because the current shared host refuses an encrypted connection — the TLS handshake fails.
   This is a hosting limitation to raise with MonsterASP, not a preference. 🔴
3. **Medium tier is unimplemented.** The tier and its five modules exist and are enforced; none
   of the features behind them are built, and there is no Branch entity, so multi-branch is not
   supported in any form. ❌
4. **Micro and Small have no in-app user management**, by the tier decision above. Accounts come
   from the `Bootstrap` section. ⚠️
5. **`/api/customers` and `/api/suppliers` have no UI page.** Reachable API surface with no
   screen behind it. ⚠️
6. **`ERP_UI/DTOs` duplicates `ERP_api/DTOs`**, deliberately, so the client keeps zero project
   references. A shared contracts project would remove the duplication. ⚠️
7. **The desktop app has no tenant concept.** `ERP_winforms` binds `TenantErpDbContext` to the
   fixed `TenantErp` connection string, so it always talks to Tenant A and knows nothing about
   sign-in, roles or tiers. Legacy. ⚠️
8. **Empty migration** `20260915173453_AddSuppliersToTenantErp` is a no-op left in the chain.
   Harmless; it is already applied.
9. **Email validation is enforced on write.** One existing member record holds a malformed
   email and cannot be re-saved until corrected.
10. **Tenant B carries one demonstration Employee and Payroll run** (`EMP-112806`, Liza Manalo).
    Real rows, not fixtures.
11. **`SaleService` opens its own transaction** through the DbContext, bypassing the
    repositories. Correct today, but the repository layer has no unit of work.

---

## Troubleshooting

**"The server was not found" / Named Pipes error 40** — the connection string is missing the
`tcp:` prefix. The registry path adds it automatically; a hand-written `ConnectionStrings` entry
must include it.

**Error 64 on connect** — the TLS handshake failed. Set `Tenancy:EncryptTenantConnections` to
false, or use a host that supports encryption.

**Error 258, timeout** — raise `Tenancy:ConnectTimeoutSeconds`.

**API refuses to start** — either `Jwt:SigningKey` is missing or shorter than 32 characters, or
`Cors:AllowedOrigins` is empty outside Development. Both are deliberate.

**503 "Tenant database unavailable"** — the company has no usable `CompanyDatabases` row and the
fallback is disabled. The server log has the real reason.

**403 on `/api/users` or `/api/expenses`** — expected on Micro and Small. Those are Medium
modules.

**Blazor shows "The API could not be reached"** — check `ERP_UI/wwwroot/appsettings.json`
`ApiBaseUrl` against the API port, and that the UI origin is in `Cors:AllowedOrigins`.

---

## Development guidelines

- Controllers stay thin: no EF Core, no business rules. Controller → service → repository.
- Return DTOs, not entities. `ApiMappings` holds the `ToDto()` projections.
- Declare literal routes (`active`, `search`) before parameter routes, and constrain ids with `:int`.
- Never accept a tenant identifier from the client on a business endpoint.
- Adding a feature: entity → `DbSet` and configuration → repository → service → register in
  `AddErpApplicationServices` → DTOs and `ToDto()` → controller → migration → typed API service
  and page in `ERP_UI`.

Because both the API and the desktop app bind to `ERP_infrastructure`, changing a service
interface or an entity breaks both. Change them together.

Older status reports have been moved to `docs/archive/`. They describe earlier states of the
project and contradict each other; this file is the current one.
