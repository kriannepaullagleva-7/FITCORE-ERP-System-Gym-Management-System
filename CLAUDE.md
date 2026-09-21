# FitCore ERP System

Multi-tenant SaaS ERP for gyms. WinForms desktop client, ASP.NET Core 10 Web API,
EF Core 10, SQL Server, database-per-company.

Everything below describes what the code actually does. Where a feature is not built, it says
so. Nothing here is aspirational.

---

## Status at a glance

| | |
|---|---|
| Build | ✅ 0 errors, 0 warnings (`--no-incremental`, whole solution) |
| Tests | ✅ 131 passing |
| EF model vs migrations | ✅ both contexts in sync, no pending migrations on any database |
| Micro Enterprise | ✅ working end to end against the live database |
| Small Enterprise | ✅ working end to end, including Employees and Payroll |
| Medium Enterprise | ❌ tier is defined and enforced; none of its features are built |

---

## Projects

```
ERP_domain          POCO entities and the module catalogue. No dependencies.
ERP_infrastructure  DbContexts, repositories, services, tenancy. No ASP.NET Core reference.
ERP_api             Thin MVC controllers, JWT, tenant middleware.        https://localhost:7214
ERP_Tests           xUnit: unit, service (SQLite) and HTTP integration tests.
ERP_Project1        WinForms desktop client (ERP_winforms.csproj) — the only user-facing app.
```

`ERP_infrastructure` deliberately has no ASP.NET Core reference. Anything HTTP-aware lives in
`ERP_api` behind an interface.

**`ERP_winforms` references neither `ERP_infrastructure` nor EF Core.** Its only packages are
`Microsoft.Extensions.Configuration{,.Json,.Binder}`, so there is no way for a screen to open a
DbContext or see a connection string — the compiler prevents it. Every byte of business data
arrives over HTTP from the API, which owns tenancy, authorisation and the business rules.

`ERP_UI` (Blazor WebAssembly) is **retired**. The folder is still on disk but is not a member
of `ERP_Project1.slnx`, so it is not restored, built or deployed. WinForms is the UI.

### Request flow

```
ERP_winforms → HTTPS/JSON + Bearer JWT → ERP_api controller
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

## The desktop client

One setting: `ApiBaseUrl` in `ERP_Project1/appsettings.json` (override per machine with the
gitignored `appsettings.Local.json`). No connection string, no credentials.

```
Program        sign in → shell → sign out → sign in again, without restarting the process
LoginForm      the only way in; posts to /api/auth/login
ShellForm      sidebar, topbar, module routing, session-expiry handling
ModuleWorkspace  a module and its submodules on a tab strip
ModulePageBase   the frame: toolbar, stats, filters, body, status strip, loading/empty states
CrudPageBase<T>  list-and-maintain: search, grid, Add/Edit/Delete, confirmations, messages
EditDialog       field-descriptor form with client-side validation
ListDialog       read-only detail grid (sale lines, stock ledger, pay history)
UiTheme/UiKit    the design tokens and the control library
Api/             FitCoreSession + typed API services + DTOs. No WinForms reference.
```

### Navigation

The sidebar lists **modules**; each module opens a workspace whose submodules are tabs. That
keeps the sidebar to the things a gym actually does rather than the fourteen screens it takes
to do them.

```
MAIN         Dashboard
OPERATIONS   Membership   → Members · Membership Plans · Subscriptions
             Payments     → Payment Transactions
             Sales        → New Sale · Sales History · Customers
             Inventory    → Products · Stock · Suppliers
             Employees    → Employee Records          (Small and above)
             Payroll      → Payroll Records           (Small and above)
