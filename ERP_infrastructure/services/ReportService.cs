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
                    MemberName = s.Member.FirstName + " " + s.Member.LastName,
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
                    MemberName = s.Member.FirstName + " " + s.Member.LastName,
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
