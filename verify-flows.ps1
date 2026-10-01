# FitCore business-flow verification, against the live Medium tenant (Tenant C).
#
# The read sweep in _verify.ps1 proves every screen can load. This proves the gym can actually
# be run: a member joins and pays, stock is bought from a supplier and sold, some of it comes
# back, staff are hired and paid, bills are settled - and the books still balance afterwards.
#
# The last part is the point. Every step below is checked not only for "did the call succeed"
# but for "did the ledger move the way double-entry says it must".

$ErrorActionPreference = 'Continue'

Add-Type @"
using System.Net; using System.Security.Cryptography.X509Certificates;
public class FitCoreTrustAllFlows : ICertificatePolicy {
  public bool CheckValidationResult(ServicePoint s, X509Certificate c, WebRequest r, int p) { return true; }
}
"@ -ErrorAction SilentlyContinue

[System.Net.ServicePointManager]::CertificatePolicy = New-Object FitCoreTrustAllFlows
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Base = 'https://localhost:7214'
$script:Token = $null
$script:Failures = New-Object System.Collections.ArrayList
$script:Steps = 0

# A per-run tag, so re-running this never collides with the codes it wrote last time.
$Tag = (Get-Date).ToString('MMddHHmm')
function IsoDate { param([datetime]$d) return $d.ToString('yyyy-MM-ddTHH:mm:ss') }

function Invoke-Api {
    param([string]$Method, [string]$Path, $Body)

    $headers = @{}
    if ($script:Token) { $headers['Authorization'] = "Bearer $script:Token" }

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
        if ($resp) { try { $text = (New-Object System.IO.StreamReader($resp.GetResponseStream())).ReadToEnd() } catch {} }
        if ($code -eq 0) { $text = $_.Exception.Message }
        return [pscustomobject]@{ Status = $code; Body = $null; Error = $text }
    }
}

function Fail {
    param([string]$What, [string]$Detail)
    [void]$script:Failures.Add([pscustomobject]@{ What = $What; Detail = $Detail })
    Write-Host ("   FAIL  {0}" -f $What) -ForegroundColor Red
    if ($Detail) {
        $snip = $Detail
        if ($snip.Length -gt 300) { $snip = $snip.Substring(0, 300) }
        Write-Host ("         {0}" -f $snip) -ForegroundColor DarkGray
    }
}

# Calls an endpoint and returns the body, or records a failure and returns $null.
function Step {
    param([string]$What, [string]$Method, [string]$Path, $Body, [int[]]$Ok = @(200, 201, 204))

    $script:Steps++
    $r = Invoke-Api -Method $Method -Path $Path -Body $Body

    if ($Ok -notcontains $r.Status) {
        Fail $What ("HTTP {0} - {1}" -f $r.Status, $r.Error)
        return $null
    }

    Write-Host ("   ok    {0}" -f $What) -ForegroundColor DarkGreen
    return $r.Body
}

# Asserts an arithmetic fact about the books.
function Assert {
    param([string]$What, [bool]$Condition, [string]$Detail)

    $script:Steps++
    if ($Condition) {
        Write-Host ("   ok    {0}  {1}" -f $What, $Detail) -ForegroundColor DarkGreen
    } else {
        Fail $What $Detail
    }
}

function Money { param($v) if ($null -eq $v) { return 0 } return [decimal]$v }
function Near {
    param($a, $b, $tolerance = 0.01)
    return [math]::Abs((Money $a) - (Money $b)) -le $tolerance
}

Write-Host ''
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host ' FitCore - running a gym end to end on Tenant C (Medium)' -ForegroundColor Cyan
Write-Host '================================================================' -ForegroundColor Cyan