INSIGHT      Reports
ACCOUNT      Change Password · Sign Out
```

**Employees and Payroll sit inside OPERATIONS**, as peers of Membership, Payments, Sales and
Inventory. Whether they appear at all is the tier's decision; once they do, they are ordinary
day-to-day work and are not separated into a group of their own. A Micro tenant simply does
not see them, and neither does a Staff user on any tier.

### Feedback, errors and validation

- `EditDialog` validates **before** anything is sent: required fields, email format, whole
  numbers, negative amounts, minimum/maximum, field length, and per-field custom rules.
- `ApiErrorText` turns every failure into one sentence. It prefers the API's own business
  message ("This member has subscriptions, payments or sales on record…") and falls back to a
  canned one per status: 401 session expired, 403 no permission, 404 not found, 409 conflict,
  5xx server problem, plus unreachable and timeout. Anything that looks like a connection
  string, a token, a stack trace or an HTML error page is refused and replaced.
- Confirmations name the record **and** the consequence — never a bare "Are you sure?".
- Success is confirmed on the page's status strip; a modal is reserved for what the operator
  cannot see, such as a completed sale.
- A 401 anywhere raises `FitCoreSession.SessionExpired`, and the shell returns the operator to
  the sign-in window rather than letting every screen fail in turn.

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
| Expenses | — | — | ⚠️ fully built, tier-gated off |
| Finance Management | — | — | ❌ not built (permission key only) |
| Business Intelligence | — | — | ❌ not built (permission key only) |
| User Access | — | — | ⚠️ fully built, tier-gated off |
| System Administration | — | — | ⚠️ fully built, tier-gated off |

Three of those five are complete stacks — entity, repository, service, controller, DTOs, typed
client and page — that are simply unreachable because no Medium tenant is enabled. Only Finance
Management and Business Intelligence are genuinely unbuilt: they exist as keys in
`ErpModule.cs` and in two test files, and nowhere else.

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

**Either the username or the email signs in** — `UserAuthenticationService` matches on both,
which is why the field is labelled "Username or email".

| Username | Email | Name | Role | Tenant |
|---|---|---|---|---|
| `admin` | kris.santos@fitcore.local | Kris Santos | Admin / Owner | B (Small) |
| `manager` | john.doe@fitcore.local | John Doe | Manager | B (Small) |
| `staff` | maria.santos@fitcore.local | Maria Santos | Receptionist / Staff | B (Small) |
| `micro.admin` | elena.reyes@fitcore.local | Elena Reyes | Admin / Owner | A (Micro) |
| `micro.manager` | paolo.cruz@fitcore.local | Paolo Cruz | Manager | A (Micro) |
| `micro.staff` | ana.lim@fitcore.local | Ana Lim | Receptionist / Staff | A (Micro) |

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

### The fallback is bound to one company

`FallbackCompanyId` names the single company `ConnectionStrings:TenantErp` belongs to. Zero —
the default — means it is bound to nobody and therefore never applies, whatever
`AllowConnectionStringFallback` is set to.

This exists because an *unbound* fallback is what pooled every tenant into one database. The
`CompanyDatabases` rows were wrong — rows 1 and 2 pointed at a dead server with an empty
`CredentialKey`, and every row belonged to `CompanyId 1` while the application served company 3
— so every company's registry lookup threw, and the fallback answered all of them with the same
connection string: `db68433`, the Micro tenant. Small-tier work was written into the Micro
tenant's database, which is where a stray Employee and a Paid payroll run were later found on a
company whose tier has neither module.

The registry was repaired by `sql/FixTenantRegistry.sql` and the fallback disabled. Binding it
to a company closes the shape of the bug rather than the instance: a second company hitting the
same lookup failure now gets a 503, not somebody else's data. Two tests cover it — one that the
bound company is still rescued, one that a different company is refused.

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
| Customers / Suppliers | `GET`/`POST /api/customers`, `/api/suppliers`, `GET`/`PUT`/`DELETE /{id}`. Customers is gated on Sales, Suppliers on Inventory, so both are reachable on every tier. |
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
    "FallbackCompanyId": 0,                // the fallback belongs to this one company only
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

### Solution layout

The solution is `ERP_Project1.slnx` (XML format, not `.sln`). Five projects:

| Project file | Role |
|---|---|
| `ERP_Project1/ERP_winforms.csproj` | WinForms desktop client — **the only UI**, listed first |
| `ERP_api/ERP_api.csproj` | Web API — **EF startup project** |
| `ERP_infrastructure/ERP_infrastructure.csproj` | DbContexts, repositories, services — **EF migrations project** |
| `ERP_domain/ERP_domain.csproj` | Entities and the module catalogue |
| `ERP_Tests/ERP_Tests.csproj` | xUnit |

**The folder and the assembly disagree on the first one.** The WinForms project lives in the
`ERP_Project1` folder but is named `ERP_winforms.csproj`. `ERP_winforms\ERP_winforms.csproj`
does not exist and never has.

`ERP_winforms` is listed first because it is the product. That does mean the Package Manager
Console defaults to it, and EF will answer *"No DbContext was found in assembly
'ERP_winforms'"* — correctly, because it holds none. **Always pass `--project
ERP_infrastructure --startup-project ERP_api` to every `dotnet ef` command**, as the examples
below do; then the default selection does not matter.

`ERP_Project1.slnLaunch` defines three Visual Studio launch profiles: **FitCore Desktop
(API + WinForms)** for the normal flow, **FitCore WinForms only**, and **FitCore API only**.
Pick one from the Start button's dropdown.

### From the terminal

`fitcore.ps1` at the repo root wraps the whole workflow, and every build path stops the
running applications first so the assembly-lock failure below cannot happen:

```powershell
.\fitcore.ps1 stop      # stop FitCore processes, verify nothing holds the build output
.\fitcore.ps1 build     # stop, clean, restore, build
.\fitcore.ps1 rebuild   # as build, but removes bin/obj first
.\fitcore.ps1 db        # migration state for master and both tenants
.\fitcore.ps1 update    # apply pending migrations to all three databases
.\fitcore.ps1 api       # start ERP_api and wait for the port
.\fitcore.ps1 ui        # start ERP_winforms
.\fitcore.ps1 run       # build, start the API, wait, start the desktop client
.\fitcore.ps1 test      # dotnet test
```

It only ever stops processes whose executable lives under this repository, so an unrelated
`dotnet` tool is left alone.

By hand, the same thing is:

```powershell
dotnet restore
dotnet build ERP_Project1.slnx
dotnet test ERP_Tests\ERP_Tests.csproj

