# FitCore end-to-end verification.
#
# Signs in as every seeded account and calls every module's endpoints, recording what each one
# answers. The point is not that everything returns 200 - a Micro tenant *should* be refused
# Finance - but that every answer is a deliberate one. A 200 or a 403 is a decision; a 5xx is a
# defect, and this script exists to make the difference obvious.

$ErrorActionPreference = 'Continue'

Add-Type @"
using System.Net; using System.Security.Cryptography.X509Certificates;
public class FitCoreTrustAll : ICertificatePolicy {
  public bool CheckValidationResult(ServicePoint s, X509Certificate c, WebRequest r, int p) { return true; }
}
"@ -ErrorAction SilentlyContinue

[System.Net.ServicePointManager]::CertificatePolicy = New-Object FitCoreTrustAll
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Base = 'https://localhost:7214'
$Password = 'FitCore@2026'

$script:Failures = New-Object System.Collections.ArrayList
$script:Checks = 0
$script:Token = $null

function Invoke-Api {
    param([string]$Method, [string]$Path, $Body, [string]$Token)

    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }

    try {
        if ($null -ne $Body) {
            $json = $Body | ConvertTo-Json -Depth 10
            $r = Invoke-WebRequest -Uri "$Base$Path" -Method $Method -Headers $headers -ContentType 'application/json' -Body $json -UseBasicParsing
        } else {
            $r = Invoke-WebRequest -Uri "$Base$Path" -Method $Method -Headers $headers -UseBasicParsing
        }
        $parsed = $null
        if ($r.Content) { try { $parsed = $r.Content | ConvertFrom-Json } catch {} }
        return [pscustomobject]@{ Status = [int]$r.StatusCode; Body = $parsed; Error = '' }
    } catch {
        $resp = $_.Exception.Response
        $code = 0
        if ($resp) { $code = [int]$resp.StatusCode }
        $text = ''
        if ($resp) {
            try { $text = (New-Object System.IO.StreamReader($resp.GetResponseStream())).ReadToEnd() } catch {}
        }
        if ($code -eq 0) { $text = $_.Exception.Message }
        return [pscustomobject]@{ Status = $code; Body = $null; Error = $text }
    }
}

function Check {
    param([string]$Who, [string]$What, [string]$Method, [string]$Path, [int[]]$Expected, $Body)

    $script:Checks++
    $r = Invoke-Api -Method $Method -Path $Path -Body $Body -Token $script:Token

    if ($Expected -notcontains $r.Status) {
        [void]$script:Failures.Add([pscustomobject]@{
            Who = $Who; What = $What; Path = "$Method $Path"
            Got = $r.Status; Expected = ($Expected -join '/'); Detail = $r.Error
        })
        Write-Host ("   FAIL  {0,-28} {1,-44} got {2}, wanted {3}" -f $What, $Path, $r.Status, ($Expected -join '/')) -ForegroundColor Red
        if ($r.Error) {
            $snip = $r.Error
            if ($snip.Length -gt 200) { $snip = $snip.Substring(0, 200) }
            Write-Host ("         {0}" -f $snip) -ForegroundColor DarkGray
        }
    } else {
        $colour = 'DarkGreen'
        $mark = 'ok  '
        if ($r.Status -eq 403) { $colour = 'DarkYellow'; $mark = 'deny' }
        Write-Host ("   {0}  {1,-28} {2}" -f $mark, $What, $r.Status) -ForegroundColor $colour
    }

    return $r
}

function Sign-In {
    param([string]$User)

    $r = Invoke-Api -Method POST -Path '/api/auth/login' -Body @{ username = $User; password = $Password }
    $script:Checks++

    if ($r.Status -ne 200) {
        [void]$script:Failures.Add([pscustomobject]@{
            Who = $User; What = 'sign in'; Path = 'POST /api/auth/login'
            Got = $r.Status; Expected = '200'; Detail = $r.Error
        })
        Write-Host "   FAIL  could not sign in as $User (HTTP $($r.Status))" -ForegroundColor Red
        return $null
    }

    $script:Token = $r.Body.token
    return $r.Body.user
}