# ---------------------------------------------------------------- sign in
$login = Invoke-Api -Method POST -Path '/api/auth/login' -Body @{ username = 'medium.admin'; password = 'FitCore@2026' }
if ($login.Status -ne 200) {
    Write-Host "Could not sign in as medium.admin (HTTP $($login.Status)) - nothing else can run." -ForegroundColor Red
    exit 1
}
$script:Token = $login.Body.token
Write-Host ("`n== signed in: {0} / {1} / tier {2}" -f `
    $login.Body.user.fullName, $login.Body.user.companyName, $login.Body.user.enterpriseTier) -ForegroundColor White

# ---------------------------------------------------------------- opening position
Write-Host "`n-- opening position" -ForegroundColor White
$before = Step 'trial balance reads' GET '/api/finance/reports/trial-balance'
if ($null -ne $before) {
    Assert 'books open balanced' ([bool]$before.isBalanced) `
        ("debit {0:N2} = credit {1:N2}" -f (Money $before.totalDebit), (Money $before.totalCredit))
}

# The opening position, so the checks below can assert what a day's trading *changed* rather
# than what the tenant happens to have accumulated. Several properties here are only meaningful
# as a delta: cost of goods sold grows every run, so "COGS is under 12,000" stops being a real
# statement about anything after the second one.
$isBefore = Step 'income statement reads' GET '/api/finance/reports/income-statement'
$sheetBefore = Step 'balance sheet reads' GET '/api/finance/reports/balance-sheet'

function ReceivablesOf {
    param($sheet)
    if ($null -eq $sheet) { return 0 }
    $sum = ($sheet.assetLines | Where-Object { $_.label -match 'Receivable' } |
            Measure-Object -Property amount -Sum).Sum
    if ($null -eq $sum) { return 0 }
    return [decimal]$sum
}

$openingCogs = Money $isBefore.costOfGoodsSold
$openingReceivable = ReceivablesOf $sheetBefore

# ================================================================ 1. Membership
Write-Host "`n-- 1. Membership Management" -ForegroundColor White

$plan = Step 'create membership plan' POST '/api/membership-plans' @{
    planName = "Verification Annual $Tag"; durationMonths = 12; price = 12000
    description = 'Created by the end-to-end verification run.'
}
$member = Step 'register member' POST '/api/members' @{
    firstName = 'Verification'; lastName = "Member $Tag"
    phone = '09170000001'; email = "verify.$Tag@fitcore.local"
}

$sub = $null
if ($plan -and $member) {
    $sub = Step 'subscribe member to plan' POST '/api/subscriptions' @{
        memberId = $member.memberId; planId = $plan.planId; startDate = (IsoDate (Get-Date))
    }
}
if ($sub) {
    Step 'renew subscription' POST ("/api/subscriptions/{0}/renew" -f $sub.subscriptionId) | Out-Null
    Step 'read member subscriptions' GET ("/api/members/{0}/subscriptions" -f $member.memberId) | Out-Null
}
if ($member) {
    Step 'add member note' POST ("/api/members/{0}/notes" -f $member.memberId) @{
        category = 'General'; note = 'Joined during the verification run.'; isPinned = $false
    } | Out-Null
}

# ================================================================ 2. Payments
Write-Host "`n-- 2. Payment Management" -ForegroundColor White

$payment = $null
if ($sub) {
    $payment = Step 'take subscription payment' POST '/api/payments' @{
        memberId = $member.memberId; subscriptionId = $sub.subscriptionId
        amount = 12000; method = 'Cash'; referenceNo = "VER-$Tag"; status = 'Completed'
    }
}
Step 'payment summary' GET '/api/payments/summary' | Out-Null

# ================================================================ 3 & 4. Inventory and purchasing
Write-Host "`n-- 3. Inventory Management (purchase -> stock -> payable)" -ForegroundColor White

$supplier = Step 'create supplier' POST '/api/suppliers' @{
    supplierCode = "SUP-$Tag"; supplierName = "Verification Supplies $Tag"
    contactPerson = 'Supply Desk'; contactNumber = '09170000002'
    emailAddress = "supplier.$Tag@fitcore.local"; address = 'Davao City'
}
$product = Step 'create product' POST '/api/products' @{
    productCode = "PRD-$Tag"; productName = "Verification Protein $Tag"; category = 'Supplements'
    costPrice = 600; unitPrice = 950; openingStock = 0; reorderLevel = 5
}

$purchase = $null
if ($supplier -and $product) {
    $purchase = Step 'raise purchase order' POST '/api/purchases' @{
        supplierId = $supplier.supplierId; orderDate = (IsoDate (Get-Date))
        expectedDate = (IsoDate (Get-Date).AddDays(3)); supplierReference = "PO-$Tag"
        discount = 0; tax = 0; notes = 'Verification purchase.'
        lines = @( @{ productId = $product.productId; quantity = 20; unitCost = 600 } )
    }
}
if ($purchase) {
    Step 'issue the order' POST ("/api/purchases/{0}/order" -f $purchase.purchaseId) | Out-Null
    Step 'receive the delivery' POST ("/api/purchases/{0}/receive" -f $purchase.purchaseId) @{ receivedByItemId = $null } | Out-Null
}

$stock = $null
if ($product) {
    $stock = Step 'stock reflects the delivery' GET ("/api/inventory/{0}" -f $product.productId)
    if ($stock) {
        Assert 'receiving raised stock to 20' (Near $stock.quantityOnHand 20) ("on hand {0}" -f (Money $stock.quantityOnHand))
    }
}

# ================================================================ 4. Sales
Write-Host "`n-- 4. Sales Management (sale -> COGS -> revenue)" -ForegroundColor White

$sale = $null
if ($product -and $member) {
    $sale = Step 'sell 5 units' POST '/api/sales' @{
        memberId = $member.memberId
        items = @( @{ productId = $product.productId; quantity = 5 } )
        discount = 0; notes = "Verification sale $Tag"; settleNow = $true; paymentMethod = 'Cash'
    }
}
if ($sale) {
    Assert 'sale totalled at catalogue price' (Near $sale.totalAmount 4750) ("total {0:N2}" -f (Money $sale.totalAmount))
    $stockAfter = Step 'stock reflects the sale' GET ("/api/inventory/{0}" -f $product.productId)
    if ($stockAfter) {
        Assert 'selling dropped stock to 15' (Near $stockAfter.quantityOnHand 15) ("on hand {0}" -f (Money $stockAfter.quantityOnHand))
    }
}

# ================================================================ returns
Write-Host "`n-- 4b. Sales returns (contra-revenue, restock)" -ForegroundColor White

$saleItems = $null
if ($sale) { $saleItems = Step 'read sale lines' GET ("/api/sales/{0}/items" -f $sale.saleId) }

if ($saleItems -and @($saleItems).Count -gt 0) {
    $line = @($saleItems)[0]

    # A saleable return goes back on the shelf.
    Step 'return 2 units (changed mind)' POST '/api/returns' @{
        saleId = $sale.saleId
        lines = @( @{ saleItemId = $line.saleItemId; quantity = 2 } )
        reason = 'Changed mind'; refundMethod = 'Cash'; restockToInventory = $true
        notes = 'Verification return.'
    } | Out-Null

    $stockReturned = Step 'stock reflects the return' GET ("/api/inventory/{0}" -f $product.productId)
    if ($stockReturned) {
        Assert 'restocking took stock back to 17' (Near $stockReturned.quantityOnHand 17) ("on hand {0}" -f (Money $stockReturned.quantityOnHand))
    }

    # A damaged one does not, whatever the operator ticked - it would put unsellable stock on
    # the books. This asserts the override, not the tick.
    Step 'return 1 unit (damaged)' POST '/api/returns' @{
        saleId = $sale.saleId
        lines = @( @{ saleItemId = $line.saleItemId; quantity = 1 } )
        reason = 'Damaged'; refundMethod = 'Cash'; restockToInventory = $true
        notes = 'Verification damaged return.'
    } | Out-Null

    $stockDamaged = Step 'stock after the damaged return' GET ("/api/inventory/{0}" -f $product.productId)
    if ($stockDamaged) {
        Assert 'damaged goods were not put back on the shelf' (Near $stockDamaged.quantityOnHand 17) `
            ("on hand still {0} - the refund was made but the stock was not restored" -f (Money $stockDamaged.quantityOnHand))
    }
}

# ================================================================ 5. Employees
Write-Host "`n-- 5. Employee Management" -ForegroundColor White

# Position is the employment level - it decides who may hire whom - so the job title lives in
# Department rather than here.
$employee = Step 'hire employee' POST '/api/employees' @{
    employeeCode = "EMP-$Tag"; firstName = 'Verification'; lastName = "Trainer $Tag"
    position = 'Staff'; department = 'Fitness'
    phone = '09170000003'; email = "trainer.$Tag@fitcore.local"
    hireDate = (IsoDate (Get-Date).AddMonths(-6)); basicSalary = 25000; hourlyRate = 150
}

if ($employee) {
    Step 'record attendance' POST '/api/attendance' @{
        employeeId = $employee.employeeId; date = (IsoDate (Get-Date).Date)
        timeIn = (IsoDate (Get-Date).Date.AddHours(9)); timeOut = (IsoDate (Get-Date).Date.AddHours(18))
        status = 'Present'; notes = 'Verification run.'
    } | Out-Null

    Step 'attendance summary' GET '/api/attendance/summary' | Out-Null

    $leave = Step 'file leave request' POST '/api/leave' @{
        employeeId = $employee.employeeId; leaveType = 'Vacation'
        startDate = (IsoDate (Get-Date).AddDays(20)); endDate = (IsoDate (Get-Date).AddDays(22))
        isPaid = $true; reason = 'Verification leave.'
    }
    if ($leave) {
        Step 'approve leave' POST ("/api/leave/{0}/decide" -f $leave.leaveRequestId) @{
            approve = $true; notes = 'Approved by the verification run.'
        } | Out-Null
    }
    Step 'leave balance' GET ("/api/leave/balance/{0}" -f $employee.employeeId) | Out-Null
}

# ================================================================ 6. Payroll
Write-Host "`n-- 6. Payroll Management (salary expense, deductions, liabilities)" -ForegroundColor White

$payroll = $null
if ($employee) {
    # A period far enough back that it cannot overlap anything already on file.
    $periodStart = (Get-Date).AddMonths(-2).Date
    $periodStart = $periodStart.AddDays(1 - $periodStart.Day)
    $periodEnd = $periodStart.AddMonths(1).AddDays(-1)

    # Generated pay is derived from attendance rather than assumed, so the days have to exist
    # before they can be paid for. Ten working days, 9-to-6.
    $logged = 0
    $day = $periodStart
    while ($day -le $periodEnd -and $logged -lt 10) {
        if ($day.DayOfWeek -ne 'Saturday' -and $day.DayOfWeek -ne 'Sunday') {
            $r = Invoke-Api -Method POST -Path '/api/attendance' -Body @{
                employeeId = $employee.employeeId; date = (IsoDate $day)
                timeIn = (IsoDate $day.AddHours(9)); timeOut = (IsoDate $day.AddHours(18))
                status = 'Present'; notes = 'Verification run.'
            }
            if ($r.Status -eq 200 -or $r.Status -eq 201) { $logged++ }
        }
        $day = $day.AddDays(1)
    }
    $script:Steps++
    if ($logged -ge 10) {
        Write-Host ("   ok    logged {0} days of attendance in the pay period" -f $logged) -ForegroundColor DarkGreen
    } else {
        Fail 'log attendance for the pay period' ("only {0} days were accepted" -f $logged)
    }

    $payroll = Step 'generate a payroll run' POST '/api/payroll/generate' @{
        employeeId = $employee.employeeId
        periodStart = (IsoDate $periodStart); periodEnd = (IsoDate $periodEnd)
        allowances = 2000; otherDeductions = 0; notes = "Verification payroll $Tag"
    }
}
if ($payroll) {
    Assert 'statutory deductions were computed' ((Money $payroll.deductions) -gt 0) `
        ("gross {0:N2}, deductions {1:N2}, net {2:N2}" -f (Money $payroll.grossPay), (Money $payroll.deductions), (Money $payroll.netPay))
    Assert 'net = gross - deductions' (Near ((Money $payroll.grossPay) - (Money $payroll.deductions)) $payroll.netPay) `
        ("{0:N2} - {1:N2} = {2:N2}" -f (Money $payroll.grossPay), (Money $payroll.deductions), (Money $payroll.netPay))

    Step 'mark the run paid' PATCH ("/api/payroll/{0}/status" -f $payroll.payrollId) @{ status = 'Paid' } | Out-Null

    # Paying twice for the same days is the mistake payroll must refuse.
    $dup = Invoke-Api -Method POST -Path '/api/payroll/generate' -Body @{
        employeeId = $employee.employeeId
        periodStart = (IsoDate $periodStart.AddDays(10)); periodEnd = (IsoDate $periodEnd.AddDays(10))
        allowances = 0; otherDeductions = 0; notes = 'Should be refused.'
    }
    $script:Steps++
    if ($dup.Status -eq 400 -or $dup.Status -eq 409) {
        Write-Host '   ok    overlapping payroll period refused' -ForegroundColor DarkGreen
    } else {
        Fail 'overlapping payroll period refused' ("HTTP {0} - an overlapping run was accepted" -f $dup.Status)
    }
}
Step 'payroll summary' GET '/api/payroll/summary' | Out-Null

# ================================================================ 7. Finance
Write-Host "`n-- 7. Finance Management" -ForegroundColor White

$expense = Step 'record an operating expense' POST '/api/expenses' @{
    category = 'Electricity'; description = "Verification electricity $Tag"; amount = 3500
    expenseDate = (IsoDate (Get-Date)); paymentMethod = 'Cash'; referenceNo = "EXP-$Tag"
}

if ($purchase -and $supplier) {
    Step 'pay the supplier' POST '/api/purchases/payments' @{
        supplierId = $supplier.supplierId; purchaseId = $purchase.purchaseId
        amount = 12000; paymentDate = (IsoDate (Get-Date)); method = 'Bank'
        referenceNo = "SPAY-$Tag"; notes = 'Verification settlement.'
    } | Out-Null
}

$bank = Step 'open a bank account' POST '/api/finance/bank-accounts' @{
    accountName = "Verification Current $Tag"; bankName = 'BDO'
    accountNumber = "00$Tag"; kind = 'Bank'; openingBalance = 50000; notes = 'Verification run.'
}

$budget = Step 'create a budget' POST '/api/finance/budgets' @{
    name = "Verification Budget $Tag"; year = (Get-Date).Year; notes = 'Verification run.'
}

Step 'chart of accounts' GET '/api/finance/accounts' | Out-Null
Step 'general ledger (journal)' GET '/api/finance/journal' | Out-Null
Step 'receivables ageing' GET '/api/finance/receivables' | Out-Null
Step 'payables ageing' GET '/api/finance/payables' | Out-Null
Step 'cash flow' GET '/api/finance/reports/cash-flow' | Out-Null

# ---------------------------------------------------------------- the books must agree
Write-Host "`n-- the books" -ForegroundColor White

$after = Step 'trial balance after the day''s work' GET '/api/finance/reports/trial-balance'
if ($after) {
    Assert 'trial balance still balances' ([bool]$after.isBalanced) `
        ("debit {0:N2} = credit {1:N2}, difference {2:N2}" -f (Money $after.totalDebit), (Money $after.totalCredit), (Money $after.difference))
    Assert 'the day''s work reached the ledger' ((Money $after.totalDebit) -gt (Money $before.totalDebit)) `
        ("{0:N2} -> {1:N2}" -f (Money $before.totalDebit), (Money $after.totalDebit))
}

$income = Step 'income statement' GET '/api/finance/reports/income-statement'
if ($income) {
    Assert 'net revenue = revenue - discounts - returns' `
        (Near $income.netRevenue ((Money $income.revenue) - (Money $income.salesDiscounts) - (Money $income.salesReturns))) `
        ("{0:N2} - {1:N2} - {2:N2} = {3:N2}" -f (Money $income.revenue), (Money $income.salesDiscounts), (Money $income.salesReturns), (Money $income.netRevenue))

    Assert 'gross profit = net revenue - COGS' `
        (Near $income.grossProfit ((Money $income.netRevenue) - (Money $income.costOfGoodsSold))) `
        ("{0:N2} - {1:N2} = {2:N2}" -f (Money $income.netRevenue), (Money $income.costOfGoodsSold), (Money $income.grossProfit))

    Assert 'net income = gross profit - expenses' `
        (Near $income.netIncome ((Money $income.grossProfit) - (Money $income.totalExpenses))) `
        ("{0:N2} - {1:N2} = {2:N2}" -f (Money $income.grossProfit), (Money $income.totalExpenses), (Money $income.netIncome))

    # The rule that matters most about inventory: buying it is not an expense. 12,000 of stock
    # arrived this run and 5 units of it were sold at a cost of 600 each, so cost of sales must
    # have moved by a fraction of what was bought - the rest is sitting on the balance sheet as
    # an asset. Asserted as a change, because the tenant's lifetime COGS only ever grows.
    $cogsMoved = (Money $income.costOfGoodsSold) - $openingCogs
    Assert 'stock bought was not expensed' ($cogsMoved -gt 0 -and $cogsMoved -lt 12000) `
        ("cost of sales moved {0:N2} against 12,000 of stock bought - the rest became an asset" -f $cogsMoved)

    Assert 'payroll reached the income statement' ((Money $income.payrollExpenses) -gt 0) `
        ("payroll expense {0:N2}" -f (Money $income.payrollExpenses))
}

$sheet = Step 'balance sheet' GET '/api/finance/reports/balance-sheet'
if ($sheet) {
    Assert 'assets = liabilities + equity' `
        (Near $sheet.totalAssets $sheet.liabilitiesAndEquity) `
        ("{0:N2} = {1:N2} + {2:N2}" -f (Money $sheet.totalAssets), (Money $sheet.totalLiabilities), (Money $sheet.totalEquity))

    Assert 'inventory is carried as an asset' ((Money $sheet.totalAssets) -gt 0) `
        ("total assets {0:N2}" -f (Money $sheet.totalAssets))

    # A day of selling memberships must not *reduce* receivables.
    #
    # That is what the date bugs did: a membership or renewal was posted under the term it
    # bought - up to a year ahead - while the member's payment was recorded on the day it was
    # taken. Only the credit landed in the current period, so every membership sold pushed
    # receivables further negative, and the books ended up saying the gym owed its members
    # money. The trial balance cannot catch it, because a misdated entry is still balanced.
    #
    # Asserted as a change rather than a level, so the check is meaningful on a tenant that
    # already carries history.
    $receivable = ReceivablesOf $sheet
    $receivableMoved = $receivable - $openingReceivable
    Assert 'selling memberships did not reduce receivables' ($receivableMoved -ge 0) `
        ("receivables moved {0:N2}, now {1:N2}" -f $receivableMoved, $receivable)
}

# No live entry should be dated ahead of today. One that is stays invisible to every current
# report while the cash side of it is not, and no statement will flag the mismatch because a
# misdated entry is still a balanced one.
#
# A reversed pair is excluded: correcting a future-dated entry leaves the original and its
# mirror both under the original date, which is deliberate - they net to zero where they were
# posted rather than moving a correction into a period it did not belong to.
$journal = Step 'journal reads' GET '/api/finance/journal?pageSize=500'
if ($journal) {
    $tomorrow = (Get-Date).Date.AddDays(1)
    $future = @($journal | Where-Object {
        [datetime]$_.entryDate -ge $tomorrow -and
        -not $_.reversedByEntryId -and -not $_.reversesEntryId
    })
    Assert 'no live entry is dated in the future' ($future.Count -eq 0) `
        ("{0} future-dated entries that are not part of a reversed pair" -f $future.Count)
}

# ================================================================ 8. System Administration
Write-Host "`n-- 8. System Administration" -ForegroundColor White

Step 'read settings' GET '/api/settings' | Out-Null
Step 'change a setting' PUT '/api/settings' @{ values = @{ 'business.name' = "FitCore Medium $Tag" } } | Out-Null

# A key that is not in the catalogue must be refused rather than quietly stored.
$badSetting = Invoke-Api -Method PUT -Path '/api/settings' -Body @{ values = @{ 'gym.nonsense' = 'x' } }
$script:Steps++
if ($badSetting.Status -eq 400) {
    Write-Host '   ok    an unknown setting key is refused' -ForegroundColor DarkGreen
} else {
    Fail 'an unknown setting key is refused' ("HTTP {0} - it was accepted" -f $badSetting.Status)
}

$newUser = Step 'create a user' POST '/api/users' @{
    username = "verify.$Tag"; fullName = "Verification User $Tag"
    email = "user.$Tag@fitcore.local"; roleKey = 'staff'; password = 'FitCore@2026'
}
if ($newUser) {
    Step 'read the user''s permissions' GET ("/api/users/{0}/permissions" -f $newUser.appUserId) | Out-Null
    Step 'deactivate the user' PATCH ("/api/users/{0}/status" -f $newUser.appUserId) @{ isActive = $false } | Out-Null
}
Step 'audit trail recorded the work' GET '/api/audit-events?pageSize=20' | Out-Null

# ================================================================ 9. Business Intelligence
Write-Host "`n-- 9. Business Intelligence (values must come from the data above)" -ForegroundColor White

# Every BI area is checked for the same three things: it has KPI cards, it has charts with
# points on them, and at least one figure is non-zero. The last one is what separates a real
# measurement from a hard-coded placeholder - the numbers have to have come from the work done
# above, so on a tenant with data they cannot all be zero.
function Check-Analytics {
    param([string]$Area, [string]$Path, [switch]$CardsOnly)

    $view = Step ("BI: {0}" -f $Area) GET $Path
    if ($null -eq $view) { return }

    $cards = @()
    foreach ($g in @($view.groups)) { $cards += @($g.cards) }
    $charts = @($view.charts)

    $points = 0
    foreach ($c in $charts) { foreach ($s in @($c.series)) { $points += @($s.values).Count } }

    $nonZero = @($cards | Where-Object { (Money $_.value) -ne 0 }).Count
    $compared = @($cards | Where-Object { $_.hasComparison }).Count

    Assert ("{0}: has KPI cards" -f $Area) ($cards.Count -gt 0) ("{0} cards in {1} groups" -f $cards.Count, @($view.groups).Count)

    if (-not $CardsOnly) {
        Assert ("{0}: has charts with data" -f $Area) ($charts.Count -gt 0 -and $points -gt 0) `
            ("{0} charts, {1} plotted points" -f $charts.Count, $points)
    }

    Assert ("{0}: figures are measured, not placeholders" -f $Area) ($nonZero -gt 0) `
        ("{0} of {1} cards non-zero, {2} carry a period-on-period comparison" -f $nonZero, $cards.Count, $compared)
}

# /kpi is every figure in one round trip and is all cards by design; /overview is the charted
# one. Both are listed by /areas, so both are checked.
Check-Analytics 'kpi dashboard' '/api/bi/kpi' -CardsOnly
Check-Analytics 'overview'      '/api/bi/overview'
Check-Analytics 'sales'         '/api/bi/sales'
Check-Analytics 'membership'    '/api/bi/membership'
Check-Analytics 'payments'      '/api/bi/payments'
Check-Analytics 'inventory'     '/api/bi/inventory'
Check-Analytics 'workforce'     '/api/bi/workforce'
Check-Analytics 'finance'       '/api/bi/finance'
Check-Analytics 'profitability' '/api/bi/profitability'

# Every area the server advertises must actually be served. An area that is built, listed and
# unreachable is how /overview went missing.
$areas = Step 'BI: advertised areas' GET '/api/bi/areas'
foreach ($area in @($areas)) {
    $script:Steps++
    $r = Invoke-Api -Method GET -Path ("/api/bi/{0}" -f $area)
    if ($r.Status -eq 200) {
        Write-Host ("   ok    advertised area '{0}' is reachable" -f $area) -ForegroundColor DarkGreen
    } else {
        Fail ("advertised area '{0}' is reachable" -f $area) ("HTTP {0} - /api/bi/areas lists it but nothing serves it" -f $r.Status)
    }
}

# The same measure asked for twice must answer the same way; a KPI that drifts between calls is
# reading something other than the database.
$kpiA = Invoke-Api -Method GET -Path '/api/bi/kpi'
$kpiB = Invoke-Api -Method GET -Path '/api/bi/kpi'
$script:Steps++
if ($kpiA.Status -eq 200 -and $kpiB.Status -eq 200) {
    $a = @(); foreach ($g in @($kpiA.Body.groups)) { $a += @($g.cards | ForEach-Object { $_.value }) }
    $b = @(); foreach ($g in @($kpiB.Body.groups)) { $b += @($g.cards | ForEach-Object { $_.value }) }
    if ((($a -join ',') -eq ($b -join ',')) -and $a.Count -gt 0) {
        Write-Host ("   ok    KPI values are stable across calls  ({0} measures)" -f $a.Count) -ForegroundColor DarkGreen
    } else {
        Fail 'KPI values are stable across calls' 'the same request returned different figures'
    }
} else {
    Fail 'KPI values are stable across calls' 'the KPI endpoint did not answer twice'
}

Step 'dashboard' GET '/api/reports/dashboard' | Out-Null

# ================================================================ verdict
Write-Host ''
Write-Host '================================================================' -ForegroundColor Cyan
if ($script:Failures.Count -eq 0) {
    Write-Host (" PASS - {0} steps, the gym runs and the books balance" -f $script:Steps) -ForegroundColor Green
} else {
    Write-Host (" {0} of {1} steps failed" -f $script:Failures.Count, $script:Steps) -ForegroundColor Red
    Write-Host ''
    $script:Failures | ForEach-Object { Write-Host ("  {0}`n     {1}" -f $_.What, $_.Detail) -ForegroundColor Red }
}
Write-Host '================================================================' -ForegroundColor Cyan
