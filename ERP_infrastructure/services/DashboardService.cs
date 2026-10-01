using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    public class DashboardService : IDashboardService
    {
        private const int TrendDays = 14;
        private const int TrendMonths = 6;
        private const int ActivityItems = 12;

        private readonly TenantErpDbContext _context;
        private readonly ISubscriptionService _subscriptionService;

        public DashboardService(TenantErpDbContext context, ISubscriptionService subscriptionService)
        {
            _context = context;
            _subscriptionService = subscriptionService;
        }

        /// <summary>
        /// Builds the dashboard figures.
        ///
        /// The counts are grouped so each table is aggregated in a single round trip. Against a
        /// remote database the alternative, one query per figure, spends most of its time
        /// waiting on latency. The queries stay sequential because a single DbContext cannot
        /// run overlapping operations.
        /// </summary>
        public async Task<DashboardSummary> GetSummaryAsync()
        {
            // Bring lapsed memberships up to date before counting them.
            await _subscriptionService.ExpireOverdueSubscriptionsAsync();

            var now = DateTime.UtcNow;
            var soonCutoff = now.AddDays(MemberService.ExpiringSoonDays);
            var cutoff1Day = now.AddDays(1);
            var cutoff3Days = now.AddDays(3);
            var cutoff7Days = now.AddDays(7);
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var dayStart = now.Date;
            var trendStart = dayStart.AddDays(-(TrendDays - 1));
            var monthTrendStart = monthStart.AddMonths(-(TrendMonths - 1));

            var summary = new DashboardSummary { GeneratedAtUtc = now };

            // ------------------------------------------------------------ Members
            var memberStats = await _context.Members
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Active = g.Count(m => m.Status == "Active"),
                    NewThisMonth = g.Count(m => m.JoinDate >= monthStart)
                })
                .FirstOrDefaultAsync();

            summary.TotalMembers = memberStats?.Total ?? 0;
            summary.ActiveMembers = memberStats?.Active ?? 0;
            summary.InactiveMembers = summary.TotalMembers - summary.ActiveMembers;
            summary.NewMembersThisMonth = memberStats?.NewThisMonth ?? 0;

            // ------------------------------------------------------------ Subscriptions
            var subscriptionStats = await _context.Subscriptions
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Active = g.Count(s => s.Status == "Active"),
                    ExpiringSoon = g.Count(s =>
                        s.Status == "Active" && s.EndDate >= now && s.EndDate <= soonCutoff),
                    ExpiringWithin1Day = g.Count(s =>
                        s.Status == "Active" && s.EndDate >= now && s.EndDate <= cutoff1Day),
                    ExpiringWithin3Days = g.Count(s =>
                        s.Status == "Active" && s.EndDate >= now && s.EndDate <= cutoff3Days),
                    ExpiringWithin7Days = g.Count(s =>
                        s.Status == "Active" && s.EndDate >= now && s.EndDate <= cutoff7Days),
                    Expired = g.Count(s => s.Status == "Expired"),
                    Cancelled = g.Count(s => s.Status == "Cancelled"),
                    MembersWithPlan = g.Select(s => s.MemberId).Distinct().Count()
                })
                .FirstOrDefaultAsync();

            summary.ActiveSubscriptions = subscriptionStats?.Active ?? 0;
            summary.ExpiringSoon = subscriptionStats?.ExpiringSoon ?? 0;
            summary.ExpiredSubscriptions = subscriptionStats?.Expired ?? 0;
            summary.ExpiringWithin1Day = subscriptionStats?.ExpiringWithin1Day ?? 0;
            summary.ExpiringWithin3Days = subscriptionStats?.ExpiringWithin3Days ?? 0;
            summary.ExpiringWithin7Days = subscriptionStats?.ExpiringWithin7Days ?? 0;

            summary.TotalPlans = await _context.MembershipPlans.CountAsync();
            summary.TotalProducts = await _context.Products.CountAsync();

            summary.MembershipBreakdown = new List<CategorySlice>
            {
                new() { Label = "Active", Count = summary.ActiveSubscriptions - summary.ExpiringSoon,
                        Value = summary.ActiveSubscriptions - summary.ExpiringSoon },
                new() { Label = "Expiring Soon", Count = summary.ExpiringSoon, Value = summary.ExpiringSoon },
                new() { Label = "Expired", Count = summary.ExpiredSubscriptions, Value = summary.ExpiredSubscriptions },
                new() { Label = "Cancelled", Count = subscriptionStats?.Cancelled ?? 0,
                        Value = subscriptionStats?.Cancelled ?? 0 },
                new() { Label = "No Plan",
                        Count = Math.Max(0, summary.TotalMembers - (subscriptionStats?.MembersWithPlan ?? 0)),
                        Value = Math.Max(0, summary.TotalMembers - (subscriptionStats?.MembersWithPlan ?? 0)) }
            };

            // ------------------------------------------------------------ Sales
            // Cancelled sales were reversed, so they are excluded from every revenue figure.
            var liveSales = _context.Sales.Where(s => s.Status != "Cancelled");

            var saleStats = await liveSales
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    Total = g.Sum(s => s.TotalAmount),
                    ThisMonth = g.Sum(s => s.SaleDate >= monthStart ? s.TotalAmount : 0m),
                    Today = g.Sum(s => s.SaleDate >= dayStart ? s.TotalAmount : 0m),
                    TransactionsToday = g.Count(s => s.SaleDate >= dayStart),
                    TransactionsThisMonth = g.Count(s => s.SaleDate >= monthStart)
                })
                .FirstOrDefaultAsync();

            summary.SalesCount = saleStats?.Count ?? 0;
            summary.SalesTotal = saleStats?.Total ?? 0m;
            summary.SalesThisMonth = saleStats?.ThisMonth ?? 0m;
            summary.SalesToday = saleStats?.Today ?? 0m;
            summary.TransactionsToday = saleStats?.TransactionsToday ?? 0;
            summary.TransactionsThisMonth = saleStats?.TransactionsThisMonth ?? 0;

            // ------------------------------------------------------------ Payments
            var paymentStats = await _context.Payments
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    Total = g.Sum(p => p.Status == "Completed" ? p.Amount : 0m),
                    ThisMonth = g.Sum(p =>
                        p.Status == "Completed" && p.PaymentDate >= monthStart ? p.Amount : 0m),
                    Today = g.Sum(p =>
                        p.Status == "Completed" && p.PaymentDate >= dayStart ? p.Amount : 0m),
                    Pending = g.Count(p => p.Status == "Pending")
                })
                .FirstOrDefaultAsync();

            summary.PaymentsCount = paymentStats?.Count ?? 0;
            summary.PaymentsTotal = paymentStats?.Total ?? 0m;
            summary.PaymentsThisMonth = paymentStats?.ThisMonth ?? 0m;
            summary.PaymentsToday = paymentStats?.Today ?? 0m;
            summary.PendingPayments = paymentStats?.Pending ?? 0;

            summary.PaymentMethods = await _context.Payments
                .Where(p => p.Status == "Completed")
                .GroupBy(p => p.Method)
                .Select(g => new CategorySlice
                {
                    Label = g.Key,
                    Count = g.Count(),
                    Value = g.Sum(p => p.Amount)
                })
                .OrderByDescending(x => x.Value)
                .ToListAsync();

            // What the gym is still owed. Sales and memberships are asked separately because
            // they are settled through different records.
            var saleBalances = await liveSales
                .Select(s => new
                {
                    s.TotalAmount,
                    Paid = s.Payments.Where(p => p.Status == "Completed").Sum(p => (decimal?)p.Amount) ?? 0m
                })
                .ToListAsync();

            var membershipDue = await _context.Subscriptions
                .Where(s => s.Status != "Cancelled")
                .Select(s => new
                {
                    Price = s.Plan.Price,
                    Paid = s.Payments.Where(p => p.Status == "Completed").Sum(p => (decimal?)p.Amount) ?? 0m
                })
                .ToListAsync();

            summary.PartiallyPaidSales = saleBalances.Count(s => s.Paid > 0 && s.Paid < s.TotalAmount);
            summary.UnpaidSales = saleBalances.Count(s => s.Paid <= 0 && s.TotalAmount > 0);
            summary.OutstandingBalance =
                saleBalances.Sum(s => Math.Max(0m, s.TotalAmount - s.Paid)) +
                membershipDue.Sum(s => Math.Max(0m, s.Price - s.Paid));

            // ------------------------------------------------------------ Stock
            var stockStats = await _context.Inventories
                .Select(i => new
                {
                    i.QuantityOnHand,
                    i.ReorderLevel,
                    CostPrice = i.Product != null ? i.Product.CostPrice : 0m,
                    UnitPrice = i.Product != null ? i.Product.UnitPrice : 0m
                })
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    OutOfStock = g.Count(s => s.QuantityOnHand <= 0),
                    LowStock = g.Count(s =>
                        s.QuantityOnHand > 0 &&
                        s.ReorderLevel > 0 &&
                        s.QuantityOnHand <= s.ReorderLevel),
                    InStock = g.Count(s =>
                        s.QuantityOnHand > 0 &&
                        (s.ReorderLevel <= 0 || s.QuantityOnHand > s.ReorderLevel)),
                    Value = g.Sum(s => s.QuantityOnHand * (s.CostPrice > 0 ? s.CostPrice : s.UnitPrice))
                })
                .FirstOrDefaultAsync();

            summary.OutOfStockProducts = stockStats?.OutOfStock ?? 0;
            summary.LowStockProducts = stockStats?.LowStock ?? 0;
            summary.InventoryValue = stockStats?.Value ?? 0m;

            summary.StockBreakdown = new List<CategorySlice>
            {
                new() { Label = "In Stock", Count = stockStats?.InStock ?? 0, Value = stockStats?.InStock ?? 0 },
                new() { Label = "Low Stock", Count = summary.LowStockProducts, Value = summary.LowStockProducts },
                new() { Label = "Out of Stock", Count = summary.OutOfStockProducts, Value = summary.OutOfStockProducts }
            };

            // ------------------------------------------------------------ Workforce and costs
            var employeeStats = await _context.Employees
                .GroupBy(_ => 1)
                .Select(g => new { Total = g.Count(), Active = g.Count(e => e.Status == "Active") })
                .FirstOrDefaultAsync();

            summary.TotalEmployees = employeeStats?.Total ?? 0;
            summary.ActiveEmployees = employeeStats?.Active ?? 0;

            var payrollStats = await _context.Payrolls
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    ThisMonth = g.Sum(p => p.PeriodStart >= monthStart ? p.NetPay : 0m),
                    Outstanding = g.Sum(p => p.Status != "Paid" ? p.NetPay : 0m),
                    PaidThisMonth = g.Sum(p =>
                        p.Status == "Paid" && p.PaidDate != null && p.PaidDate >= monthStart ? p.NetPay : 0m),
                    Draft = g.Count(p => p.Status == "Draft")
                })
                .FirstOrDefaultAsync();

            summary.PayrollThisMonth = payrollStats?.ThisMonth ?? 0m;
            summary.PayrollOutstanding = payrollStats?.Outstanding ?? 0m;
            summary.DraftPayrollRuns = payrollStats?.Draft ?? 0;

            var expenseStats = await _context.Expenses
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Sum(e => e.Amount),
                    ThisMonth = g.Sum(e => e.ExpenseDate >= monthStart ? e.Amount : 0m)
                })
                .FirstOrDefaultAsync();

            summary.ExpensesTotal = expenseStats?.Total ?? 0m;
            summary.ExpensesThisMonth = expenseStats?.ThisMonth ?? 0m;

            summary.NetThisMonth =
                summary.PaymentsThisMonth - summary.ExpensesThisMonth - (payrollStats?.PaidThisMonth ?? 0m);

            // ------------------------------------------------------------ Trends
            summary.SalesTrend = await BuildDailyTrendAsync(
                liveSales.Where(s => s.SaleDate >= trendStart)
                    .Select(s => new DatedAmount { Date = s.SaleDate, Amount = s.TotalAmount }),
                trendStart);

            summary.PaymentTrend = await BuildDailyTrendAsync(
                _context.Payments
                    .Where(p => p.Status == "Completed" && p.PaymentDate >= trendStart)
                    .Select(p => new DatedAmount { Date = p.PaymentDate, Amount = p.Amount }),
                trendStart);

            summary.SalesByMonth = await BuildMonthlyTrendAsync(monthTrendStart);

            // ------------------------------------------------------------ Recent activity
            summary.RecentActivity = await BuildRecentActivityAsync();

            return summary;
        }

        private sealed class DatedAmount
        {
            public DateTime Date { get; set; }
            public decimal Amount { get; set; }
        }

        /// <summary>
        /// Groups a dated money series into one point per day and fills the gaps, so a quiet
        /// day is drawn as a zero rather than being skipped and distorting the line.
        /// </summary>
        private static async Task<List<TrendPoint>> BuildDailyTrendAsync(
            IQueryable<DatedAmount> source, DateTime startDate)
        {
            var grouped = await source
                .GroupBy(x => x.Date.Date)
                .Select(g => new { Day = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count() })
                .ToListAsync();

            var byDay = grouped.ToDictionary(x => x.Day, x => x);

            var points = new List<TrendPoint>(TrendDays);

            for (var i = 0; i < TrendDays; i++)
            {
                var day = startDate.AddDays(i);
                byDay.TryGetValue(day, out var match);

                points.Add(new TrendPoint
                {
                    Date = day,
                    Label = day.ToString("MMM d"),
                    Value = match?.Total ?? 0m,
                    Count = match?.Count ?? 0
                });
            }

            return points;
        }

        private async Task<List<TrendPoint>> BuildMonthlyTrendAsync(DateTime startMonth)
        {
            var grouped = await _context.Sales
                .Where(s => s.Status != "Cancelled" && s.SaleDate >= startMonth)
                .GroupBy(s => new { s.SaleDate.Year, s.SaleDate.Month })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Total = g.Sum(s => s.TotalAmount),
                    Count = g.Count()
                })
                .ToListAsync();

            var points = new List<TrendPoint>(TrendMonths);

            for (var i = 0; i < TrendMonths; i++)
            {
                var month = startMonth.AddMonths(i);
                var match = grouped.FirstOrDefault(g => g.Year == month.Year && g.Month == month.Month);

                points.Add(new TrendPoint
                {
                    Date = month,
                    Label = month.ToString("MMM yyyy"),
                    Value = match?.Total ?? 0m,
                    Count = match?.Count ?? 0
                });
            }

            return points;
        }

        /// <summary>
        /// The most recent real records across the modules, merged into one feed. Each source
        /// is capped before the merge so the feed costs a fixed number of small queries.
        /// </summary>
        private async Task<List<ActivityItem>> BuildRecentActivityAsync()
        {
            var items = new List<ActivityItem>();

            items.AddRange(await _context.Sales
                .OrderByDescending(s => s.SaleDate)
                .Take(ActivityItems)
                .Select(s => new ActivityItem
                {
                    Type = "Sale",
                    Title = s.Status == "Cancelled" ? $"Sale #{s.SaleId} cancelled" : $"Sale #{s.SaleId}",

                    // Most sales at the till are walk-ins and have no Member row; reaching
                    // through the navigation showed the activity feed a blank name for them.
                    Detail = s.Member == null
                        ? (s.WalkInName ?? "Walk-In")
                        : s.Member.FirstName + " " + s.Member.LastName,
                    Amount = s.TotalAmount,
                    OccurredAt = s.SaleDate,
                    Link = "fitcore/sales"
                })
                .ToListAsync());

            items.AddRange(await _context.Payments
                .OrderByDescending(p => p.PaymentDate)
                .Take(ActivityItems)
                .Select(p => new ActivityItem
                {
                    Type = "Payment",
                    Title = p.SaleId != null
                        ? "Payment for sale #" + p.SaleId
                        : p.SubscriptionId != null ? "Membership payment" : "Payment received",
                    Detail = (p.Member == null
                        ? (p.WalkInName ?? "Walk-In")
                        : p.Member.FirstName + " " + p.Member.LastName) + " - " + p.Method,
                    Amount = p.Amount,
                    OccurredAt = p.PaymentDate,
                    Link = "fitcore/payments"
                })
                .ToListAsync());

            items.AddRange(await _context.Members
                .OrderByDescending(m => m.JoinDate)
                .Take(ActivityItems)
                .Select(m => new ActivityItem
                {
                    Type = "Member",
                    Title = "New member",
                    Detail = m.FirstName + " " + m.LastName,
                    OccurredAt = m.JoinDate,
                    Link = "fitcore/members"
                })
                .ToListAsync());

            items.AddRange(await _context.StockMovements
                .OrderByDescending(m => m.MovementDate)
                .Take(ActivityItems)
                .Select(m => new ActivityItem
                {
                    Type = "Stock",
                    Title = "Stock " + m.MovementType.ToLower(),
                    Detail = (m.Product != null ? m.Product.ProductName : "Product #" + m.ProductId)
                             + " - " + m.Quantity.ToString() + " units",
                    OccurredAt = m.MovementDate,
                    Link = "fitcore/inventory"
                })
                .ToListAsync());

            items.AddRange(await _context.Expenses
                .OrderByDescending(e => e.ExpenseDate)
                .Take(ActivityItems)
                .Select(e => new ActivityItem
                {
                    Type = "Expense",
                    Title = e.Category + " expense",
                    Detail = e.Description,
                    Amount = e.Amount,
                    OccurredAt = e.ExpenseDate,
                    Link = "fitcore/expenses"
                })
                .ToListAsync());

            return items
                .OrderByDescending(i => i.OccurredAt)
                .Take(ActivityItems)
                .ToList();
        }
    }
}