# 1. the API first - the desktop client cannot sign in without it
dotnet run --project ERP_api\ERP_api.csproj --launch-profile https    # https://localhost:7214

# 2. then the desktop client, in a second terminal
dotnet run --project ERP_Project1\ERP_winforms.csproj
```

The solution root is not itself a project, so a bare `dotnet run` there fails with
*"Couldn't find a project to run"*. That is expected; always pass `--project`.

The desktop client will not start before the API: it shows
*"Unable to connect to the FitCore server. Please make sure the server is running and try
again."* on the sign-in screen, which is the intended behaviour rather than a crash.

A running `ERP_winforms.exe` or `ERP_api` holds a lock on its own output assembly, so
**stop them before rebuilding** or MSBuild fails with `MSB3021 … being used by another
process`.

The databases are remote (`*.public.databaseasp.net`). There is no local SQL Server.

### Migrations

Run them with the infrastructure project as the target and the API as the startup project:

```powershell
dotnet ef database update --context MasterErpDbContext ^
    --project ERP_infrastructure --startup-project ERP_api

dotnet ef database update --context TenantErpDbContext ^
    --project ERP_infrastructure --startup-project ERP_api
```

In the Package Manager Console the equivalent is:

```powershell
Update-Database -Context MasterErpDbContext -Project ERP_infrastructure -StartupProject ERP_api
Update-Database -Context TenantErpDbContext -Project ERP_infrastructure -StartupProject ERP_api
```

Passing both switches explicitly means the Default project dropdown does not matter. Never run
migrations against `ERP_winforms`.

**There is one tenant schema but three tenant databases**, so "update the tenant database" is
ambiguous. Left alone it targets `TenantErp`, which is **tenant_a**. Name a different one with
`TENANT_ERP_CONNECTION_NAME`:

```powershell
$env:TENANT_ERP_CONNECTION_NAME = "TenantErpB"   # tenant_b, db68484
dotnet ef database update --context TenantErpDbContext `
    --project ERP_infrastructure --startup-project ERP_api
Remove-Item Env:\TENANT_ERP_CONNECTION_NAME
```

Every design-time command prints the database it resolved before it does anything:

```
[EF design-time] target -> tcp:db68484.public.databaseasp.net,1433 / db68484  (from …\ERP_api)
```

Read that line. A migration applied to the wrong tenant is the expensive mistake here.

`TENANT_ERP_CONNECTION` / `MASTER_ERP_CONNECTION` still override with a full connection string.

The design-time factories layer `appsettings.Development.json` and
`appsettings.MonsterASP.json` over `appsettings.json`, exactly as the applications do. Without
that layering the tools read the committed password-less entry, dropped to Named Pipes and
failed with *"the server was not found"* against a server that was up.

**Always `dotnet build` before `dotnet ef ... --no-build`.** A stale assembly makes EF report
pending model changes that do not exist, and makes it run against the wrong schema.

Both contexts are in sync with their snapshots.

---

## Testing

131 tests. `dotnet test ERP_Tests/ERP_Tests.csproj`

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
  containing connection-string syntax survives intact. Also that the fallback is refused for a
  company it is not bound to, and that an unbound fallback never applies at all.
- **Audit trail** — attribution, generated keys, changed-properties-only updates, that the trail
  does not record itself, and that no password hash ever reaches it.