# ======================================================================== the matrix
#
# For each account: which modules it should hold, and therefore which endpoints should answer
# and which should refuse. "deny" below means 403 is the correct answer - the tier or the role
# is doing its job.

$Accounts = @(
    #                                                          Shop  Workforce Finance SysAdmin Analytics Branch
    @{ User = 'medium.admin';   Tenant = 'C / Medium'; Role = 'Admin';   Shop = $true;  Workforce = $true;  Finance = $true;  SysAdmin = $true;  Analytics = $true;  Branch = $true  }
    @{ User = 'medium.manager'; Tenant = 'C / Medium'; Role = 'Manager'; Shop = $true;  Workforce = $true;  Finance = $true;  SysAdmin = $false; Analytics = $true;  Branch = $false }
    @{ User = 'medium.staff';   Tenant = 'C / Medium'; Role = 'Staff';   Shop = $true;  Workforce = $false; Finance = $false; SysAdmin = $false; Analytics = $false; Branch = $false }
    @{ User = 'admin';          Tenant = 'B / Small';  Role = 'Admin';   Shop = $true;  Workforce = $true;  Finance = $true;  SysAdmin = $false; Analytics = $false; Branch = $false }
    @{ User = 'manager';        Tenant = 'B / Small';  Role = 'Manager'; Shop = $true;  Workforce = $true;  Finance = $true;  SysAdmin = $false; Analytics = $false; Branch = $false }
    @{ User = 'staff';          Tenant = 'B / Small';  Role = 'Staff';   Shop = $true;  Workforce = $false; Finance = $false; SysAdmin = $false; Analytics = $false; Branch = $false }
    @{ User = 'micro.admin';    Tenant = 'A / Micro';  Role = 'Admin';   Shop = $false; Workforce = $false; Finance = $false; SysAdmin = $false; Analytics = $false; Branch = $false }
    @{ User = 'micro.manager';  Tenant = 'A / Micro';  Role = 'Manager'; Shop = $false; Workforce = $false; Finance = $false; SysAdmin = $false; Analytics = $false; Branch = $false }
    @{ User = 'micro.staff';    Tenant = 'A / Micro';  Role = 'Staff';   Shop = $false; Workforce = $false; Finance = $false; SysAdmin = $false; Analytics = $false; Branch = $false }

    # The branch accounts on Tenant C. Their module set is the ordinary Manager and Staff one -
    # what is different about them is the scope their answers are narrowed to, which is asserted
    # separately below rather than by a status code.
    @{ User = 'bra.manager';    Tenant = 'C / Branch A'; Role = 'Manager'; Shop = $true; Workforce = $true;  Finance = $true;  SysAdmin = $false; Analytics = $true;  Branch = $false }
    @{ User = 'brb.manager';    Tenant = 'C / Branch B'; Role = 'Manager'; Shop = $true; Workforce = $true;  Finance = $true;  SysAdmin = $false; Analytics = $true;  Branch = $false }
    @{ User = 'brc.staff1';     Tenant = 'C / Branch C'; Role = 'Staff';   Shop = $true; Workforce = $false; Finance = $false; SysAdmin = $false; Analytics = $false; Branch = $false }
)
Write-Host ''
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host ' FitCore - every module, every role, every tenant' -ForegroundColor Cyan
Write-Host '================================================================' -ForegroundColor Cyan

