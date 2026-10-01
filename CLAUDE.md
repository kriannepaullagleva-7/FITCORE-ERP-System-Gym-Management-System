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
| Tests | ✅ 297 passing |
| EF model vs migrations | ✅ both contexts in sync, no pending migrations on any database |
| Micro Enterprise | ✅ Membership and Payments, working end to end against the live database |
| Small Enterprise | ✅ working end to end, including Employees, Payroll and Finance |
| Medium Enterprise | ✅ working end to end, including System Administration and a three-branch network |
| Super Admin | ✅ platform panel working, account and data in the master database |
| Authorization sweep | ✅ 706 checks — 19 accounts × every module, every answer deliberate |
| Business flow | ✅ 120 steps on the live Medium tenant, books balance afterwards |

The last two are `verify-access.ps1` and `verify-flows.ps1` at the repo root. Run the API first, then
either script. See [Verification](#verification).

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
ConfirmDialog    the styled yes/no prompt behind every confirmation and delete
UiTheme/UiKit    the design tokens and the control library
Api/             FitCoreSession + typed API services + DTOs. No WinForms reference.
Receipts         one receipt model + ReceiptDialog, shared by sales, payments,
                 memberships and payslips — print, copy and preview live there once
Pages/           one folder per module, named for the module it serves:
                 Membership · Payments · Sales · Inventory · Employees · Payroll ·
                 Finance · SystemAdmin · BusinessIntelligence · Platform
```

Every page lives under the folder of the module whose tab opens it, so "where is the Valuation
screen?" is answered by the module it appears in rather than by searching. The two screens that
serve several modules — `ReportsPage` and `AnalyticsPage` — sit under `BusinessIntelligence`,
which is the module that owns the generic versions of both.

### Navigation

The sidebar lists **modules**; each module opens a workspace whose submodules are tabs. That
keeps the sidebar to the nine things a gym does rather than the sixty screens it takes to do
them.

```
OPERATIONS   Membership Management  → Members · Plans · Subscriptions · History · Reports
             Payment Management     → Transactions · Outstanding · Reconciliation · Reports
             Sales Management       → New Sale · Sales History · Returns · Reports
                                                                             (Small and above)
             Inventory Management   → Products · Stock · Purchases · Suppliers ·
                                      Stock Movements · Valuation · Reports  (Small and above)
             Employee Management    → Records · Attendance · Leave · Reports (Small and above)
             Payroll Management     → Payroll Records · Attendance Summary · Calculation ·
                                      Payslips · History · Reports           (Small and above)
FINANCE      Finance Management     → Overview · Expenses · Chart of Accounts · Journal ·
                                      Ledger · Receivable · Payable · Cash & Bank ·
                                      Reconciliation · Budgets · Periods ·
                                      Financial Reports                      (Small and above)
SYSTEM       System Administration  → Users · Roles · Permissions · Branches · Settings ·
                                      Audit Logs · Security · Data Integrity (Medium)
INSIGHT      Business Intelligence  → Dashboard · Reports · KPIs · the seven analytics areas ·
                                      Branch Performance                     (Small and above)
ACCOUNT      Change Password · Sign Out
```

The **Super Admin sees a different sidebar entirely** — four entries, none of them operational.
See [The Super Admin panel](#the-super-admin-panel).

On a Medium tenant the topbar carries a **branch picker**, drawn only for an Admin/Owner whose
account is not itself bound to a branch. Switching discards every open workspace and rebuilds
it: each screen caches the rows it last fetched, and those rows belong to the branch that was
selected when they were read.

**Every submodule in the catalogue has a tab, and every tab names a catalogue submodule.** The
two lists are checked against each other rather than maintained separately — a submodule
declared with nowhere to open it is a promise the product does not keep, and a tab guarded on a
key that no longer exists would be drawn for nobody.

**The module reports are one screen, not seven.** `ReportsPage` takes the kinds it should offer:
Business Intelligence's own Reports tab gets all six behind a selector, and each operational
module's Reports tab pins it to its own kind and hides the selector. Six near-identical report
forms would have drifted apart on the first change to the date filter. `SettingsPage` is
parameterised the same way, so Platform Settings is the tenant settings screen pointed at the
installation-wide store.

**There are exactly nine modules and there will not be a tenth.** Attendance, purchasing,
returns, leave, expenses, the general ledger, AR/AP, budgets, banking, users, roles, settings,
the audit trail, analytics and platform administration are all **submodules** — they appear as
tabs inside one of the nine, never as a sidebar entry of their own. A flat list of everything
this system does would be a list of sixty items, which is a menu nobody reads.

Which tabs appear is the server's decision, not the client's. `ShellForm.Tabs(...)` filters on
the submodule list that came back from `/api/auth/me`, so a tab the caller may not use is never
drawn — and hiding it is a courtesy, because the endpoint behind it refuses independently.

A platform administrator gets a different catalogue altogether - `PlatformCatalogue()` rather
than `TenantCatalogue()` - because the Super Admin does not run anybody's gym. See
[The Super Admin panel](#the-super-admin-panel).

### Feedback, errors and validation

- `EditDialog` validates **before** anything is sent: required fields, email format, whole
  numbers, negative amounts, minimum/maximum, field length, and per-field custom rules.
- **Every failing field is reported at once, under the field it belongs to.** `ValidateFields`
  walks the whole form rather than stopping at the first problem, and each message is shown as a
  small red line beneath its own control; the banner at the top of the dialog carries only the
  count ("3 fields need attention"), and is omitted entirely when a single field is at fault,
  since repeating one sentence twice on one screen says nothing the inline line did not. Focus
  still lands on the first offending field. Server-side failures stay in the banner: a message
  from the API names a DTO property, and `FieldSpec` keys do not follow from those names
  reliably enough to route one to a field without risking misattributing it.
- **A required field's asterisk is drawn in the danger colour**, so "required" reads at a glance.
- **Numeric, money and integer fields refuse a keystroke that cannot belong to a number**
  (`UiKit.AttachNumericFilter`, shared with the till's own hand-built amount boxes), reading the
  decimal separator and negative sign from the current culture rather than assuming them. This is
  never the only defence — the same text is still parsed and range-checked exactly as before.
  Money fields additionally seed at two decimals and carry a ₱ beside the box, which is a caption
  only: what is typed and what is sent are unchanged.
- **Closing a dialog with unsaved typing asks first.** `EditDialog` snapshots every field's
  starting value and compares on close - Cancel, Escape and the window's own X all route through
  the one check - offering Stay or Discard changes. An untouched dialog closes silently, and a
  save still in flight refuses to close at all.
- `ApiErrorText` turns every failure into one sentence. It prefers the API's own business
  message ("This member has subscriptions, payments or sales on record…") and falls back to a
  canned one per status: 401 session expired, 403 no permission, 404 not found, 409 conflict,
  5xx server problem, plus unreachable and timeout. Anything that looks like a connection
  string, a token, a stack trace or an HTML error page is refused and replaced.
- Confirmations name the record **and** the consequence — never a bare "Are you sure?" — and are
  drawn by `ConfirmDialog` rather than a native message box, so the buttons say what they do
  ("Delete member" / "Cancel", "Discard changes" / "Stay") instead of Yes and No, and the
  destructive one is red. The safe button keeps the focus and is what Enter and Escape both
  activate, which is the property the `MessageBoxDefaultButton.Button2` it replaces always had.
  `UiKit.Confirm`/`ConfirmDelete` take the label as an optional argument, so a screen that wants
  wording of its own passes it and every other call site keeps the default.
- **Three states, not one.** A list that is loading shows the busy overlay, one that came back
  empty shows an invitation, and one whose **initial load failed** shows "Unable to load {noun}s"
  with the server's own sentence and a Retry button (`ShowErrorState`). A failed Edit or Delete
  on a page that already holds good rows deliberately keeps the status-strip banner instead —
  blanking a grid that still has valid data in it would be a regression, not a clarification.
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
| Membership Management | ✅ | ✅ | ✅ |
| Payment Management | ✅ | ✅ | ✅ |
| Sales Management | — | ✅ | ✅ |
| Inventory Management | — | ✅ | ✅ |
| Employee Management | — | ✅ | ✅ |
| Payroll Management | — | ✅ | ✅ |
| Finance Management | — | ✅ | ✅ |
| System Administration | — | — | ✅ |
| Business Intelligence | — | ✅ | ✅ |

**The tier gates modules; the submodule gates the feature.** Business Intelligence is on Small
and Medium, but only some of what is inside it is: a Small owner gets the dashboard and the
reports, while the KPI set and every analytics area are Medium. That is why the row reads ✅ on
Small without a single-site gym acquiring a cross-module analytics suite.

**A Micro tenant has no Business Intelligence module and is still not left without reports.**
Each module report is guarded by the module it reports on rather than by Business Intelligence,
so `/api/reports/membership` and `/api/reports/payments` come with the two modules Micro holds.
That is the property that made moving Business Intelligence up to Small safe, and it is asserted
in both directions by `ApiAuthorizationTests`.

A submodule is allowed when three things hold at once — the caller holds the parent module, the
company's tier reaches the submodule's minimum, and the caller's role is senior enough:

```csharp
public static bool IsSubmoduleAllowed(
    string? submoduleKey, EnterpriseTier tier, int roleLevel, Func<string, bool> holdsModule)
{
    var definition = FindSubmodule(submoduleKey);
    if (definition is null) return false;
    if (definition.MinimumTier > tier) return false;
    if (roleLevel > definition.MinimumRoleLevel) return false;
    return holdsModule(definition.ModuleKey);
}
```

Submodules are **derived, never granted**. They are not stored in a table and not carried in the
token — the token holds the nine module claims and the tier, and the submodule list is computed
from them. So a subfeature can never be held without its module, can never exceed the company's
plan, and adding one needs no migration and no permission backfill.

**The tier is a hard ceiling.** `PermissionResolver` seeds its result from the tier's module
list, so a permission row granting a module the company's tier does not include can never take
effect — not for an override, not for an administrator, not for a Super Admin. This is covered
by tests, including one that grants a Medium module to a Super Admin on a Micro tenant and
asserts they still get nothing.

### Consequences worth knowing

**System Administration is Medium alone**, so Micro and Small tenants have no in-app user
management, no settings screen, no audit trail and no branch network. Everything they need is
provisioned for them by the `Bootstrap` configuration section at start-up, which is idempotent
and additive. Self-service password change is *not* module-gated and keeps working for everyone.

**Micro is the front desk alone.** A Micro gym takes memberships and takes money; it has no till
and no stockroom, so Sales and Inventory are Small. A tier that offered a point of sale to a
company with no products would be offering a screen that can only report being empty.

**Finance starts at Small**, so a single-site gym gets double-entry books, statements and the
expense ledger. Payment reconciliation moved down with it, because reconciliation compares the
takings against the ledger and Small is where the ledger begins.

If any of this proves impractical, it is one line per module or submodule in `ErpModule.cs`.
Nothing else needs to change — the sidebar, the tab strip and the API filter all read the
catalogue.

### The tenants

| Tier | Company | Code | Id | Database | Credential key | Accounts |
|---|---|---|:--:|---|---|:--:|
| Micro | FitCore Gym — Micro Enterprise | `COMP001` | 3 | `db68433` | `TenantA` | 3 |
| Small | FitCore Gym — Small Enterprise | `COMP002` | 4 | `db68484` | `TenantB` | 7 |
| Medium | FitCore Gym — Medium Enterprise | `COMP003` | 7 | `db68521` | `TenantC` | 14 active, 26 total |
| — | FitCore Platform | `FITCORE` | 8 | `db68434` (**master**) | `MasterErp` | 1 |

**There are three tenants and there were once six.** `COMP001` (an empty "reserved" company
whose registry rows pointed at a dead server, `db66559`) and a second `COMP01`/`COMP02` pair that
duplicated the Micro and Small registrations were the residue of the tenant-registry bug
described under [The fallback is bound to one company](#the-fallback-is-bound-to-one-company).
All three held **no accounts at all**, and were deleted from the master database along with their
`CompanyDatabases` and `Devices` rows; the survivors were then renumbered `COMP001`/`COMP002`/
`COMP003` so the codes ascend with the tier. `Bootstrap:Tenants` in `ERP_api/appsettings.json`
carries the same three codes — that is what stops the next start-up registering the deleted ones
again, since `MasterBootstrapper` matches a company by its code and creates one when it finds no
match.

The tenant *schema* is shared: one `TenantErpDbContext`, one migration chain, applied to each
database. Isolation is physical — Tenant A's data is in `db68433`, Tenant C's in `db68521`, and
no request can cross because the company comes from the signed token.

**The platform company is not a gym and has no business database.** It is registered against the
master database because that is where the Super Admin's own data already lives: the companies,
the subscription plans, the platform accounts and the platform audit trail. There is no FitCore
business database and there should not be one — when the Super Admin needs to look inside a
tenant they name it explicitly and the ordinary tenant-resolution path serves them that tenant's
own database. No tenant's operational records are copied into the master.

Its tier is Medium because the tier ceiling applies to the Super Admin exactly as to everybody
else, and on a smaller plan their own administration screens would be withheld from them — a
correct rule producing a nonsensical result.

### Bringing a company back

`MasterBootstrapper` activation is **one-way**: configuration may activate a company that is
registered but inactive, and may never deactivate one.

Without that, flipping `Enabled: true` for an existing company did nothing — the row had been
written as inactive on first run and was never revisited, so a real account got
*403 "company 7 is inactive"* from a configuration that plainly said otherwise. Refusing to
deactivate in the same breath is deliberate: a stale flag in a config file must not lock a paying
tenant out of their own data.

---

## Roles

| Role | Level | Default modules (before the tier ceiling) |
|---|:--:|---|
| Super Admin | 0 | everything, plus the platform subfeatures nobody else can reach |
| Admin / Owner | 1 | all nine |
| Manager | 2 | all nine, minus the Admin-only subfeatures inside them |
| Receptionist / Staff | 3 | Membership, Payments, Sales, Inventory - no part of Business Intelligence, dashboard included |

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
| `micro.admin` | elena.reyes@fitcore.local | Elena Reyes | Admin / Owner | A (Micro) |
| `micro.manager` | paolo.cruz@fitcore.local | Paolo Cruz | Manager | A (Micro) |
| `micro.staff` | ana.lim@fitcore.local | Ana Lim | Receptionist / Staff | A (Micro) |
| `admin` | kris.santos@fitcore.local | Kris Santos | Admin / Owner | B (Small) |
| `manager` | john.doe@fitcore.local | John Doe | Manager | B (Small) |
| `staff` | maria.santos@fitcore.local | Maria Santos | Receptionist / Staff | B (Small) |
| `medium.admin` | rafael.ocampo@fitcore.local | Rafael Ocampo | Admin / Owner | C (Medium) |
| `medium.manager` | grace.bautista@fitcore.local | Grace Bautista | Manager | C (Medium) |
| `medium.staff` | miguel.torres@fitcore.local | Miguel Torres | Receptionist / Staff | C (Medium) |
| `superadmin` | platform@fitcore.local | FitCore Platform Administrator | Super Admin | the platform |

**The branch accounts on Tenant C.** Each branch gets one Manager and two Staff, and each of
those accounts is *bound* to its branch — `AppUser.BranchId` is set, sign-in turns it into a
signed `branch_id` claim, and the branch filter narrows every query they make. None of them can
read a sibling branch, and none of them can switch. `medium.admin` is deliberately unbound: an
Admin/Owner has no branch and chooses which one to look at.

| Username | Name | Role | Branch |
|---|---|---|---|
| `bra.manager` | Celine Abad | Manager | Branch A — Downtown |
| `bra.staff1` | Noel Villar | Receptionist / Staff | Branch A — Downtown |
| `bra.staff2` | Rhea Domingo | Receptionist / Staff | Branch A — Downtown |
| `brb.manager` | Ivan Mercado | Manager | Branch B — Lanang |
| `brb.staff1` | Dina Salcedo | Receptionist / Staff | Branch B — Lanang |
| `brb.staff2` | Marco Tolentino | Receptionist / Staff | Branch B — Lanang |
| `brc.manager` | Liza Fuentes | Manager | Branch C — Matina |
| `brc.staff1` | Ryan Espino | Receptionist / Staff | Branch C — Matina |
| `brc.staff2` | Kaye Bautista | Receptionist / Staff | Branch C — Matina |

Each also gets an `Employee` row inside Tenant C, coded `<BRANCH>-<USERNAME>`, so a branch is
populated rather than merely declared. The `Employee` rows are tenant data and stay in the
tenant database; only the sign-in accounts are master-side.

### The Super Admin

A dedicated account on a dedicated company, and **not** an Admin, a Manager, a Receptionist or
any tenant's employee. Level 0, which no tenant role can reach and no permission grant can
confer.

It is **not a tenth module.** The platform panel is a set of submodules inside System
Administration (`systemadmin.platform.*` — tenants, plans, subscriptions, platform users,
platform settings, platform audit) and Business Intelligence
(`businessintelligence.platform.*` — the platform dashboard and cross-tenant analytics). Their
minimum role level is 0, so they are invisible and unreachable for everybody else on every tier,
and the nine-module structure is unchanged.

### The Super Admin panel

`ShellForm.PlatformCatalogue()` gives the platform account its own four-entry sidebar:

```
PLATFORM   Dashboard      → the platform dashboard
           Subscription   → Plans · Subscriptions & Tiers
SYSTEM     Administration → Tenants · Platform Users · Platform Settings · Platform Audit · Monitoring
INSIGHT    Intelligence   → Platform Analytics
```

These are **not four new modules**. Every entry names one of the nine in its `RequiredModule`
and opens onto submodules that already exist; what changes is only how they are grouped for an
account whose workspace is the platform rather than a gym. The dashboard and the subscription
book are the two things a platform operator opens most, so they get their own entries instead
of being buried as the fifth tab of something else.

The seven operational modules are absent because the platform company is not a gym: it has no
members, no stock and no till. **Branch administration is absent for a different reason** — a
branch belongs to a tenant's own company, so creating and switching branches stays with that
tenant's Admin/Owner and is never the Super Admin's to do.

**The platform company does not appear in its own tenant list.** `GetTenantsAsync` excludes any
company holding a Super Admin account, so the Tenants screen and every subscriber figure on the
panel count the three gyms and not the operator looking at them. It is identified by the account
it holds rather than by its code, so a second platform company would be excluded on the same
grounds instead of needing its name added to a list. The row stays in the master database either
way — it is where the Super Admin's own account lives, and deleting it would end their ability to
sign in at all.

**A toolbar action that needs a row is disabled until there is one** (`CrudPageBase.AddRowAction`),
and the one button whose wording follows its row — Activate/Deactivate — says which of the two it
is about to do. Labelling it "Activate" while it is about to deactivate a live company is how an
operator locks a paying tenant's users out believing they were turning them on.

Their account and all their data live in the **master** database. See
[The tenants](#the-tenants) for why, and for how they read a tenant without anything being
copied.

### Subscription plans

What FitCore sells is three rows in the master `SubscriptionPlans` table, one per tier:

| Plan | Tier | Modules | Monthly | Annual |
|---|:--:|:--:|--:|--:|
| Micro Gym | Micro | 2 | ₱399 | ₱3,990 |
| Small Gym | Small | 8 | ₱1,499 | ₱14,990 |
| Medium Gym | Medium | 9 | ₱2,999 | ₱29,990 |

Each row stores `PlanCode`, `PlanName`, `Tier`, `MonthlyPrice`, `AnnualPrice`, `MaxUsers`,
`Description` and `Capability` (the one-sentence "what this buys you" under the tier name) —
`PlatformAdminService.EnsureStandardPlansAsync` seeds them at start-up and never overwrites an
existing row, the same one-way pattern `MasterBootstrapper` uses for accounts.

**A plan's module list and annual savings are never stored — they are computed on every read**,
from `Tier` against `ErpModules.All`, by the same comparison `IsSubmoduleAllowed` uses. A plan
row cannot advertise a module its tier would not actually grant, because there is no column to
drift: `SubscriptionPlanView.Modules` and `.AnnualSavings` are calculated in
`PlatformAdminService.ToView` and re-derived on every request, not written at seed time and read
back stale.

Subscribing a tenant to a plan (`POST /api/platform/subscriptions`) is what sets
`Company.EnterpriseTier = plan.Tier` — see [Enterprise tiers](#enterprise-tiers). Managing the
price list itself is a separate concern with its own submodule,
`systemadmin.platform.plans` (Medium, Super Admin only), distinct from
`systemadmin.platform.subscriptions` which governs a company's billing term on a plan. The
desktop reflects the split as two tabs: **Plans** (`SubscriptionPlansPage`) — view, create, edit,
activate/deactivate and view the modules a plan includes — and **Subscriptions & Tiers**
(`PlatformSubscriptionsPage`) — start, renew and cancel a tenant's term. A plan is never
deleted, only deactivated, so a tenant already on it keeps what they are paying for and it
simply drops off the list a new subscription can pick from.

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

## Branching

A Medium company can run more than one site. `Branch` is a **tenant** entity — it is the
company's own operational data, and the master database holds the registry of companies, not
the inside of any of them. Micro and Small tenants have no `Branch` rows at all.

```
FitCore Gym — Medium Enterprise  (COMP003, db68521)
├── BRA  Branch A — Downtown   (primary)
├── BRB  Branch B — Lanang
└── BRC  Branch C — Matina
```

### How a row belongs to a branch

Thirteen entities implement `IBranchScoped` and carry a nullable `BranchId`:

```
Member · Customer · Subscription · Payment · Sale · SaleReturn · Employee
Attendance · LeaveRequest · Payroll · Expense · Purchase · StockMovement
```

Null means "belongs to the company rather than to any one branch", which is what every row on a
single-site tenant is.

**Products, stock, suppliers, membership plans and the general ledger are deliberately
company-wide.** A branch sells from one catalogue at one set of prices, and the books are the
company's. `StockMovement` *is* branch-scoped, so the ledger of every in and out still records
which site moved the stock.

### Enforcement is in the DbContext, not in the services

`TenantErpDbContext.OnModelCreating` sweeps the model, finds every entity implementing
`IBranchScoped`, and gives each one a global query filter:

```csharp
builder.Entity<TEntity>()
    .HasQueryFilter(e => CurrentBranchId == null || e.BranchId == CurrentBranchId);
```

This is the whole of branch isolation, and it is deliberately not in any service. Every query in
the application goes through this context, so a filter here is the only kind that cannot be
forgotten by the next endpoint somebody writes. No repository, service or controller mentions
branches; none of them can opt out by accident.

The filter closes over `CurrentBranchId` on the **context instance** rather than a captured
constant, so EF compiles it to a parameter read per query. That matters because the options —
and with them the model — are cached per connection string for the life of the process: one
cached model serves a request scoped to Branch A and the next one scoped to Branch B.

A `SaveChanges` sweep does the other half, stamping the branch onto rows being inserted. A
scoped caller writes to their own branch; an unscoped caller on a company that *has* branches
falls to the primary one, because an owner looking at the whole company is not a reason to
write a record that belongs to no branch and therefore appears in no branch's books.

**Deliberately, a scoped caller does not see unassigned rows.** Lending them to whichever branch
happened to ask would double-count them in every branch comparison, which is the one screen the
feature exists to make trustworthy.

### How the branch is decided

`BranchResolutionMiddleware` runs after tenant resolution:

1. **The `branch_id` claim on the token.** Minted at sign-in from `AppUser.BranchId`. An account
   bound to a branch is narrowed to it, full stop — the header is not consulted, so a branch
   manager cannot widen their own view by sending one, and cannot read a sibling by naming it.
2. **The `X-Branch-Id` header**, honoured only for an Admin/Owner of a Medium tenant whose own
   account is not bound to a branch. This is the branch picker in the desktop topbar. The value
   is checked against the tenant's own open branches, so a header naming a branch that does not
   exist is refused with **400** rather than silently showing nothing.
3. **Nothing**, which means the whole company — every branch and the records that belong to
   none. This is what a single-site tenant always gets.

The header validation is the only database read the middleware ever does, and it happens solely
on a request that asked for a branch. A request that sends no header pays nothing and never
causes the tenant DbContext to be constructed.

### Who administers branches

`systemadmin.branches` — Medium, Admin/Owner. A Manager runs a branch; they do not decide that
it exists, and their account is bound to one anyway. `BranchesController` carries both
`[RequireModule(SystemAdmin)]` and `[RequireSubmodule(Branches)]`, so hiding the tab is a
courtesy and these two are the boundary.

`BranchService` is the one place that deliberately reads across branches, so almost every query
in it carries `IgnoreQueryFilters`. That is not a loophole: branch administration is Admin-only,
and an Admin account is never bound to a branch in the first place.

Rules worth knowing:

- **A branch that has traded cannot be deleted.** Same reason a member who has paid cannot be:
  the branch is what explains which till took the money, and a sale whose branch has been
  deleted is a sale that belongs to nobody. Closing it is the supported alternative and keeps
  every figure intact.
- **The primary branch cannot be deleted or closed.** It is where an unassigned record lands, so
  another branch has to be made primary first.
- **Transfers move people, not transactions.** Members, employees and walk-in customers can be
  moved between branches — that is a real thing that happens. A sale, a payment, a pay run and
  an expense say what happened at one site on one day; moving one would rewrite two branches'
  revenue after the fact and leave the company-wide ledger agreeing with neither.

### Bringing an existing company into branches

`TenantBranchBootstrapper` creates the configured branches and their staff at start-up, and on
the transition from *no branches* to *some* it assigns every existing unassigned row to the
primary branch. Without that, a company's whole history would vanish the moment an Admin
selected a branch, and would count towards no branch at all. It runs only on that transition, so
it can never reassign a record twice, and it uses `ExecuteUpdate` — one UPDATE per table rather
than loading every member, sale and payment of a live tenant into memory.

This is the one bootstrap step that writes to both databases, and it keeps the same split as the
rest of the system: the `Branch` and `Employee` rows go into the tenant database; only the
`AppUser` sign-in accounts are master-side, because an account is what *selects* a tenant and so
has to be readable before any tenant database is opened. `AppUser.BranchId` holds an identifier
and nothing else — no branch name, address or history is copied into the master database.

### Branch-aware Business Intelligence

`GET /api/bi/branches` (`businessintelligence.branches`, Medium, Admin) answers the whole company
beside each of its branches over one period. It ignores the caller's own branch selection on
purpose: a comparison that showed one branch would not be one.

The consolidated row is the same measurements with `BranchId` left null, so the parts and the
whole are never two different definitions of revenue — and the desktop lists the company row
first, above the branches, so a reader can check one against the other without changing screens.

---

## Authentication and authorization

JWT bearer, issued by `POST /api/auth/login` — the only anonymous endpoint.

```
AppUsers (master DB)
  → password verified with PBKDF2 (ASP.NET Core PasswordHasher)
  → account active? company active?
  → role + per-user overrides + company tier
  → effective module list
  → signed token carrying company_id, role_key, an optional branch_id,
    and one "module" claim per module
```

The `branch_id` claim is present only when the account is bound to a branch, and its *absence*
is as meaningful as its presence: it is what tells the branch middleware that this caller may
choose a branch for themselves. See [Branching](#branching).

Accounts live in the **master** database because a user is the thing that *selects* a tenant.
Storing them per tenant would mean knowing the tenant before authenticating, which is the wrong
way round.

### Enforcement

`[RequireModule("payroll")]` answers **403** for a caller without the module, and
`[RequireSubmodule(ErpModules.Sub.FinancialPeriods)]` for one without the subfeature. Branch
scoping is enforced separately and lower down, by a query filter in the DbContext rather than by
a filter on the action — an endpoint cannot be reached for the wrong branch, because there is no
query in the application that reads outside the scope the server set. Hiding a
sidebar entry or a tab is a courtesy; these are the boundary. Where a controller rule and an
action rule disagree, the action wins — which is how Staff reach `GET /api/reports/dashboard` on
a controller otherwise reserved for Business Intelligence, and how a Manager reaches the whole of
Finance except `/api/finance/periods`.

Closing a period is the example worth knowing: it locks the books against any further posting,
which is an owner's decision rather than a manager's, so Financial Periods is the one Finance
subfeature at Admin level while the rest are Manager.

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
`AppRolePermissions`, `AppUserPermissions`, `AuditEvents`, plus the ASP.NET Identity tables, plus
the platform tables: `SubscriptionPlans`, `CompanySubscriptions`, `PlatformSettings`.

**Tenant** (one per company) — `Branches`, `Members`, `MemberNotes`, `MembershipPlans`, `Subscriptions`,
`Payments`, `Sales`, `SaleItems`, `SaleReturns`, `SaleReturnItems`, `Products`, `Inventories`,
`StockMovements`, `Purchases`, `PurchaseItems`, `SupplierPayments`, `Employees`, `Attendances`,
`LeaveRequests`, `Payrolls`, `Expenses`, `Customers`, `Suppliers`, `TenantSettings`,
`AuditEvents`, and the finance tables: `Accounts`, `JournalEntries`, `JournalEntryLines`,
`FinancialPeriods`, `Budgets`, `BudgetLines`, `CashAccounts`, `BankTransactions`.

There is no `CompanyId` column on tenant tables: isolation comes from connecting to a different
physical database.

Thirteen of them *do* carry a nullable `BranchId`, which divides one company rather than
separating two. See [Branching](#branching) for the list and for why the filter lives in the
DbContext rather than in any service.

### The ledger

Double-entry, posted automatically from the operations that cause it. `FinancePostingService` is
the bridge; `JournalService` enforces that every entry balances, refuses to alter a posted one and
refuses a closed period.

```
Purchase received  →  Inventory (asset) ↑        Accounts Payable ↑
Supplier paid      →  Accounts Payable ↓         Cash ↓
Sale               →  Accounts Receivable ↑      Product Revenue ↑
                      Cost of Goods Sold ↑       Inventory ↓
Payment taken      →  Cash ↑                     Accounts Receivable ↓
Membership sold    →  Accounts Receivable ↑      Membership Revenue ↑
Discount / return  →  contra-revenue ↑ (a debit against revenue, not a hidden deduction)
Payroll paid       →  Salaries + Overtime + Employer contributions ↑
                      Statutory payables ↑       Cash ↓
Expense            →  the matching expense account ↑        Cash or Payable ↓
```

Consequences that follow from this and are worth keeping straight:

- **Buying stock is not an expense.** A received purchase becomes an asset; it becomes cost only
  when the goods are sold, at which point the cost is frozen onto the sale line. Revenue − COGS is
  gross profit, and gross profit − operating expenses (payroll included) is net income.
- **Posting never vetoes the operation.** `GuardedAsync` catches everything, logs it and returns a
  result — a committed sale is never rolled back because the books could not be written. The
  Finance screen's catch-up sweep offers to post whatever was missed.
- **Posting is idempotent**, keyed by `(SourceEntityName, SourceEntityId)`, so a retry or a sweep
  cannot double-count. A renewal is keyed by the subscription *and* the term it buys, because
  renewing extends a subscription in place rather than creating a row to key off.
- **Entries are reversed, never deleted.** The original stays `Posted` and gains
  `ReversedByEntryId`; the mirror is posted too, and the pair nets to zero. There is deliberately
  no `Reversed` status — marking the original as no longer counting would leave only the reversal
  in the balances and put every affected account out by the original amount, in the opposite
  direction.
- **The ledger records when a transaction happened**, not when the term it pays for begins. A
  membership sold today that starts next month, or renewed a year early, posts today. Dating it
  forward left the payment on the books with no receivable against it and drove receivables
  negative — and the trial balance could not catch it, because a misdated entry is still balanced.
- **Retained earnings are computed**, not stored: revenue less expenses. Nothing closes the books,
  so this is what keeps the balance sheet balanced without a year-end journal.

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

The same rule runs through the rest of the master data, and the reason is always that **history
must survive its subject**:

- **A product with stock movements cannot be deleted.** This one is not protected by a foreign
  key — it is endangered by one. `Inventory` and `StockMovement` both *cascade* from `Product`,
  so deleting a product does not fail; it silently takes the movement ledger with it. That
  ledger is what explains the inventory figure on the balance sheet, so the asset would be left
  in the books with nothing to justify it. `ProductService` refuses first. A product nothing has
  ever happened to is still freely deletable.
- **A supplier with purchases, stock movements, payments or expenses cannot be deleted.** Here
  the foreign keys do refuse — but as a provider error, which becomes a 500 and reads as
  "server problem" on the desktop: true, useless, and indistinguishable from an outage.
  `SupplierService` checks first and names what is in the way.

Both are covered by `RelationalIntegrityTests`, which asserts the refusal *and* that the history
it was protecting is still there afterwards.

### Two tills, one last item

`Inventory.QuantityOnHand` is a **concurrency token**.

Selling stock is read-check-write: read the quantity, refuse if it is too low, subtract. Two
tills doing that at the same moment both read the same figure, both pass the check, and both
write what they calculated — and under read-committed, which is what an ordinary transaction
gets, the second write simply overwrites the first. The gym sells six of the last five, and the
two stock movements both claim to have started from the same balance, which breaks the chain
that is supposed to explain the quantity on hand.

Marking the column as a token puts *"and the quantity is still what I read"* into the UPDATE's
WHERE clause. The loser affects no rows, EF raises `DbUpdateConcurrencyException`, the sale's
transaction rolls back, and `ApiExceptionHandler` answers **409** with a message that says to
refresh and try again — not 500, because the request was legitimate and retrying is exactly the
right response.

A concurrency token rather than a `rowversion` on purpose: it needs no column, and therefore no
migration against three live tenant databases, and it behaves identically on SQL Server and on
the SQLite the tests run against. `dotnet ef migrations has-pending-model-changes` confirms it
is not a schema change.

---

## API

All business routes are tenant-scoped and carry no company id. None of them carries a branch id
either — the branch travels in the signed token or in the `X-Branch-Id` header, and the server
decides which of the two it honours. See [Branching](#branching).

| Module | Routes |
|---|---|
| Members | `GET`/`POST /api/members`, `GET`/`PUT`/`DELETE /api/members/{id}`, `POST /api/members/{id}/archive`, `/restore`, `GET /api/members/search?term=`, `/{id}/subscriptions`, `/payments`, `/sales` |
| Membership plans | `GET`/`POST /api/membership-plans`, `GET /api/membership-plans/active`, `GET`/`PUT`/`DELETE /api/membership-plans/{id}` |
| Subscriptions | `GET`/`POST /api/subscriptions`, `/active`, `GET`/`DELETE /{id}`, `POST /{id}/renew`, `/cancel`, `POST /api/subscriptions/expire-overdue`, `GET /{id}/payments` |
| Payments | `GET`/`POST /api/payments`, `GET`/`PUT`/`DELETE /{id}`, `PATCH /{id}/status` |
| Sales | `GET`/`POST /api/sales`, `GET`/`DELETE /{id}`, `GET /{id}/items` |
| Products | `GET`/`POST /api/products`, `/active`, `GET`/`PUT`/`DELETE /{id}` |
| Inventory | `GET /api/inventory`, `/movements`, `/{productId}`, `POST /{productId}/stock-in`, `/stock-out`, `/adjust`, `PUT /{productId}/reorder-level` |
| Suppliers | `GET`/`POST /api/suppliers`, `GET`/`PUT`/`DELETE /{id}`. Gated on Inventory. |
| Sale returns | `GET`/`POST /api/returns`, `/reasons`, `/returnable/{saleId}`, `GET /{id}`, `POST /{id}/cancel` |
| Purchasing | `GET`/`POST /api/purchases`, `/summary`, `/statuses`, `/payments`, `GET`/`PUT`/`DELETE /{id}`, `POST /{id}/order`, `/receive`, `/cancel`, `POST /api/purchases/payments` |
| Attendance | `GET`/`POST /api/attendance`, `/summary`, `GET`/`PUT`/`DELETE /{id}` |
| Leave | `GET`/`POST /api/leave`, `/types`, `/balance/{employeeId}`, `GET`/`PUT`/`DELETE /{id}`, `POST /{id}/decide`, `/cancel` |
| Reports | `GET /api/reports/dashboard`, `/membership-overview`; and one report per module, each taking `?from=&to=` and each guarded by the module it reports on rather than by Business Intelligence: `/sales`, `/payments`, `/inventory`, `/membership`, `/employees`, `/payroll`, `/expenses` (Finance), `/payment-reconciliation` (Medium, because it compares the takings against the ledger) |
| Business Intelligence | `GET /api/bi/kpi`, `/areas`, `/overview`, `/membership`, `/sales`, `/payments`, `/inventory`, `/workforce`, `/finance`, `/profitability` — each takes `?from=&to=`; and `/branches`, the company beside each of its branches (Medium, Admin) |
| Finance | `GET /api/finance/overview`, `POST /post-outstanding`; accounts `GET`/`POST /accounts`, `/accounts/types`, `GET`/`PUT`/`DELETE /accounts/{id}`; journal `GET`/`POST /journal`, `/journal/sources`, `GET`/`PUT`/`DELETE /journal/{id}`, `POST /journal/{id}/post`, `/reverse`; `GET /ledger/{accountId}`; reports `/reports/trial-balance`, `/income-statement`, `/balance-sheet`, `/cash-flow`; `/receivables`, `/payables`; periods `GET /periods`, `POST /periods/{year}/{month}/close`, `/reopen`; budgets `GET`/`POST /budgets`, `GET /budgets/{id}`, `/variance`, `PUT /budgets/{id}`, `/lines`, `POST /budgets/{id}/approve`, `/spread`, `DELETE /budgets/{id}`; banking `GET`/`POST /bank-accounts`, `GET`/`PUT`/`DELETE /bank-accounts/{id}`, `GET`/`POST` transactions, reconciliation |
| Branches | `GET`/`POST /api/branches`, `GET`/`PUT`/`DELETE /api/branches/{id}`, `POST /{id}/primary`, `POST /api/branches/transfer` — a System Administration subfeature at Admin level, so Medium and the owner only |
| Platform (Super Admin) | `GET /api/platform/tenants`, `/plans`, `/subscriptions`, `/users`, `/settings`, `/audit`, `/health`, `/dashboard`, `/analytics` |
| Settings | `GET`/`PUT /api/settings`, `POST /api/settings/{key}/reset` |
| Audit | `GET /api/audit-events` |
| Data integrity | `GET /api/data-integrity` — the read-only consistency sweep. Admin on every tier; there is deliberately no repair endpoint beside it |
| Employees | `GET`/`POST /api/employees`, `/active`, `/search?term=`, `GET`/`PUT`/`DELETE /{id}`, `GET /{id}/payrolls` |
| Payroll | `GET`/`POST /api/payroll`, `/summary`, `GET`/`PUT`/`DELETE /{id}`, `PATCH /{id}/status` |
| Expenses | `GET`/`POST /api/expenses`, `/summary`, `/categories`, `GET`/`PUT`/`DELETE /{id}`, `POST /{id}/settle` — a Finance subfeature, so Medium only |
| Authentication | `POST /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/refresh`, `/change-password`, `/logout` |
| Users | `GET`/`POST /api/users`, `/roles`, `/modules`, `GET /{id}`, `/{id}/permissions`, `PUT /{id}`, `/{id}/permissions`, `PATCH /{id}/role`, `/{id}/status`, `POST /{id}/reset-password` — a System Administration subfeature, so Medium only |
| Tenant | `GET /api/tenant/current`, `GET /api/tenant/probe/{companyId}` (admin) |
| Companies | `GET`/`POST /api/companies`, `/databases`, `/devices`, `GET /{id}/provisioning`, `POST /{id}/provision` — Super Admin + the cross-tenant flag |

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

⚠️ **Credentials are currently committed in plain text, and should not be.**

`ERP_api/appsettings.json` in this repository holds real database passwords, in
`ConnectionStrings` and in `TenantCredentials`, for the master database and all three tenants.
They were committed in `72f7fe2`. `Jwt:SigningKey` and `Bootstrap:SeedPassword` are still empty
there and come from configuration, as intended.

This is the design the rest of this section describes being *violated*, not a second design. The
`MasterErp` entry added for the platform company follows the same pattern as the entries beside
it, so it is consistent with the file — not with the policy.

**What needs doing:** rotate all four passwords in the MonsterASP control panel, move them into
`appsettings.Development.json` (gitignored) or user-secrets for local work and environment
variables for deployment, and restore `appsettings.json` to shipping empty values. Rotation is
required regardless, because these have been in the repository's history and in shell history.

The intended arrangement, which everything else already supports:

Local development reads them from `appsettings.Development.json`, which is **gitignored**, or
from .NET user-secrets. `setup-secrets.ps1` at the repo root loads user-secrets; fill in the
placeholder values first. Note that user-secrets are loaded *after* `appsettings.Development.json`
and therefore win, so do not set the same key in both.

Production reads them from environment variables:

```
ConnectionStrings__MasterErp
ConnectionStrings__TenantErp
TenantCredentials__MasterErp__Password     # the platform company resolves to the master database
TenantCredentials__TenantA__Password
TenantCredentials__TenantB__Password
TenantCredentials__TenantC__Password
Bootstrap__SeedPassword
Jwt__SigningKey
Cors__AllowedOrigins__0
```

The master database stores only the credential *key* for each tenant, never a password, so
`CompanyDatabases` can be read without exposing anything.

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
.\fitcore.ps1 update    # apply pending migrations to master + all 3 tenants
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

297 tests. `dotnet test ERP_Tests/ERP_Tests.csproj`

- **Authorization over real HTTP** (`ApiAuthorizationTests`) — anonymous callers refused
  everywhere but sign-in; Micro refused every module above its tier even as Admin; Small reaches
  Employees and Payroll but not Finance or Users; Staff held to the front desk; the dashboard
  reachable on a controller otherwise reserved for Business Intelligence; tokens signed with the
  wrong key and expired tokens rejected. These drive the real pipeline and touch no database — a
  refusal is decided before anything reaches a tenant connection, which is itself the property
  being asserted.
- **The module reports** (`ApiAuthorizationTests`) — each report is guarded by the module it
  reports on, so a Micro tenant is refused the Employees and Payroll reports however senior the
  caller, a Micro *manager* still reaches the reports of the four modules Micro does have, Staff
  are refused every module report even for modules they hold, the expense report is refused
  wherever `/api/expenses` is, and reconciliation needs the ledger and so needs Medium. The
  positive half matters as much as the negative: it is what proves moving the guards off
  Business Intelligence did not quietly narrow anything.
- **The module reports, against real SQLite** (`ModuleReportTests`) — the reports are asserted
  against records created through the operational services rather than rows inserted by hand,
  for the same reason the ledger tests are. Attendance totals come from the rows payroll reads;
  an employee with nothing recorded still appears, and their rate is 0 rather than 100; leave
  spanning a month end is counted in both months it touches; a pay run belongs to the month it
  *ends* in, so twelve monthly reports partition the year; gross − deductions = net and
  gross + employer share = total cost; and reconciliation balances when posting is on, names the
  individual payments when it is off, and ignores money that was only ever pending.
- **Tier and permission resolution** — the full matrix, including that an explicit grant of a
  Medium module to a Super Admin on a Micro tenant still yields nothing, that no Small module
  reaches a Micro tenant however it is granted, and the invariant that no submodule is ever
  licensed below the module it belongs underneath.
- **Tenant resolution** — claim beats header, header ignored outside Development, an
  unauthenticated claim is not trusted.
- **Connection strings** — registry lookup, fallback, failure cases, and that a password
  containing connection-string syntax survives intact. Also that the fallback is refused for a
  company it is not bound to, and that an unbound fallback never applies at all.
- **Audit trail** — attribution, generated keys, changed-properties-only updates, that the trail
  does not record itself, and that no password hash ever reaches it.
- **Service rules** — member delete guards, duplicate product codes, stock arithmetic, sale
  transactions, payroll calculation and overlapping periods. Run against real SQLite.
- **The ledger, through the operations that feed it** (`LedgerTests`, 34) — the entries a *sale*
  produces rather than entries written by hand, because the ledger's whole value is that it agrees
  with the till. Covers the purchase→stock→sale→statement chain, weighted-average cost, discounts
  and returns as contra-revenue, idempotent re-posting, reversal leaving the original posted, a
  closed period refusing a posting, and the two date rules: a renewal or a future-starting
  membership posts on the day it was sold, while a backdated one keeps its own date.
- **Relational integrity, against a real relational database** (`RelationalIntegrityTests`) —
  that a product with stock movements cannot be deleted *and the movements are still there
  afterwards*, that one with no history still can, that a supplier with purchases is refused
  with a sentence rather than a 500, that stock on hand always equals the closing balance of an
  unbroken movement chain, and that a later, dearer purchase moves the weighted average without
  reaching back into what an earlier sale already cost.
- **Branch scoping, against real SQLite** (`BranchScopingTests`, 18) — the property being
  asserted is that isolation belongs to the data access layer rather than to any one service, so
  every read here goes through an ordinary `DbSet` query that says nothing about branches and is
  expected to be narrowed anyway. Covers: a scoped caller reading only their own branch; an
  unscoped caller seeing every branch *and* the unassigned rows; an unassigned row **not** being
  lent to whichever branch asks, because that would double-count it in the comparison; the
  filter reaching every `IBranchScoped` entity rather than only `Member`; the product catalogue
  staying shared; a scoped write landing in the right branch without saying so; an unscoped
  write falling to the primary branch; an explicit branch surviving the stamp; a tenant with no
  branches being completely unaffected; the delete guard refusing a branch that has traded *and*
  its history still being there afterwards; the primary branch refusing to be deleted or closed;
  a transfer moving people and leaving the sale where it was rung; and the comparison measuring
  each branch and the company consistently, so the parts sum to the whole.
- **The branch boundary over real HTTP** (`ApiAuthorizationTests`) — branch administration
  refused below Medium and refused to a Manager and to Staff; a Medium owner reaching it; and
  the one that matters most, that an account carrying a branch claim cannot reach another branch
  by sending `X-Branch-Id`.
- **Concurrency** — two contexts on one database, which is what two HTTP requests are. Both read
  the same stock, both try to take it, and the second is refused with
  `DbUpdateConcurrencyException` instead of silently overselling. This test failed before the
  concurrency token existed, which is how the bug was found.
- **The integrity sweep itself** — a database built entirely through the services reports
  clean; stock nudged out of step with its ledger is caught as Critical; a journal line knocked
  out of balance is caught; a deduction total that is right but unattributed is a *Warning*
  rather than a Critical, because the net pay and the ledger are untouched; and the finance
  checks stay silent on a tenant with no chart of accounts, so a tier boundary never reads as a
  fault.
- **Payroll rounding** — every statutory figure is a whole number of centavos and the totals are
  the sums of exactly those figures. A gross of 15,725 puts PhilHealth on 393.125 and is the case
  that found the bug; rounding late made the payslip disagree with itself and the journal entry
  unbalanced by a centavo, so it was refused silently and a paid run reached nobody's books.

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

## Verification

Two scripts at the repo root drive the live API. Start it first (`.\fitcore.ps1 api`).

### `verify-access.ps1` — the authorization boundary

706 checks: every seeded account against every module, including the nine branch accounts. It
asserts that each answer is **deliberate** rather than that everything succeeds — a Micro tenant *should* be
refused Sales, and Staff *should* be refused purchasing. A 200 or a 403 is a decision; anything
else is a defect.

The sweep covers every module report as well as every module, because a report is a way of
reading a module and must answer the same way the module does: the Sales and Inventory reports
refused on Micro while the membership and payment reports still answer, the expense report
refused wherever `/api/expenses` is, branch administration refused below Medium and refused to
a Manager, and platform monitoring refused to every tenant role on every tier.

### `GET /api/data-integrity` — does this tenant's data still hang together?

Not a script but an endpoint, and a tab under System Administration. It sweeps the tenant
database and reports; it never repairs. An automatic fix for a problem nobody has understood yet
is how one bad row becomes a thousand, and the two findings that *do* have a supported recovery
— payments and sales that never reached the ledger — name the Finance catch-up sweep instead.

Two kinds of thing are checked, and the second is the point:

- **Orphans**, which the foreign keys should already make impossible. Expected to be zero, and
  run anyway, because "should be impossible" is a claim worth testing against a database three
  tenants have been writing to.
- **Cross-table arithmetic**, which no foreign key can express: that a journal entry balances,
  that its header totals match its own lines, that stock on hand equals the closing balance of
  the movement ledger, that a sale's total is the sum of its lines less its discount, that net
  pay is gross less deductions and that those deductions are the sum of their named parts, and
  that no employee has two pay runs covering the same day. This is where real drift shows up,
  because nothing in the schema prevents it.

The finance checks are skipped for a tenant with no chart of accounts: a Micro or Small gym has
no ledger by design, and "0 of 0 entries balanced" would turn a tier boundary into a finding on
every sweep they ever run.

Current state, run against all three live tenants:

| Tenant | Checks | Issues | Critical |
|---|:--:|:--:|:--:|
| Micro (`db68433`) | 27 | 0 | 0 |
| Small (`db68484`) | 27 | 3 | **0** |
| Medium (`db68521`) | 32 | 0 | 0 |

The three on Small are payroll runs 28–30, whose deduction total is correct and whose net pay is
right, but where the amount sits in no named column. That is the signature of rows written
before the statutory breakdown columns existed: the migration that added them defaulted them to
zero and did not backfill. Nothing is out by a centavo and no ledger entry is affected — the
payslip simply cannot itemise itself. **It is deliberately reported as a Warning rather than a
Critical**, because grading it alongside a payslip whose net does not follow from its own gross
would bury the severe case among harmless ones. Every write path in the current code keeps the
parts in step, so no new run can arrive this way; the existing three are a data decision, not a
code fix, and have been left alone.


### `verify-flows.ps1` — running a gym end to end

120 steps against the live Medium tenant: a member joins, pays and renews; stock is bought from a
supplier, received, sold, and some of it returned; staff are hired, their attendance recorded,
leave approved and payroll run and paid; bills are settled — and then the books are checked.

The checks after the fact are the point, because a balanced ledger is a weak signal on its own:

- the trial balance still balances, and moved
- net revenue = revenue − discounts − returns
- gross profit = net revenue − COGS, and net income = gross profit − expenses
- assets = liabilities + equity
- cost of sales moved by a fraction of the stock bought, so buying stock was not expensed
- payroll reached the income statement
- a day of selling memberships did not *reduce* receivables
- no live entry is dated in the future
- every BI area has cards, charts with plotted points, and non-zero measured values
- the same KPI request twice returns the same figures
- every area `/api/bi/areas` advertises is actually served

Several of those are asserted as a **change** rather than a level, so they stay meaningful on a
tenant that already carries history. Three of them were written after they caught something:
receivables going negative, revenue posted a year ahead, and `/api/bi/overview` being built,
listed and unreachable.

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

1. **Rotate the database credentials, and get them out of `appsettings.json`.** Four real
   passwords — master plus all three tenants — are in the committed file right now, in both
   `ConnectionStrings` and `TenantCredentials`. See [Secrets](#secrets). 🔴
2. **Tenant connections are unencrypted.** `EncryptTenantConnections` defaults to `false`
   because the current shared host refuses an encrypted connection — the TLS handshake fails.
   This is a hosting limitation to raise with MonsterASP, not a preference. 🔴
3. ~~**Medium tier is unimplemented.**~~ **Done.** Tenant C is live on `db68521` with all nine
   modules, Finance posting from every operation, and the eight Business Intelligence areas. ✅
3b. ~~**There is no Branch entity, so multi-branch is not supported in any form.**~~ **Done.**
   Tenant C runs three branches. Branch scoping is a global query filter in the DbContext, so it
   applies to every query in the application rather than to the ones a service remembered to
   narrow. See [Branching](#branching). ✅
4. **Micro and Small have no in-app user management, settings screen or audit trail**, because
   System Administration is Medium. Accounts come from the `Bootstrap` section. ⚠️
4a. **Branch stock is shared, not per-branch.** `Product` and `Inventory` are company-wide, so
   every branch sells from one catalogue and one stock pool; `StockMovement` records which
   branch moved it. Per-branch stock would change the concurrency token and the unique index on
   `Inventory`, which is a real change and has not been made. ⚠️
4b. **Analytics is slow against the remote database.** `/api/bi/kpi` takes about nine seconds and
   `/api/reports/dashboard` about eight. The cause is round-trip latency to a shared remote host,
   not the queries themselves: each is a few hundred milliseconds and these endpoints make dozens
   sequentially. Three N+1 patterns were removed — profitability was building a full income
   statement per month, twelve times (15s → 2.3s); the finance KPI group was asking for four
   complete reports to fill nine cards; and the chart-of-accounts check re-read the whole table on
   every finance call. What remains is latency × query count. The fix beyond this point is to run
   the seven KPI groups concurrently on separate DbContexts, which is a real change and has not
   been made. Under concurrent load the slowest of these can still exceed the command timeout and
   answer 500. ⚠️
5. ~~`/api/customers` and `/api/suppliers` have no UI page.~~ **Superseded.** Suppliers has a page
   under Inventory with list, search, status filter, create, edit, delete, validation, pagination
   and empty states. Customers was removed instead of finished: it was never wired to a sale
   (`Sale` has no `CustomerId` and never did — only `MemberId`), so it was a disconnected roster
   with no transaction behind it. Sales now names a member, a typed walk-in name, or neither,
   which is what the roster existed to approximate. The `Customer` entity and table remain in the
   schema, unused, exactly like `MemberNoteService` after the Notes button was removed below. ✅
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
12. ~~**Membership, Payments and Sales always required an existing Member.**~~ **Done.**
    `Subscription`, `Payment` and `Sale` all carry a nullable `MemberId`; a null one carries a
    `WalkInName` instead (defaulting to "Walk-In" when left blank). A walk-in is not a Member
    record created on the fly — it is genuinely memberless, so every read site that used to say
    `sale.Member.FirstName` now falls back to `sale.WalkInName ?? "Walk-In"`. The Subscriptions
    tab's own listing (`MemberService.GetMembershipOverviewAsync`) was rebuilt to read every
    subscription rather than every member's subscriptions, since a walk-in's membership has no
    Member row to be found through. Payments gained a "What is this for?" choice (Membership /
    Walk-In / Sale / Other) ahead of the usual fields, and both Payments and Sales gained
    Amount Tendered → Change, computed server-side and never accepted from the client. ✅
13. ~~**The Subscriptions tab 403'd on every tier below the one that has Reports.**~~ **Done.**
    `GET /api/reports/membership-overview` is the tab's own data, not a report, but it sat on a
    controller whose class-level rule required the Reports subfeature; the action overrode the
    module check but not the submodule one, so the class rule kept applying underneath it. Fixed
    with an explicit `[RequireSubmodule(Sub.Subscriptions)]` override on that one action.
14. ~~**A Manager could not edit a Staff employee's or payroll's pay, and could edit a peer's.**~~
    **Done, twice.** `EmployeeService`/`PayrollService` originally gated every pay change behind
    `RequireAdminFor` unconditionally; a first pass introduced `RequireSalaryAuthority(target)`
    restricted to Staff-level targets only. That restriction has since been **deliberately
    widened**: a Manager may now set pay for anyone — Staff or a peer Manager alike — except
    themselves, which is the one rule that never bends. The Staff-only restriction was also the
    direct cause of "Only Admin can edit Staff salary" firing against a genuinely Staff-level
    employee whose stored `Position` wasn't the literal string `"Staff"` (`"Receptionist"`,
    seeded by `TenantBranchBootstrapper`) — see item 19.
15. **Business Intelligence, including its own dashboard, was withdrawn from Staff entirely**
    rather than narrowed, on every tier. `ShellForm.OnShown` already picks a Staff account's
    first reachable module as the landing screen, so this is not a missing-dashboard bug — it is
    the module genuinely not being theirs. The one existing installation's `staff` role had
    already been seeded with the module (as the legacy `dashboard` key, which normalises to
    `businessintelligence`); since `MasterBootstrapper` never edits an existing role's grants,
    that row was removed by hand rather than by a code change nobody's database would see.
16. **The `Customers` submodule was removed**, not merely its tab — see item 5 above.
17. **Live data cleanup.** Tenant C (`db68521`) had accumulated fifteen-plus repetitions of
    `verify-flows.ps1`'s throwaway rows, tagged `Verification ...`, across Members, Employees,
    Products, Suppliers, MembershipPlans, Budgets and BankAccounts, and every Sale, Payment,
    Subscription, Purchase, Payroll, Attendance and JournalEntry that traced back to them —
    effectively the tenant's whole operational history. All of it was removed (the nine real
    branch staff and the chart of accounts were kept), and a small set of realistic replacement
    records was created through the ordinary API — two members with an annual and a monthly
    membership, one paid in full and one left with a genuine outstanding balance; a supplier and
    three products including a `Food`-category item; a purchase received and partially paid,
    for a real payable; a walk-in cash sale exercising the change calculator; and three
    non-"Electricity" expenses and three budgets. `Members`, `Employees`, `Products` and
    `Suppliers` all resumed their `IDENTITY` sequence from where the deleted rows left off rather
    than from 1, which is expected and not itself a sign anything was missed.
18. **`DataIntegrityService`'s member-orphan checks were a false positive waiting to happen.**
    `orphan.subscription.member` and `orphan.payment.member` read a null `MemberId` as "points at
    a member that does not exist" rather than "correctly names no member" the moment walk-ins
    existed — caught by the sweep itself immediately after the Tenant C reseed, on the walk-in
    sale's own payment. Both now skip the row entirely when `MemberId` is null, matching the
    pattern the sale/subscription orphan checks already used for their nullable columns.
19. ~~**`EmployeePositions.Normalise` only recognised the literal strings "Staff" and
    "Manager".**~~ **Done.** Real seeded data (`TenantBranchBootstrapper`, written straight to
    `Employee.Position` with no service-layer validation in between) legitimately stores
    `"Branch Manager"` and `"Receptionist"`. `Normalise` now treats any value containing
    "manager" (case-insensitive) as Manager and defaults everything else non-blank to Staff —
    the same floor `ToRoleKey` already used for an unrecognised value — instead of returning
    `null` and tripping a validator with nothing to say but "Position must be Staff or Manager."
    Saving an employee whose canonical bucket hasn't changed also no longer collapses their real
    title to the bare canonical word: `RequirePositionChange` keeps the stored text verbatim
    unless an actual promotion or demotion is happening. The `[RegularExpression("^(Staff|Manager)$")]`
    that duplicated this check at the API edge — and ran before the service ever saw the
    request — is gone; `EmployeeService`'s own `RequirePosition`/`RequirePositionChange` is the
    one place this is validated now.
20. **The Sales screen no longer offers a Cashier, Member or Walk-in control.** All three were
    redundant with what the server already does unconditionally from the signed-in account:
    `SaleService.CreateSaleAsync` already fell back to the actor's own `EmployeeId` for
    `CashierEmployeeId`, and `ProcessedByUserId`/`ProcessedBy` were already taken from the JWT
    regardless of anything the client sent. Every sale rung up at the till is now a walk-in
    (`MemberId`/`WalkInName` both `null`, which the server already defaults to "Walk-In").
21. ~~**A sale's receipt could read "Cashier: - unassigned -" even when a real person processed
    it.**~~ **Done.** `SaleView.CashierName`/`SaleService.ToView` defaulted to the literal string
    `"- unassigned -"` rather than blank, so `ReceiptBuilder.ForSale`'s fallback to
    `sale.ProcessedBy` — written correctly — could never trigger, because `IsNullOrWhiteSpace`
    is never true for a non-blank placeholder. The default is now `""`; the existing fallback
    works as originally intended with no receipt-side change at all.
22. **The Administration/Tenants grid shows Client Name, Admin and Admin email instead of a
    Database column, and drops the Status column entirely.** `TenantView` gained `AdminFullName`/
    `AdminEmail`, resolved the same way `GetTenantsAsync` already resolves everything else — one
    grouped query (earliest-created `Admin`-role `AppUser` per company) rather than one query per
    row. The standalone "Change tier" button is gone; the Edit dialog now carries the tier combo
    and the reason field together, and the reason is only required when the tier actually
    changes. Tenant *creation* (Server/Database/CredentialKey entry) is unchanged — automating
    that was scoped out, since every tier's "known" database is already a live paying tenant's
    own, and this architecture has no `CompanyId` column on tenant tables to ever separate two
    companies' rows again once mixed.
23. **Membership's Archive button is gone; Restore stays.** The button duplicated what Suspend
    already covers for a gym that is still open, and removing it was a direct ask. The
    `POST /api/members/{id}/archive` endpoint is untouched — the Blazor client and the test suite
    both still use it — and Restore remains for members archived through either of those paths.
24. **`FieldKind.Phone`** (`EditDialog.cs`) rejects a keystroke that could not belong to a phone
    number — digits, spaces, parentheses, a hyphen, one leading plus — deliberately looser than
    digits-only, since real seeded numbers (`"0917-555-0142"`) already contain hyphens. Applied
    to the four existing Phone fields (Members, Employees, Branches, Suppliers). No new
    server-side format rule: this is UI-level prevention of obviously wrong input only.

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

**403 on `/api/sales`, `/api/products`, `/api/employees` or `/api/finance/*` as a Micro admin** —
expected. Micro is Membership and Payments alone. The membership and payment reports still work,
because a module report is guarded by the module it reports on.

**403 on `/api/users`, `/api/settings`, `/api/audit-events` or `/api/branches` on Micro or
Small** — expected. System Administration is Medium. On Medium, `/api/branches` is additionally
Admin-only, as is `/api/finance/periods`, and `/api/bi/*` analytics is Manager and above.

**400 "That branch does not belong to this company"** — an `X-Branch-Id` naming a branch that is
not this tenant's, or has been closed. Refusing is deliberate: serving the request unscoped
would quietly show the whole company to somebody who asked for one branch.

**403 on `/api/platform/*` as a tenant Admin** — expected, and the point. Platform administration
is the Super Admin's alone, at role level 0, on every tier.

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
- Adding a feature: decide **which of the nine it belongs inside** — never add a tenth module →
  a submodule in `ErpModules.Sub` and a definition in `ErpModules.Submodules` with its minimum
  tier and role → entity → `DbSet` and configuration → repository → service → register in
  `AddErpApplicationServices` → DTOs and `ToDto()` → controller action carrying
  `[RequireSubmodule(...)]` → migration → DTO and typed service in `ERP_Project1/Api` → page
  deriving from `CrudPageBase<T>` → a `WorkspaceTab` in `ShellForm.TenantCatalogue()` guarded by
  the same submodule key.
- **Finish the chain or do not start it.** A submodule declared in the catalogue with no tab is
  a feature the product advertises and cannot open; a tab guarded on a key the catalogue does not
  define is drawn for nobody. Keep `ErpModules.Submodules` and `ShellForm.TenantCatalogue()` plus
  `ShellForm.PlatformCatalogue()` in step — between them the two catalogues should cover exactly
  the submodules the catalogue declares.
- **Guard an endpoint on the submodule the tab is guarded on.** Where they differ, the tab is
  drawn and the endpoint refuses, which is the one combination hiding a tab is meant to prevent.
  A controller-level rule is a fallback; override it per action when an action belongs to a
  different subfeature, as the module reports do.
- A new submodule needs **no migration and no permission backfill**, because submodules are
  derived from (module ∧ tier ∧ role level) rather than stored. Adding one to the catalogue is
  the whole change.
- If an endpoint posts to the ledger, post it through `FinancePostingService` so it is guarded,
  idempotent and unable to fail the operation that triggered it — and date the entry when the
  transaction happened, not when whatever it pays for begins.
- Desktop screens never call `MessageBox` for a server failure: return the message from the
  `EditDialog` save callback, or pass it to `ShowError`, so it lands in the right place.
- New numeric fields get a `Minimum`; new text fields get a `MaxLength` matching the column.
- **A new operational entity that belongs to one site implements `IBranchScoped`** and gets a
  nullable `BranchId`. That is the whole opt-in: the DbContext sweep finds it, filters every
  query over it and stamps every insert into it. Do not add branch handling to a service - a
  service that filters by branch itself is a service that can be written without doing so.

`ERP_winforms` does **not** reference `ERP_infrastructure`, so a change to a service interface
or an entity cannot break it directly — only a change to the wire contract can. Keep
`ERP_Project1/Api/*Dtos.cs` in step with `ERP_api/DTOs`.

Older status reports have been moved to `docs/archive/`. They describe earlier states of the
project and contradict each other; this file is the current one.