- **Service rules** — member delete guards, duplicate product codes, stock arithmetic, sale
  transactions, payroll calculation and overlapping periods. Run against real SQLite.

There are no *automated* desktop-UI tests in the suite. Two things were done by hand instead,
and both are reproducible.

**The desktop client's own API layer, driven headlessly.** `ERP_Project1/Api` has no WinForms
reference, so it compiles into a plain console host and can be run against a live API. Doing
that exercises the exact `FitCoreSession`, typed services and DTOs the screens use: 91
assertions per tenant covering CRUD on every entity, the sale/stock/rollback path, the payroll
overlap and paid-run locks, the member delete guard, and the wording of 400/403/404/offline
failures. Run for `admin`, `manager`, `staff`, `micro.admin`, `micro.manager` and
`micro.staff`. That harness is not checked in; it is a `.csproj` with
`<Compile Include="…/ERP_Project1/Api/*.cs" />` and one `Main`.

**The application itself, driven through Win32.** Sign-in through the real form, every module
and tab opened against the live tenant databases, a member created / edited / deleted through
the dialogs with the writes verified in `db68484`, the required-field and email validation
refusing to submit, the delete confirmation and its consequence text, the empty and
no-matches states, sign out and sign in again as a different user without restarting.

Micro was checked for the opposite property: no Employees or Payroll anywhere in the sidebar,
no employee or payroll cards on the dashboard, and no "Add employee" quick action. Staff on
the Small tenant additionally loses Reports, so the INSIGHT group disappears entirely.

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
5. ~~`/api/customers` and `/api/suppliers` have no UI page.~~ **Done.** Both now have a page
   under Operations with list, search, status filter, create, edit, delete, validation,
   pagination and empty states. The `PUT` endpoints and the duplicate-code guards they need
   were added at the same time — the controllers previously offered no update at all. ✅
6. **`ERP_Project1/Api` duplicates `ERP_api/DTOs`**, deliberately, so the desktop client keeps
   zero project references and cannot reach EF Core. A shared contracts project would remove
   the duplication at the cost of that guarantee. ⚠️
7. ~~The desktop app has no tenant concept.~~ **Done.** `ERP_winforms` is now a pure API
   consumer: it signs in, carries a bearer token, and the company travels inside that token,
   so the desktop cannot choose or influence its tenant. It holds no connection string and
   references neither EF Core nor `ERP_infrastructure`. ✅
7b. **`ERP_UI` (Blazor) is retired but not deleted.** It is out of the solution and is not
   built; the folder remains only so the work is not lost. Delete it when you are sure. ⚠️
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

**The desktop says "Unable to connect to the FitCore server"** — the API is not running, or
`ApiBaseUrl` in `ERP_Project1/appsettings.json` does not match the port it is listening on.
The sign-in screen prints the address it is trying underneath the button. CORS is not
involved: a desktop client is not a browser.

**"Your session has expired. Please sign in again."** — the token lapsed (eight hours by
default) or the API restarted with a different signing key. The shell returns to sign-in.

**MSB3021 / MSB3027 "being used by another process" when building** — `ERP_winforms.exe` or
the API is still running and holding its own assembly. Stop them and build again.

---

## Development guidelines

- Controllers stay thin: no EF Core, no business rules. Controller → service → repository.
- Return DTOs, not entities. `ApiMappings` holds the `ToDto()` projections.
- Declare literal routes (`active`, `search`) before parameter routes, and constrain ids with `:int`.
- Never accept a tenant identifier from the client on a business endpoint.
- Adding a feature: entity → `DbSet` and configuration → repository → service → register in
  `AddErpApplicationServices` → DTOs and `ToDto()` → controller → migration → DTO and typed
  service in `ERP_Project1/Api` → page deriving from `CrudPageBase<T>` → a `WorkspaceTab` in
  `ShellForm.Catalogue()`.
- Desktop screens never call `MessageBox` for a server failure: return the message from the
  `EditDialog` save callback, or pass it to `ShowError`, so it lands in the right place.
- New numeric fields get a `Minimum`; new text fields get a `MaxLength` matching the column.

`ERP_winforms` does **not** reference `ERP_infrastructure`, so a change to a service interface
or an entity cannot break it directly — only a change to the wire contract can. Keep
`ERP_Project1/Api/*Dtos.cs` in step with `ERP_api/DTOs`.

Older status reports have been moved to `docs/archive/`. They describe earlier states of the
project and contradict each other; this file is the current one.