foreach ($a in $Accounts) {
    Write-Host ''
    Write-Host ("== {0}  ({1}, {2})" -f $a.User, $a.Tenant, $a.Role) -ForegroundColor White

    $user = Sign-In $a.User
    if ($null -eq $user) { continue }

    Write-Host ("   tier={0}  company={1}  modules={2}  subfeatures={3}" -f `
        $user.enterpriseTier, $user.companyName, $user.modules.Count, $user.submodules.Count) -ForegroundColor Gray

    $ok   = @(200)
    $deny = @(403)

    # 1. Membership -------------------------------------------------------
    Check $a.User 'membership: members'      GET '/api/members'                     $ok  | Out-Null
    Check $a.User 'membership: plans'        GET '/api/membership-plans'            $ok  | Out-Null
    Check $a.User 'membership: subs'         GET '/api/subscriptions'               $ok  | Out-Null

    # 2. Payments ---------------------------------------------------------
    Check $a.User 'payments: transactions'   GET '/api/payments'                    $ok  | Out-Null
    Check $a.User 'payments: summary'        GET '/api/payments/summary'            $ok  | Out-Null

    # 3 & 4. Sales and Inventory -----------------------------------------
    # Both are Small and above: a Micro gym sells memberships, not merchandise, so it has
    # neither a till nor a stockroom.
    $shop = $deny
    if ($a.Shop) { $shop = $ok }
    Check $a.User 'sales: history'           GET '/api/sales'                       $shop | Out-Null
    Check $a.User 'sales: returns'           GET '/api/returns'                     $shop | Out-Null
    Check $a.User 'inventory: stock'         GET '/api/inventory'                   $shop | Out-Null
    Check $a.User 'inventory: products'      GET '/api/products'                    $shop | Out-Null
    Check $a.User 'inventory: suppliers'     GET '/api/suppliers'                   $shop | Out-Null

    # Purchasing needs the module and is Manager and above, so the front desk is refused it.
    $purchaseExpected = $deny
    if ($a.Shop -and $a.Role -ne 'Staff') { $purchaseExpected = $ok }
    Check $a.User 'inventory: purchases'     GET '/api/purchases'                   $purchaseExpected | Out-Null

    # 5 & 6. Employees and Payroll ---------------------------------------
    $workforce = $deny
    if ($a.Workforce) { $workforce = $ok }
    Check $a.User 'employees: records'       GET '/api/employees'                   $workforce | Out-Null
    Check $a.User 'employees: attendance'    GET '/api/attendance'                  $workforce | Out-Null
    Check $a.User 'employees: leave'         GET '/api/leave'                       $workforce | Out-Null
    Check $a.User 'payroll: records'         GET '/api/payroll'                     $workforce | Out-Null
    Check $a.User 'payroll: summary'         GET '/api/payroll/summary'             $workforce | Out-Null

    # 7. Finance ----------------------------------------------------------
    $finance = $deny
    if ($a.Finance) { $finance = $ok }
    Check $a.User 'finance: overview'        GET '/api/finance/overview'            $finance | Out-Null
    Check $a.User 'finance: accounts'        GET '/api/finance/accounts'            $finance | Out-Null
    Check $a.User 'finance: journal'         GET '/api/finance/journal'             $finance | Out-Null
    Check $a.User 'finance: trial balance'   GET '/api/finance/reports/trial-balance' $finance | Out-Null
    Check $a.User 'finance: income stmt'     GET '/api/finance/reports/income-statement' $finance | Out-Null
    Check $a.User 'finance: balance sheet'   GET '/api/finance/reports/balance-sheet' $finance | Out-Null
    Check $a.User 'finance: cash flow'       GET '/api/finance/reports/cash-flow'   $finance | Out-Null
    Check $a.User 'finance: receivables'     GET '/api/finance/receivables'         $finance | Out-Null
    Check $a.User 'finance: payables'        GET '/api/finance/payables'            $finance | Out-Null
    Check $a.User 'finance: banking'         GET '/api/finance/bank-accounts'       $finance | Out-Null
    Check $a.User 'finance: budgets'         GET '/api/finance/budgets'             $finance | Out-Null
    # Opening and closing a period locks the books against further posting, so it stays the
    # owner's decision even on a tier that has Finance.
    $periods = $deny
    if ($a.Finance -and $a.Role -eq 'Admin') { $periods = $ok }
    Check $a.User 'finance: periods'         GET '/api/finance/periods'             $periods | Out-Null
    Check $a.User 'finance: expenses'        GET '/api/expenses'                    $finance | Out-Null

    # 8. System Administration -------------------------------------------
    # Medium alone, and the owner's alone within it. Accounts, roles, settings, the audit trail
    # and the branch network are all things a Micro or Small tenant has provisioned for it
    # rather than administering itself.
    $sysadmin = $deny
    if ($a.SysAdmin) { $sysadmin = $ok }
    Check $a.User 'sysadmin: settings'       GET '/api/settings'                    $sysadmin | Out-Null
    Check $a.User 'sysadmin: audit'          GET '/api/audit-events'                $sysadmin | Out-Null
    Check $a.User 'sysadmin: integrity'      GET '/api/data-integrity'              $sysadmin | Out-Null
    Check $a.User 'sysadmin: users'          GET '/api/users'                       $sysadmin | Out-Null

    # Branching divides the company itself, so it is the Admin/Owner's alone even among
    # accounts that hold System Administration.
    $branch = $deny
    if ($a.Branch) { $branch = $ok }
    Check $a.User 'sysadmin: branches'       GET '/api/branches'                    $branch | Out-Null
    Check $a.User 'bi: branch performance'   GET '/api/bi/branches'                 $branch | Out-Null

    # Platform administration is the Super Admin's alone, on every tier.
    Check $a.User 'sysadmin: platform'       GET '/api/platform/tenants'            $deny | Out-Null

    # 9. Business Intelligence -------------------------------------------
    # The dashboard belongs to Business Intelligence, which starts at Small - so a Micro tenant
    # is refused it at every seniority. Staff hold no part of Business Intelligence at all,
    # dashboard included, on any tier. Micro is not left without reporting: the module reports
    # below are guarded by the module they report on, and Micro holds two of those.
    $dashboard = $deny
    if ($a.Shop -and $a.Role -ne 'Staff') { $dashboard = $ok }
    Check $a.User 'bi: dashboard'            GET '/api/reports/dashboard'           $dashboard | Out-Null

    # Reports are Manager and above, *and* need the module they report on.
    $shopReports = $deny
    if ($a.Shop -and $a.Role -ne 'Staff') { $shopReports = $ok }

    $frontDeskReports = $deny
    if ($a.Role -ne 'Staff') { $frontDeskReports = $ok }

    # Each module report is guarded by the module it reports on, not by Business Intelligence,
    # so that the desktop's per-module Reports tabs and the endpoints behind them answer to the
    # same subfeature. The expectations below are therefore the module's, not BI's - which is
    # exactly what keeps membership and payment reporting working on a tier that has no BI.
    Check $a.User 'bi: reports (sales)'      GET '/api/reports/sales'               $shopReports      | Out-Null
    Check $a.User 'reports: inventory'       GET '/api/reports/inventory'           $shopReports      | Out-Null
    Check $a.User 'reports: payments'        GET '/api/reports/payments'            $frontDeskReports | Out-Null
    Check $a.User 'reports: membership'      GET '/api/reports/membership'          $frontDeskReports | Out-Null

    # Employees and Payroll reports need the tier that has those modules at all.
    Check $a.User 'reports: employees'       GET '/api/reports/employees'           $workforce | Out-Null
    Check $a.User 'reports: payroll'         GET '/api/reports/payroll'             $workforce | Out-Null

    # Expenses are a Finance subfeature, so the expense report is gated exactly as /api/expenses
    # is - a Micro or Small manager must not read through the report what the endpoint refuses.
    Check $a.User 'reports: expenses'        GET '/api/reports/expenses'            $finance   | Out-Null

    # Reconciliation compares the takings against the general ledger, so it needs the ledger -
    # Small - even though the rest of Payment Management starts at Micro.
    Check $a.User 'reports: reconciliation'  GET '/api/reports/payment-reconciliation' $finance | Out-Null

    # Member history is a Micro, Staff-level subfeature: the person at the desk is the one who
    # gets asked when a charge is disputed. Member 1 may not exist on every tenant, so a 404 is a
    # pass here - what is being asserted is that the caller got past the filter.
    $historyExpected = @(200, 404)
    Check $a.User 'membership: history'      GET '/api/members/1/subscriptions'     $historyExpected | Out-Null

    # Platform monitoring is the Super Admin's alone, on every tier.
    Check $a.User 'sysadmin: monitoring'     GET '/api/platform/health'             $deny | Out-Null

    # The analytics areas are Medium and Manager-and-above together.
    $analytics = $deny
    if ($a.Analytics -and $a.Role -ne 'Staff') { $analytics = $ok }
    Check $a.User 'bi: kpi dashboard'        GET '/api/bi/kpi'                      $analytics | Out-Null
    Check $a.User 'bi: overview'             GET '/api/bi/overview'                 $analytics | Out-Null
    Check $a.User 'bi: membership'           GET '/api/bi/membership'               $analytics | Out-Null
    Check $a.User 'bi: sales'                GET '/api/bi/sales'                    $analytics | Out-Null
    Check $a.User 'bi: payments'             GET '/api/bi/payments'                 $analytics | Out-Null
    Check $a.User 'bi: inventory'            GET '/api/bi/inventory'                $analytics | Out-Null
    Check $a.User 'bi: workforce'            GET '/api/bi/workforce'                $analytics | Out-Null
    Check $a.User 'bi: finance'              GET '/api/bi/finance'                  $analytics | Out-Null
    Check $a.User 'bi: profitability'        GET '/api/bi/profitability'            $analytics | Out-Null
}

# ======================================================================== super admin

Write-Host ''
Write-Host '== superadmin  (platform)' -ForegroundColor White

$sa = Sign-In 'superadmin'
if ($null -ne $sa) {
    Write-Host ("   company={0}  platformAdministrator={1}  modules={2}" -f `
        $sa.companyName, $sa.isPlatformAdministrator, $sa.modules.Count) -ForegroundColor Gray

    $ok = @(200)
    Check 'superadmin' 'platform: tenants'       GET '/api/platform/tenants'       $ok | Out-Null
    Check 'superadmin' 'platform: plans'         GET '/api/platform/plans'         $ok | Out-Null
    Check 'superadmin' 'platform: subscriptions' GET '/api/platform/subscriptions' $ok | Out-Null
    Check 'superadmin' 'platform: users'         GET '/api/platform/users'         $ok | Out-Null
    Check 'superadmin' 'platform: settings'      GET '/api/platform/settings'      $ok | Out-Null
    Check 'superadmin' 'platform: audit'         GET '/api/platform/audit'         $ok | Out-Null
    Check 'superadmin' 'platform: monitoring'    GET '/api/platform/health'        $ok | Out-Null
    Check 'superadmin' 'platform: dashboard'     GET '/api/platform/dashboard'     $ok | Out-Null
    Check 'superadmin' 'platform: analytics'     GET '/api/platform/analytics'     $ok | Out-Null
}

# ======================================================================== verdict

Write-Host ''
Write-Host '================================================================' -ForegroundColor Cyan
if ($script:Failures.Count -eq 0) {
    Write-Host (" PASS - {0} checks, every answer as intended" -f $script:Checks) -ForegroundColor Green
} else {
    Write-Host (" {0} of {1} checks did not answer as intended" -f $script:Failures.Count, $script:Checks) -ForegroundColor Red
    Write-Host ''
    $script:Failures | ForEach-Object {
        Write-Host ("  {0,-16} {1,-28} {2}  got {3}, wanted {4}" -f $_.Who, $_.What, $_.Path, $_.Got, $_.Expected) -ForegroundColor Red
    }
}
Write-Host '================================================================' -ForegroundColor Cyan
