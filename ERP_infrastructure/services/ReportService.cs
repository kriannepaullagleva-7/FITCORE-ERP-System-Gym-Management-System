using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    public class ReportService : IReportService
    {
        private const int TopProductCount = 10;

        private readonly TenantErpDbContext _context;
        private readonly ISaleService _saleService;
        private readonly IPaymentService _paymentService;
        private readonly IInventoryService _inventoryService;
        private readonly IMemberService _memberService;
        private readonly IExpenseService _expenseService;
        private readonly ISubscriptionService _subscriptionService;

        public ReportService(
            TenantErpDbContext context,
            ISaleService saleService,
            IPaymentService paymentService,
            IInventoryService inventoryService,
            IMemberService memberService,
            IExpenseService expenseService,
            ISubscriptionService subscriptionService)
        {
            _context = context;
            _saleService = saleService;
            _paymentService = paymentService;
            _inventoryService = inventoryService;
            _memberService = memberService;
            _expenseService = expenseService;
            _subscriptionService = subscriptionService;
        }

        // ------------------------------------------------------------------ Sales

        public async Task<SalesReport> GetSalesReportAsync(DateTime? from, DateTime? to)
        {
            var range = ReportRange.Resolve(from, to);

            var report = new SalesReport { FromUtc = range.FromUtc, ToUtc = range.ToUtc };

            var sales = await _saleService.GetSalesInRangeAsync(range.FromUtc, range.ToUtc);

            var live = sales.Where(s => s.Status != "Cancelled").ToList();

            report.Sales = sales;
            report.TransactionCount = live.Count;
            report.CancelledCount = sales.Count - live.Count;
            report.GrossSales = live.Sum(s => s.Subtotal);
            report.Discounts = live.Sum(s => s.Discount);
            report.NetSales = live.Sum(s => s.TotalAmount);
            report.AverageSale = live.Count == 0 ? 0m : report.NetSales / live.Count;
            report.UnitsSold = live.Sum(s => s.TotalQuantity);
            report.Collected = live.Sum(s => s.AmountPaid);
            report.Outstanding = live.Sum(s => s.Balance);

            report.ByDay = BuildDailySeries(
                live.Select(s => (s.SaleDate, s.TotalAmount)), range);

            // Units and revenue per product, aggregated in the database over the sale lines.
            var productRows = await _context.SaleItems
                .Where(i => i.Sale.SaleDate >= range.FromUtc
                         && i.Sale.SaleDate < range.ToUtc
                         && i.Sale.Status != "Cancelled")
                .GroupBy(i => new
                {
                    i.ProductId,
                    i.Product.ProductCode,
                    i.Product.ProductName,
                    i.Product.Category,
                    i.Product.CostPrice
                })
                .Select(g => new ProductSalesRow
                {
                    ProductId = g.Key.ProductId,
                    ProductCode = g.Key.ProductCode,
                    ProductName = g.Key.ProductName,
                    Category = g.Key.Category,
                    QuantitySold = g.Sum(i => i.Quantity),
                    Revenue = g.Sum(i => i.Quantity * i.UnitPrice),
                    Cost = g.Sum(i => i.Quantity * g.Key.CostPrice)
                })
                .ToListAsync();

            report.TopProducts = productRows
                .OrderByDescending(p => p.Revenue)
                .Take(TopProductCount)
                .ToList();

            report.ByCategory = productRows
                .GroupBy(p => string.IsNullOrWhiteSpace(p.Category) ? "Other" : p.Category)
                .Select(g => new CategorySlice
                {
                    Label = g.Key,
                    Count = g.Sum(p => p.QuantitySold),
                    Value = g.Sum(p => p.Revenue)
                })
                .OrderByDescending(c => c.Value)
                .ToList();

            return report;
        }

        // ------------------------------------------------------------------ Payments

        public async Task<PaymentReport> GetPaymentReportAsync(DateTime? from, DateTime? to)
        {
            var range = ReportRange.Resolve(from, to);

            var report = new PaymentReport { FromUtc = range.FromUtc, ToUtc = range.ToUtc };

            var payments = await _paymentService.GetPaymentsInRangeAsync(range.FromUtc, range.ToUtc);

            report.Payments = payments;
            report.Count = payments.Count;
            report.TotalCollected = payments.Where(p => p.Status == "Completed").Sum(p => p.Amount);
            report.Pending = payments.Where(p => p.Status == "Pending").Sum(p => p.Amount);
            report.Refunded = payments.Where(p => p.Status == "Refunded").Sum(p => p.Amount);

            report.ByMethod = payments
                .Where(p => p.Status == "Completed")
                .GroupBy(p => p.Method)
                .Select(g => new CategorySlice
                {
                    Label = g.Key,
                    Count = g.Count(),
                    Value = g.Sum(p => p.Amount)
                })
                .OrderByDescending(c => c.Value)
                .ToList();

            report.ByDay = BuildDailySeries(
                payments.Where(p => p.Status == "Completed").Select(p => (p.PaymentDate, p.Amount)),
                range);

            // What is still owed, from both sides of the ledger. Sales are dated by the sale,
            // memberships by when the subscription started.
            var saleRows = await _context.Sales
                .Where(s => s.Status != "Cancelled")
                .Select(s => new OutstandingRow
                {
                    Source = "Sale",
                    ReferenceId = s.SaleId,
                    MemberId = s.MemberId,
                    MemberName = s.Member != null
                        ? s.Member.FirstName + " " + s.Member.LastName
                        : (s.WalkInName ?? "Walk-In"),
                    Date = s.SaleDate,
                    Total = s.TotalAmount,
                    Paid = s.Payments.Where(p => p.Status == "Completed").Sum(p => (decimal?)p.Amount) ?? 0m
                })
                .ToListAsync();

            var membershipRows = await _context.Subscriptions
                .Where(s => s.Status != "Cancelled")
                .Select(s => new OutstandingRow
                {
                    Source = "Membership",
                    ReferenceId = s.SubscriptionId,
                    MemberId = s.MemberId,
                    MemberName = s.Member != null
                        ? s.Member.FirstName + " " + s.Member.LastName
                        : (s.WalkInName ?? "Walk-In"),
                    Date = s.StartDate,
                    Total = s.Plan.Price,
                    Paid = s.Payments.Where(p => p.Status == "Completed").Sum(p => (decimal?)p.Amount) ?? 0m
                })
                .ToListAsync();

            foreach (var row in saleRows.Concat(membershipRows))
            {
                row.Balance = Math.Max(0m, row.Total - row.Paid);
                row.Status = row.Balance <= 0
                    ? "Paid"
                    : row.Paid > 0 ? "Partially Paid" : "Unpaid";
            }

            report.OutstandingFromSales = saleRows.Sum(r => r.Balance);
            report.OutstandingFromMemberships = membershipRows.Sum(r => r.Balance);

            var all = saleRows.Concat(membershipRows).ToList();
            report.PaidCount = all.Count(r => r.Status == "Paid");
            report.PartiallyPaidCount = all.Count(r => r.Status == "Partially Paid");
            report.UnpaidCount = all.Count(r => r.Status == "Unpaid");

            report.Outstanding = all
                .Where(r => r.Balance > 0)
                .OrderByDescending(r => r.Balance)
                .ToList();

            return report;
        }

        // ------------------------------------------------------------------ Inventory

        public async Task<InventoryReport> GetInventoryReportAsync(DateTime? from, DateTime? to)
        {
            var range = ReportRange.Resolve(from, to);

            var stock = await _inventoryService.GetInventoryAsync();
            var movements = await _inventoryService.GetMovementsAsync(take: 1000);

            return new InventoryReport
            {
                FromUtc = range.FromUtc,
                ToUtc = range.ToUtc,
                Summary = await _inventoryService.GetSummaryAsync(),
                Stock = stock,
                LowStock = stock.Where(s => s.StockStatus == "Low Stock").ToList(),
                OutOfStock = stock.Where(s => s.StockStatus == "Out of Stock").ToList(),
                Movements = movements
                    .Where(m => m.MovementDate >= range.FromUtc && m.MovementDate < range.ToUtc)
                    .ToList(),
                ValueByCategory = stock
                    .GroupBy(s => string.IsNullOrWhiteSpace(s.Category) ? "Other" : s.Category)
                    .Select(g => new CategorySlice
                    {
                        Label = g.Key,
                        Count = g.Count(),
                        Value = g.Sum(s => s.StockValue)
                    })
                    .OrderByDescending(c => c.Value)
                    .ToList()
            };
        }

        // ------------------------------------------------------------------ Membership

        public async Task<MembershipReport> GetMembershipReportAsync(DateTime? from, DateTime? to)
        {
            var range = ReportRange.Resolve(from, to);

            // Lapsed memberships are brought up to date first, so "expired" is accurate.
            await _subscriptionService.ExpireOverdueSubscriptionsAsync();

            var overview = await _memberService.GetMembershipOverviewAsync();

            var report = new MembershipReport
            {
                FromUtc = range.FromUtc,
                ToUtc = range.ToUtc,
                Members = overview,
                TotalMembers = overview.Count,
                ActiveMembers = overview.Count(m => m.MemberStatus == "Active"),
                NewMembersInRange = overview.Count(
                    m => m.JoinDate >= range.FromUtc && m.JoinDate < range.ToUtc),
                ActiveSubscriptions = overview.Count(m => m.MembershipStatus == "Active"),
                ExpiringSoon = overview.Count(m => m.MembershipStatus == "Expiring Soon"),
                ExpiredSubscriptions = overview.Count(m => m.MembershipStatus == "Expired"),
                CancelledSubscriptions = overview.Count(m => m.MembershipStatus == "Cancelled"),
                MembershipRevenue = overview.Sum(m => m.AmountPaid),
                MembershipOutstanding = overview.Sum(m => m.Balance)
            };

            report.InactiveMembers = report.TotalMembers - report.ActiveMembers;

            report.ByPlan = await _context.Subscriptions
                .GroupBy(s => s.Plan.PlanName)
                .Select(g => new CategorySlice
                {
                    Label = g.Key,
                    Count = g.Count(),
                    Value = g.Count()
                })
                .OrderByDescending(c => c.Count)
                .ToListAsync();

            report.JoinsByDay = BuildDailySeries(
                overview
                    .Where(m => m.JoinDate >= range.FromUtc && m.JoinDate < range.ToUtc)
                    .Select(m => (m.JoinDate, 1m)),
                range);

            return report;
        }

        // ------------------------------------------------------------------ Expenses

        public async Task<ExpenseReport> GetExpenseReportAsync(DateTime? from, DateTime? to)
        {
            var range = ReportRange.Resolve(from, to);

            var expenses = await _expenseService.GetExpensesAsync(range.FromUtc, range.ToUtc);

            // Payroll is money out too, so the outgoings total would be misleading without it.
            var payrollPaid = await _context.Payrolls
                .Where(p => p.Status == "Paid"
                         && p.PaidDate != null
                         && p.PaidDate >= range.FromUtc
                         && p.PaidDate < range.ToUtc)
                .SumAsync(p => (decimal?)p.NetPay) ?? 0m;

            return new ExpenseReport
            {
                FromUtc = range.FromUtc,
                ToUtc = range.ToUtc,
                Count = expenses.Count,
                Total = expenses.Sum(e => e.Amount),
                PayrollPaid = payrollPaid,
                Expenses = expenses,
                ByCategory = expenses
                    .GroupBy(e => e.Category)
                    .Select(g => new CategorySlice
                    {
                        Label = g.Key,
                        Count = g.Count(),
                        Value = g.Sum(e => e.Amount)
                    })
                    .OrderByDescending(c => c.Value)
                    .ToList(),
                ByDay = BuildDailySeries(expenses.Select(e => (e.ExpenseDate, e.Amount)), range)
            };
        }

        // ------------------------------------------------------------------ Employees

        /// <summary>
        /// Headcount, attendance and leave over a period.
        ///
        /// The attendance totals are aggregated per employee by the database in one query
        /// rather than by asking for each employee's summary in a loop. A gym can carry a few
        /// hundred staff, and against a remote database one round trip each is the difference
        /// between a report that opens and a report that times out.
        ///
        /// The hours are read from the same Attendance rows Payroll calculates from, so this
        /// report and a payslip for the same period cannot disagree.
        /// </summary>
        public async Task<EmployeeReport> GetEmployeeReportAsync(DateTime? from, DateTime? to)
        {
            var range = ReportRange.Resolve(from, to);

            var report = new EmployeeReport { FromUtc = range.FromUtc, ToUtc = range.ToUtc };

            var employees = await _context.Employees
                .AsNoTracking()
                .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
                .Select(e => new
                {
                    e.EmployeeId, e.EmployeeCode, e.FirstName, e.LastName,
                    e.Position, e.Department, e.Status, e.HireDate
                })
                .ToListAsync();

            report.TotalEmployees = employees.Count;
            report.ActiveEmployees = employees.Count(e => e.Status == "Active");
            report.InactiveEmployees = report.TotalEmployees - report.ActiveEmployees;
            report.NewHiresInRange = employees.Count(
                e => e.HireDate >= range.FromUtc && e.HireDate < range.ToUtc);

            // Headcount by position. Value mirrors Count so a chart can plot either without
            // the caller needing to know which field a head count lives in.
            report.ByPosition = employees
                .GroupBy(e => string.IsNullOrWhiteSpace(e.Position) ? "Unassigned" : e.Position)
                .Select(g => new CategorySlice
                {
                    Label = g.Key,
                    Count = g.Count(),
                    Value = g.Count()
                })
                .OrderByDescending(c => c.Count)
                .ToList();

            var attendance = await _context.Attendances
                .AsNoTracking()
                .Where(a => a.Date >= range.FromUtc && a.Date < range.ToUtc)
                .GroupBy(a => a.EmployeeId)
                .Select(g => new
                {
                    EmployeeId = g.Key,
                    Present = g.Count(a => a.Status == "Present"),
                    Absent = g.Count(a => a.Status == "Absent"),
                    Late = g.Count(a => a.Status == "Late"),
                    OnLeave = g.Count(a => a.Status == "Leave"),
                    Regular = g.Sum(a => a.RegularHours),
                    Overtime = g.Sum(a => a.OvertimeHours)
                })
                .ToListAsync();

            var byEmployee = attendance.ToDictionary(a => a.EmployeeId);

            // Everybody on the roll appears, including those with nothing recorded: a report
            // that silently omits the employee who never turned up is the one report where
            // that absence is the whole point.
            report.Employees = employees
                .Select(e =>
                {
                    byEmployee.TryGetValue(e.EmployeeId, out var a);

                    return new EmployeeAttendanceRow
                    {
                        EmployeeId = e.EmployeeId,
                        EmployeeCode = e.EmployeeCode,
                        EmployeeName = $"{e.FirstName} {e.LastName}".Trim(),
                        Position = e.Position,
                        Department = e.Department,
                        Status = e.Status,
                        HireDate = e.HireDate,
                        DaysPresent = a?.Present ?? 0,
                        DaysAbsent = a?.Absent ?? 0,
                        DaysLate = a?.Late ?? 0,
                        DaysOnLeave = a?.OnLeave ?? 0,
                        RegularHours = a?.Regular ?? 0m,
                        OvertimeHours = a?.Overtime ?? 0m
                    };
                })
                .ToList();

            report.DaysPresent = report.Employees.Sum(e => e.DaysPresent);
            report.DaysAbsent = report.Employees.Sum(e => e.DaysAbsent);
            report.DaysLate = report.Employees.Sum(e => e.DaysLate);
            report.DaysOnLeave = report.Employees.Sum(e => e.DaysOnLeave);
            report.TotalRegularHours = report.Employees.Sum(e => e.RegularHours);
            report.TotalOvertimeHours = report.Employees.Sum(e => e.OvertimeHours);

            var recorded = report.DaysPresent + report.DaysAbsent
                         + report.DaysLate + report.DaysOnLeave;

            report.AttendanceRate = recorded == 0
                ? 0m
                : Math.Round((report.DaysPresent + report.DaysLate) * 100m / recorded, 1);

            // Hours worked per day, with the head count that produced them.
            var daily = await _context.Attendances
                .AsNoTracking()
                .Where(a => a.Date >= range.FromUtc && a.Date < range.ToUtc)
                .Select(a => new { a.Date, Hours = a.RegularHours + a.OvertimeHours })
                .ToListAsync();

            report.AttendanceByDay = BuildDailySeries(
                daily.Select(d => (d.Date, d.Hours)), range);

            // Any request whose leave overlaps the window at all, which is not the same as one
            // that started inside it - a fortnight's leave beginning last month is still leave
            // taken this month.
            var leave = await _context.LeaveRequests
                .AsNoTracking()
                .Where(l => l.StartDate < range.ToUtc && l.EndDate >= range.FromUtc)
                .Select(l => new { l.LeaveType, l.Status, l.Days })
                .ToListAsync();

            report.PendingLeaveRequests = leave.Count(l => l.Status == LeaveStatuses.Pending);
            report.ApprovedLeaveRequests = leave.Count(l => l.Status == LeaveStatuses.Approved);
            report.ApprovedLeaveDays = leave
                .Where(l => l.Status == LeaveStatuses.Approved)
                .Sum(l => l.Days);

            report.ByLeaveType = leave
                .Where(l => l.Status == LeaveStatuses.Approved)
                .GroupBy(l => string.IsNullOrWhiteSpace(l.LeaveType) ? "Other" : l.LeaveType)
                .Select(g => new CategorySlice
                {
                    Label = g.Key,
                    Count = g.Count(),
                    Value = g.Sum(l => l.Days)
                })
                .OrderByDescending(c => c.Value)
                .ToList();

            return report;
        }

        // ------------------------------------------------------------------ Reconciliation

        /// <summary>
        /// The takings, the ledger and the bank, set against each other for a period.
        ///
        /// "Unposted" is defined here exactly as <c>JournalService.GetEntryForSourceAsync</c>
        /// defines "already posted" - an entry keyed to this payment in any status but Void.
        /// Using the same rule as the idempotency guard is the whole point: if this screen and
        /// the catch-up sweep disagreed about which payments are missing, one of them would be
        /// reporting arrears the other refuses to fix.
        ///
        /// A reversed entry still counts as posted. The original stays Posted and gains a
        /// mirror, and the pair nets to zero, so the money is accounted for even though the
        /// balance moved back.
        /// </summary>
        public async Task<PaymentReconciliationView> GetPaymentReconciliationAsync(
            DateTime? from, DateTime? to)
        {
            var range = ReportRange.Resolve(from, to);

            var view = new PaymentReconciliationView
            {
                FromUtc = range.FromUtc,
                ToUtc = range.ToUtc
            };

            var payments = await _paymentService.GetPaymentsInRangeAsync(range.FromUtc, range.ToUtc);

            // Only completed payments are money the gym actually holds. A pending payment has
            // not arrived and a refunded one has gone back, so neither belongs in the figure
            // the ledger is being checked against.
            var completed = payments
                .Where(p => string.Equals(p.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                .ToList();

            view.PaymentCount = payments.Count;
            view.Takings = completed.Sum(p => p.Amount);
            view.Pending = payments
                .Where(p => string.Equals(p.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                .Sum(p => p.Amount);
            view.Refunded = payments
                .Where(p => string.Equals(p.Status, "Refunded", StringComparison.OrdinalIgnoreCase))
                .Sum(p => p.Amount);

            view.ByMethod = completed
                .GroupBy(p => string.IsNullOrWhiteSpace(p.Method) ? "Unspecified" : p.Method)
                .Select(g => new PaymentMethodTotal
                {
                    Method = g.Key,
                    Count = g.Count(),
                    Total = g.Sum(p => p.Amount)
                })
                .OrderByDescending(m => m.Total)
                .ToList();

            // What the ledger was told. Only cash and bank accounts, and only entries a payment
            // raised - a sale posts a receivable rather than cash, so counting every cash debit
            // would compare the takings against money that arrived by other routes too.
            var cashAccountIds = await _context.Accounts
                .AsNoTracking()
                .Where(a => a.SystemKey == AccountKeys.Cash || a.SystemKey == AccountKeys.Bank)
                .Select(a => a.AccountId)
                .ToListAsync();

            var postings = cashAccountIds.Count == 0
                ? new List<PostingRow>()
                : await _context.JournalEntryLines
                    .AsNoTracking()
                    .Where(l => cashAccountIds.Contains(l.AccountId)
                             && l.Entry.Status == JournalStatuses.Posted
                             && l.Entry.Source == JournalSources.Payment
                             && l.Entry.EntryDate >= range.FromUtc
                             && l.Entry.EntryDate < range.ToUtc)
                    .Select(l => new PostingRow
                    {
                        Date = l.Entry.EntryDate,
                        Amount = l.Debit - l.Credit
                    })
                    .ToListAsync();

            view.PostedToLedger = postings.Sum(p => p.Amount);

            var takingsByDay = completed
                .GroupBy(p => p.PaymentDate.Date)
                .ToDictionary(g => g.Key, g => (Total: g.Sum(p => p.Amount), Count: g.Count()));

            var postedByDay = postings
                .GroupBy(p => p.Date.Date)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

            // Every day that either side knows about, so a day the ledger has and the till does
            // not is as visible as the other way round.
            view.ByDay = takingsByDay.Keys
                .Union(postedByDay.Keys)
                .OrderBy(d => d)
                .Select(d => new ReconciliationDayRow
                {
                    Date = d,
                    PaymentCount = takingsByDay.TryGetValue(d, out var t) ? t.Count : 0,
                    Takings = takingsByDay.TryGetValue(d, out var takings) ? takings.Total : 0m,
                    Posted = postedByDay.TryGetValue(d, out var posted) ? posted : 0m
                })
                .ToList();

            var bank = await _context.BankTransactions
                .AsNoTracking()
                .Where(t => t.TransactionDate >= range.FromUtc
                         && t.TransactionDate < range.ToUtc)
                .Select(t => new { t.Direction, t.Amount, t.IsReconciled })
                .ToListAsync();

            view.BankedIn = bank
                .Where(t => t.Direction == BankTransactionDirections.In)
                .Sum(t => t.Amount);

            view.BankedOut = bank
                .Where(t => t.Direction == BankTransactionDirections.Out)
                .Sum(t => t.Amount);

            var unreconciled = bank.Where(t => !t.IsReconciled).ToList();

            view.UnreconciledBankCount = unreconciled.Count;
            view.UnreconciledBankAmount = unreconciled.Sum(
                t => t.Direction == BankTransactionDirections.In ? t.Amount : -t.Amount);

            // Which payments are missing a posting, asked only about the payments in range so
            // the IN clause stays the size of the period rather than the size of the history.
            var keys = completed.Select(p => p.PaymentId.ToString()).ToList();

            var postedKeys = keys.Count == 0
                ? new List<string>()
                : await _context.JournalEntries
                    .AsNoTracking()
                    .Where(e => e.SourceEntityName == nameof(Payment)
                             && e.SourceEntityId != null
                             && keys.Contains(e.SourceEntityId)
                             && e.Status != JournalStatuses.Void)
                    .Select(e => e.SourceEntityId!)
                    .ToListAsync();

            var posted = new HashSet<string>(postedKeys, StringComparer.Ordinal);

            view.UnpostedPayments = completed
                .Where(p => !posted.Contains(p.PaymentId.ToString()))
                .OrderBy(p => p.PaymentDate)
                .ToList();

            view.UnpostedPaymentCount = view.UnpostedPayments.Count;
            view.UnpostedPaymentAmount = view.UnpostedPayments.Sum(p => p.Amount);

            return view;
        }

        /// <summary>A cash or bank ledger line, flattened so it can be projected in the query.</summary>
        private sealed class PostingRow
        {
            public DateTime Date { get; set; }
            public decimal Amount { get; set; }
        }

        // ------------------------------------------------------------------ Payroll

        /// <summary>
        /// What payroll cost over a period.
        ///
        /// Runs are chosen by the day the pay period ends, so each run lands in exactly one
        /// report and twelve monthly reports add up to the year. An overlap rule would count a
        /// run spanning the turn of the month in both of them.
        /// </summary>
        public async Task<PayrollReport> GetPayrollReportAsync(DateTime? from, DateTime? to)
        {
            var range = ReportRange.Resolve(from, to);

            var report = new PayrollReport { FromUtc = range.FromUtc, ToUtc = range.ToUtc };

            var runs = await _context.Payrolls
                .AsNoTracking()
                .Where(p => p.PeriodEnd >= range.FromUtc && p.PeriodEnd < range.ToUtc)
                .OrderByDescending(p => p.PeriodEnd)
                .Select(p => new PayrollView
                {
                    PayrollId = p.PayrollId,
                    EmployeeId = p.EmployeeId,
                    EmployeeCode = p.Employee.EmployeeCode,
                    EmployeeName = p.Employee.FirstName + " " + p.Employee.LastName,
                    Position = p.Employee.Position,
                    PeriodStart = p.PeriodStart,
                    PeriodEnd = p.PeriodEnd,
                    BasicSalary = p.BasicSalary,
                    Allowances = p.Allowances,
                    RegularHours = p.RegularHours,
                    HourlyRate = p.HourlyRate,
                    RegularPay = p.RegularPay,
                    OvertimeHours = p.OvertimeHours,
                    OvertimeRate = p.OvertimeRate,
                    OvertimePay = p.OvertimePay,
                    GrossPay = p.GrossPay,
                    SssDeduction = p.SssDeduction,
                    PhilHealthDeduction = p.PhilHealthDeduction,
                    PagIbigDeduction = p.PagIbigDeduction,
                    WithholdingTax = p.WithholdingTax,
                    OtherDeductions = p.OtherDeductions,
                    Deductions = p.Deductions,
                    NetPay = p.NetPay,
                    SssEmployerShare = p.SssEmployerShare,
                    PhilHealthEmployerShare = p.PhilHealthEmployerShare,
                    PagIbigEmployerShare = p.PagIbigEmployerShare,
                    EmployerContributions = p.EmployerContributions,
                    Status = p.Status,
                    PaidDate = p.PaidDate,
                    Notes = p.Notes,
                    ApprovedAt = p.ApprovedAt,
                    ApprovedBy = p.ApprovedBy,
                    ProcessedByUserId = p.ProcessedByUserId,
                    ProcessedBy = p.ProcessedBy,
                    LastModifiedByUserId = p.LastModifiedByUserId,
                    LastModifiedBy = p.LastModifiedBy
                })
                .ToListAsync();

            report.Runs = runs;
            report.RunCount = runs.Count;
            report.EmployeeCount = runs.Select(r => r.EmployeeId).Distinct().Count();

            var paid = runs
                .Where(r => string.Equals(r.Status, "Paid", StringComparison.OrdinalIgnoreCase))
                .ToList();

            report.PaidRunCount = paid.Count;
            report.UnpaidRunCount = runs.Count - paid.Count;

            report.RegularHours = runs.Sum(r => r.RegularHours);
            report.OvertimeHours = runs.Sum(r => r.OvertimeHours);

            report.RegularPay = runs.Sum(r => r.RegularPay);
            report.OvertimePay = runs.Sum(r => r.OvertimePay);
            report.Allowances = runs.Sum(r => r.Allowances);
            report.GrossPay = runs.Sum(r => r.GrossPay);

            report.Sss = runs.Sum(r => r.SssDeduction);
            report.PhilHealth = runs.Sum(r => r.PhilHealthDeduction);
            report.PagIbig = runs.Sum(r => r.PagIbigDeduction);
            report.WithholdingTax = runs.Sum(r => r.WithholdingTax);
            report.OtherDeductions = runs.Sum(r => r.OtherDeductions);
            report.TotalDeductions = runs.Sum(r => r.Deductions);

            report.NetPay = runs.Sum(r => r.NetPay);

            report.SssEmployerShare = runs.Sum(r => r.SssEmployerShare);
            report.PhilHealthEmployerShare = runs.Sum(r => r.PhilHealthEmployerShare);
            report.PagIbigEmployerShare = runs.Sum(r => r.PagIbigEmployerShare);
            report.EmployerContributions = runs.Sum(r => r.EmployerContributions);

            // Only a paid run has actually left the bank; everything else is still owed.
            report.Paid = paid.Sum(r => r.NetPay);
            report.Outstanding = report.NetPay - report.Paid;

            report.ByStatus = runs
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Status) ? "Draft" : r.Status)
                .Select(g => new CategorySlice
                {
                    Label = g.Key,
                    Count = g.Count(),
                    Value = g.Sum(r => r.NetPay)
                })
                .OrderByDescending(c => c.Value)
                .ToList();

            // Named individually rather than summed, because a payslip query is almost always
            // "which of these five figures is the one I am arguing about".
            report.DeductionBreakdown = new List<CategorySlice>
            {
                new() { Label = "SSS", Value = report.Sss, Count = runs.Count(r => r.SssDeduction > 0m) },
                new() { Label = "PhilHealth", Value = report.PhilHealth, Count = runs.Count(r => r.PhilHealthDeduction > 0m) },
                new() { Label = "Pag-IBIG", Value = report.PagIbig, Count = runs.Count(r => r.PagIbigDeduction > 0m) },
                new() { Label = "Withholding tax", Value = report.WithholdingTax, Count = runs.Count(r => r.WithholdingTax > 0m) },
                new() { Label = "Other", Value = report.OtherDeductions, Count = runs.Count(r => r.OtherDeductions > 0m) }
            }
            .Where(s => s.Value != 0m)
            .ToList();

            // Dated on the day the period ends, which is the run's own date as far as this
            // report is concerned.
            report.ByDay = BuildDailySeries(
                runs.Select(r => (r.PeriodEnd, r.NetPay)), range);

            return report;
        }

        // ------------------------------------------------------------------ Helpers

        /// <summary>
        /// One point per day across the whole range, including the days with nothing in them,
        /// so a chart drawn from this has an even time axis.
        /// </summary>
        private static List<TrendPoint> BuildDailySeries(
            IEnumerable<(DateTime Date, decimal Amount)> source, ReportRange range)
        {
            var byDay = source
                .GroupBy(x => x.Date.Date)
                .ToDictionary(
                    g => g.Key,
                    g => (Total: g.Sum(x => x.Amount), Count: g.Count()));

            var points = new List<TrendPoint>();
            var days = (range.ToUtc.Date - range.FromUtc.Date).Days;

            // A very long range is bucketed by month instead, so the series stays readable.
            if (days > 62)
            {
                var monthly = source
                    .GroupBy(x => new DateTime(x.Date.Year, x.Date.Month, 1))
                    .OrderBy(g => g.Key)
                    .Select(g => new TrendPoint
                    {
                        Date = g.Key,
                        Label = g.Key.ToString("MMM yyyy"),
                        Value = g.Sum(x => x.Amount),
                        Count = g.Count()
                    })
                    .ToList();

                return monthly;
            }

            for (var i = 0; i < days; i++)
            {
                var day = range.FromUtc.Date.AddDays(i);
                byDay.TryGetValue(day, out var match);

                points.Add(new TrendPoint
                {
                    Date = day,
                    Label = day.ToString("MMM d"),
                    Value = match.Total,
                    Count = match.Count
                });
            }

            return points;
        }
    }
}
