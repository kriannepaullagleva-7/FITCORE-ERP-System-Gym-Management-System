using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// The analytics engine: KPIs and charts computed from the tenant's own rows.
    ///
    /// Two rules run through all of it. Every number is read from the database at the moment it
    /// is asked for - nothing is estimated, sampled or carried over from a previous call -
    /// and every KPI is measured twice, once over the window asked for and once over the
    /// equal-length window before it, so a card can say what changed rather than only where
    /// things stand.
    /// </summary>
    public class BusinessIntelligenceService : IBusinessIntelligenceService
    {
        private readonly TenantErpDbContext _context;
        private readonly IFinanceReportService _finance;
        private readonly ISubscriptionService _subscriptions;

        public BusinessIntelligenceService(
            TenantErpDbContext context,
            IFinanceReportService finance,
            ISubscriptionService subscriptions)
        {
            _context = context;
            _finance = finance;
            _subscriptions = subscriptions;
        }

        public async Task<AnalyticsView> GetAnalyticsAsync(
            string area, DateTime? fromUtc, DateTime? toUtc)
        {
            if (!AnalyticsAreas.IsKnown(area))
            {
                throw new ValidationException(
                    $"'{area}' is not an analytics area. Choose one of: " +
                    string.Join(", ", AnalyticsAreas.All) + ".");
            }

            var window = Window.Resolve(fromUtc, toUtc);

            var view = NewView(area, window);

            switch (area.Trim().ToLowerInvariant())
            {
                case AnalyticsAreas.Membership:
                    view.Title = "Membership Analytics";
                    view.Description = "Growth, retention, renewals and plan mix.";
                    await BuildMembershipAsync(view, window);
                    break;

                case AnalyticsAreas.Sales:
                    view.Title = "Sales Analytics";
                    view.Description = "Revenue, basket size, product mix and growth.";
                    await BuildSalesAsync(view, window);
                    break;

                case AnalyticsAreas.Payments:
                    view.Title = "Payment Analytics";
                    view.Description = "Collection rate, method mix and what is still owed.";
                    await BuildPaymentsAsync(view, window);
                    break;

                case AnalyticsAreas.Inventory:
                    view.Title = "Inventory Analytics";
                    view.Description = "Value, movement, turnover and stock risk.";
                    await BuildInventoryAsync(view, window);
                    break;

                case AnalyticsAreas.Workforce:
                    view.Title = "Workforce Analytics";
                    view.Description = "Headcount, attendance, overtime and payroll cost.";
                    await BuildWorkforceAsync(view, window);
                    break;

                case AnalyticsAreas.Finance:
                    view.Title = "Finance Analytics";
                    view.Description = "Revenue, expenses, cash flow and net income over time.";
                    await BuildFinanceAsync(view, window);
                    break;

                case AnalyticsAreas.Profitability:
                    view.Title = "Profitability";
                    view.Description = "Gross margin, cost of goods sold and net margin.";
                    await BuildProfitabilityAsync(view, window);
                    break;

                default:
                    view.Title = "Business Intelligence";
                    view.Description = "The headline figures across every module.";
                    await BuildOverviewAsync(view, window);
                    break;
            }

            return view;
        }

        public async Task<AnalyticsView> GetKpiDashboardAsync(DateTime? fromUtc, DateTime? toUtc)
        {
            var window = Window.Resolve(fromUtc, toUtc);

            var view = NewView("kpi", window);
            view.Title = "Key Performance Indicators";
            view.Description = "Every figure FitCore measures, across all nine modules.";

            // Lapsed memberships are brought up to date first, or "active members" counts
            // people whose membership ran out last week.
            await _subscriptions.ExpireOverdueSubscriptionsAsync();

            view.Groups.Add(await MembershipKpisAsync(window));
            view.Groups.Add(await SalesKpisAsync(window));
            view.Groups.Add(await PaymentKpisAsync(window));
            view.Groups.Add(await InventoryKpisAsync(window));
            view.Groups.Add(await WorkforceKpisAsync(window));
            view.Groups.Add(await PayrollKpisAsync(window));
            view.Groups.Add(await FinanceKpisAsync(window));

            return view;
        }

        // ================================================================== areas

        private async Task BuildOverviewAsync(AnalyticsView view, Window window)
        {
            await _subscriptions.ExpireOverdueSubscriptionsAsync();

            view.Groups.Add(await MembershipKpisAsync(window));
            view.Groups.Add(await SalesKpisAsync(window));
            view.Groups.Add(await FinanceKpisAsync(window));

            view.Charts.Add(await RevenueVersusExpensesAsync(window));
            view.Charts.Add(await SalesByDayAsync(window));
            view.Charts.Add(await MemberStatusMixAsync());
        }

        private async Task BuildMembershipAsync(AnalyticsView view, Window window)
        {
            await _subscriptions.ExpireOverdueSubscriptionsAsync();

            view.Groups.Add(await MembershipKpisAsync(window));

            // Joins by month over a year, so seasonality is visible - a gym's January is not
            // its August and a single month's figure says nothing on its own.
            var months = window.TrailingMonths(12);

            var joins = await _context.Members
                .AsNoTracking()
                .Where(m => m.JoinDate >= months[0] && m.JoinDate < window.ToUtc)
                .Select(m => new { m.JoinDate })
                .ToListAsync();

            var cancellations = await _context.Subscriptions
                .AsNoTracking()
                .Where(s => s.Status == "Cancelled" && s.UpdatedAt != null &&
                            s.UpdatedAt >= months[0] && s.UpdatedAt < window.ToUtc)
                .Select(s => new { Date = s.UpdatedAt!.Value })
                .ToListAsync();

            view.Charts.Add(new ChartDefinition
            {
                Key = "membership-growth",
                Title = "Membership growth",
                Caption = "New members against cancelled memberships, by month.",
                ChartType = ChartTypes.Bar,
                ValueFormat = KpiFormats.Number,
                Labels = months.Select(m => m.ToString("MMM yy")).ToList(),
                Series = new List<ChartSeries>
                {
                    new()
                    {
                        Name = "Joined",
                        Values = months
                            .Select(m => (decimal)joins.Count(j =>
                                j.JoinDate.Year == m.Year && j.JoinDate.Month == m.Month))
                            .ToList()
                    },
                    new()
                    {
                        Name = "Cancelled",
                        Values = months
                            .Select(m => (decimal)cancellations.Count(c =>
                                c.Date.Year == m.Year && c.Date.Month == m.Month))
                            .ToList()
                    }
                }
            });

            view.Charts.Add(await MemberStatusMixAsync());

            var byPlan = await _context.Subscriptions
                .AsNoTracking()
                .Where(s => s.Status == "Active")
                .GroupBy(s => s.Plan.PlanName)
                .Select(g => new { Plan = g.Key, Count = g.Count(), Value = g.Sum(s => s.Plan.Price) })
                .OrderByDescending(g => g.Count)
                .ToListAsync();

            view.Charts.Add(new ChartDefinition
            {
                Key = "plan-mix",
                Title = "Active memberships by plan",
                Caption = "Which plans the membership is actually on.",
                ChartType = ChartTypes.Donut,
                ValueFormat = KpiFormats.Number,
                Labels = byPlan.Select(p => p.Plan).ToList(),
                Series = new List<ChartSeries>
                {
                    new() { Name = "Members", Values = byPlan.Select(p => (decimal)p.Count).ToList() }
                }
            });

            var expiring = await _context.Subscriptions
                .AsNoTracking()
                .Where(s => s.Status == "Active" && s.EndDate >= DateTime.UtcNow &&
                            s.EndDate <= DateTime.UtcNow.AddDays(30))
                .OrderBy(s => s.EndDate)
                .Select(s => new
                {
                    Member = s.Member.FirstName + " " + s.Member.LastName,
                    Plan = s.Plan.PlanName,
                    s.EndDate,
                    s.Plan.Price
                })
                .Take(200)
                .ToListAsync();

            view.Tables.Add(new AnalyticsTable
            {
                Key = "expiring",
                Title = "Memberships expiring in the next 30 days",
                Columns = new List<string> { "Member", "Plan", "Expires", "Renewal value" },
                Rows = expiring.Select(e => new List<string>
                {
                    e.Member, e.Plan, e.EndDate.ToString("d MMM yyyy"), e.Price.ToString("N2")
                }).ToList()
            });
        }

        private async Task BuildSalesAsync(AnalyticsView view, Window window)
        {
            view.Groups.Add(await SalesKpisAsync(window));

            view.Charts.Add(await SalesByDayAsync(window));

            var months = window.TrailingMonths(12);

            var monthly = await _context.Sales
                .AsNoTracking()
                .Where(s => s.Status == "Completed" &&
                            s.SaleDate >= months[0] && s.SaleDate < window.ToUtc)
                .Select(s => new { s.SaleDate, s.TotalAmount })
                .ToListAsync();

            view.Charts.Add(new ChartDefinition
            {
                Key = "sales-by-month",
                Title = "Sales by month",
                Caption = "Twelve months of takings, so a single month has something to be read against.",
                ChartType = ChartTypes.Bar,
                ValueFormat = KpiFormats.Money,
                Labels = months.Select(m => m.ToString("MMM yy")).ToList(),
                Series = new List<ChartSeries>
                {
                    new()
                    {
                        Name = "Revenue",
                        Values = months.Select(m => monthly
                            .Where(s => s.SaleDate.Year == m.Year && s.SaleDate.Month == m.Month)
                            .Sum(s => s.TotalAmount)).ToList()
                    }
                }
            });

            var topProducts = await _context.SaleItems
                .AsNoTracking()
                .Where(i => i.Sale.Status == "Completed" &&
                            i.Sale.SaleDate >= window.FromUtc && i.Sale.SaleDate < window.ToUtc)
                .GroupBy(i => new { i.ProductId, i.Product.ProductName })
                .Select(g => new
                {
                    g.Key.ProductName,
                    Units = g.Sum(i => i.Quantity),
                    Revenue = g.Sum(i => i.Quantity * i.UnitPrice),
                    Cost = g.Sum(i => i.Quantity * i.UnitCost)
                })
                .OrderByDescending(g => g.Revenue)
                .Take(10)
                .ToListAsync();

            view.Charts.Add(new ChartDefinition
            {
                Key = "top-products",
                Title = "Top products by revenue",
                Caption = "The ten lines earning the most over the period.",
                ChartType = ChartTypes.HorizontalBar,
                ValueFormat = KpiFormats.Money,
                Labels = topProducts.Select(p => p.ProductName).ToList(),
                Series = new List<ChartSeries>
                {
                    new() { Name = "Revenue", Values = topProducts.Select(p => p.Revenue).ToList() }
                }
            });

            view.Tables.Add(new AnalyticsTable
            {
                Key = "product-performance",
                Title = "Product performance",
                Columns = new List<string> { "Product", "Units", "Revenue", "Cost", "Margin", "Margin %" },
                Rows = topProducts.Select(p => new List<string>
                {
                    p.ProductName,
                    p.Units.ToString("N0"),
                    p.Revenue.ToString("N2"),
                    p.Cost.ToString("N2"),
                    (p.Revenue - p.Cost).ToString("N2"),
                    p.Revenue == 0m ? "—" : ((p.Revenue - p.Cost) / p.Revenue * 100m).ToString("N1") + "%"
                }).ToList()
            });
        }

        private async Task BuildPaymentsAsync(AnalyticsView view, Window window)
        {
            view.Groups.Add(await PaymentKpisAsync(window));

            var payments = await _context.Payments
                .AsNoTracking()
                .Where(p => p.PaymentDate >= window.FromUtc && p.PaymentDate < window.ToUtc)
                .Select(p => new { p.PaymentDate, p.Amount, p.Method, p.Status, p.Category })
                .ToListAsync();

            var days = window.Days();

            view.Charts.Add(new ChartDefinition
            {
                Key = "collections-by-day",
                Title = "Collections by day",
                Caption = "Money actually taken, day by day.",
                ChartType = ChartTypes.Area,
                ValueFormat = KpiFormats.Money,
                Labels = days.Select(d => d.ToString("d MMM")).ToList(),
                Series = new List<ChartSeries>
                {
                    new()
                    {
                        Name = "Collected",
                        Values = days.Select(d => payments
                            .Where(p => p.Status == "Completed" && p.PaymentDate.Date == d)
                            .Sum(p => p.Amount)).ToList()
                    }
                }
            });

            var byMethod = payments
                .Where(p => p.Status == "Completed")
                .GroupBy(p => p.Method)
                .Select(g => new { Method = g.Key, Total = g.Sum(p => p.Amount) })
                .OrderByDescending(g => g.Total)
                .ToList();

            view.Charts.Add(new ChartDefinition
            {
                Key = "payment-methods",
                Title = "How members pay",
                Caption = "Completed payments split by method.",
                ChartType = ChartTypes.Donut,
                ValueFormat = KpiFormats.Money,
                Labels = byMethod.Select(m => m.Method).ToList(),
                Series = new List<ChartSeries>
                {
                    new() { Name = "Collected", Values = byMethod.Select(m => m.Total).ToList() }
                }
            });

            var aged = await _finance.GetReceivablesAsync(window.ToUtc.AddDays(-1));

            view.Charts.Add(new ChartDefinition
            {
                Key = "receivables-ageing",
                Title = "What is owed, by age",
                Caption = "Debt that has been outstanding longer is less likely to be collected.",
                ChartType = ChartTypes.Bar,
                ValueFormat = KpiFormats.Money,
                Labels = new List<string> { "Current", "1-30 days", "31-60 days", "61-90 days", "Over 90" },
                Series = new List<ChartSeries>
                {
                    new()
                    {
                        Name = "Outstanding",
                        Values = new List<decimal>
                        {
                            aged.Current, aged.Days1To30, aged.Days31To60,
                            aged.Days61To90, aged.Over90Days
                        }
                    }
                }
            });

            view.Tables.Add(new AnalyticsTable
            {
                Key = "outstanding",
                Title = "Outstanding balances",
                Columns = new List<string> { "Member", "Document", "Date", "Total", "Paid", "Balance", "Age" },
                Rows = aged.Rows.Take(200).Select(r => new List<string>
                {
                    r.Party, r.Document, r.DocumentDate.ToString("d MMM yyyy"),
                    r.Total.ToString("N2"), r.Settled.ToString("N2"), r.Balance.ToString("N2"),
                    r.Bucket
                }).ToList()
            });
        }

        private async Task BuildInventoryAsync(AnalyticsView view, Window window)
        {
            view.Groups.Add(await InventoryKpisAsync(window));

            var stock = await _context.Inventories
                .AsNoTracking()
                .Include(i => i.Product)
                .Where(i => i.Product != null)
                .Select(i => new
                {
                    i.Product!.ProductName,
                    i.Product.Category,
                    i.QuantityOnHand,
                    i.ReorderLevel,
                    i.AverageCost,
                    i.Product.CostPrice,
                    i.Product.UnitPrice
                })
                .ToListAsync();

            var byCategory = stock
                .GroupBy(s => s.Category)
                .Select(g => new
                {
                    Category = g.Key,
                    Value = g.Sum(s => s.QuantityOnHand *
                        (s.AverageCost > 0m ? s.AverageCost : s.CostPrice))
                })
                .OrderByDescending(g => g.Value)
                .ToList();

            view.Charts.Add(new ChartDefinition
            {
                Key = "stock-value-by-category",
                Title = "Stock value by category",
                Caption = "Where the money tied up in stock actually is.",
                ChartType = ChartTypes.Donut,
                ValueFormat = KpiFormats.Money,
                Labels = byCategory.Select(c => c.Category).ToList(),
                Series = new List<ChartSeries>
                {
                    new() { Name = "Value", Values = byCategory.Select(c => c.Value).ToList() }
                }
            });

            var movements = await _context.StockMovements
                .AsNoTracking()
                .Where(m => m.MovementDate >= window.FromUtc && m.MovementDate < window.ToUtc)
                .Select(m => new { m.MovementDate, m.MovementType, m.Quantity })
                .ToListAsync();

            var days = window.Days();

            view.Charts.Add(new ChartDefinition
            {
                Key = "stock-movement",
                Title = "Stock in and out",
                Caption = "Units received against units issued.",
                ChartType = ChartTypes.Bar,
                ValueFormat = KpiFormats.Decimal,
                Labels = days.Select(d => d.ToString("d MMM")).ToList(),
                Series = new List<ChartSeries>
                {
                    new()
                    {
                        Name = "Received",
                        Values = days.Select(d => movements
                            .Where(m => m.MovementDate.Date == d && m.MovementType == "In")
                            .Sum(m => m.Quantity)).ToList()
                    },
                    new()
                    {
                        Name = "Issued",
                        Values = days.Select(d => movements
                            .Where(m => m.MovementDate.Date == d &&
                                        (m.MovementType == "Out" || m.MovementType == "Sale"))
                            .Sum(m => m.Quantity)).ToList()
                    }
                }
            });

            var atRisk = stock
                .Where(s => s.QuantityOnHand <= 0m ||
                            (s.ReorderLevel > 0m && s.QuantityOnHand <= s.ReorderLevel))
                .OrderBy(s => s.QuantityOnHand)
                .Take(200)
                .ToList();

            view.Tables.Add(new AnalyticsTable
            {
                Key = "stock-risk",
                Title = "Low and out of stock",
                Columns = new List<string> { "Product", "Category", "On hand", "Reorder at", "Status" },
                Rows = atRisk.Select(s => new List<string>
                {
                    s.ProductName, s.Category,
                    s.QuantityOnHand.ToString("N2"), s.ReorderLevel.ToString("N2"),
                    InventoryService.ResolveStockStatus(s.QuantityOnHand, s.ReorderLevel)
                }).ToList()
            });
        }

        private async Task BuildWorkforceAsync(AnalyticsView view, Window window)
        {
            view.Groups.Add(await WorkforceKpisAsync(window));
            view.Groups.Add(await PayrollKpisAsync(window));

            var months = window.TrailingMonths(12);

            var runs = await _context.Payrolls
                .AsNoTracking()
                .Where(p => p.PeriodEnd >= months[0] && p.PeriodEnd < window.ToUtc)
                .Select(p => new
                {
                    p.PeriodEnd, p.GrossPay, p.OvertimePay, p.EmployerContributions, p.Status
                })
                .ToListAsync();

            view.Charts.Add(new ChartDefinition
            {
                Key = "payroll-cost",
                Title = "Payroll cost by month",
                Caption = "Gross pay, overtime and the employer's own contributions.",
                ChartType = ChartTypes.StackedBar,
                ValueFormat = KpiFormats.Money,
                Labels = months.Select(m => m.ToString("MMM yy")).ToList(),
                Series = new List<ChartSeries>
                {
                    new()
                    {
                        Name = "Basic and regular",
                        Values = months.Select(m => runs
                            .Where(r => r.PeriodEnd.Year == m.Year && r.PeriodEnd.Month == m.Month)
                            .Sum(r => r.GrossPay - r.OvertimePay)).ToList()
                    },
                    new()
                    {
                        Name = "Overtime",
                        Values = months.Select(m => runs
                            .Where(r => r.PeriodEnd.Year == m.Year && r.PeriodEnd.Month == m.Month)
                            .Sum(r => r.OvertimePay)).ToList()
                    },
                    new()
                    {
                        Name = "Employer contributions",
                        Values = months.Select(m => runs
                            .Where(r => r.PeriodEnd.Year == m.Year && r.PeriodEnd.Month == m.Month)
                            .Sum(r => r.EmployerContributions)).ToList()
                    }
                }
            });

            var attendance = await _context.Attendances
                .AsNoTracking()
                .Where(a => a.Date >= window.FromUtc && a.Date < window.ToUtc)
                .GroupBy(a => a.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            view.Charts.Add(new ChartDefinition
            {
                Key = "attendance-mix",
                Title = "Attendance",
                Caption = "Days recorded, by what was recorded.",
                ChartType = ChartTypes.Donut,
                ValueFormat = KpiFormats.Number,
                Labels = attendance.Select(a => a.Status).ToList(),
                Series = new List<ChartSeries>
                {
                    new() { Name = "Days", Values = attendance.Select(a => (decimal)a.Count).ToList() }
                }
            });

            var byEmployee = await _context.Attendances
                .AsNoTracking()
                .Where(a => a.Date >= window.FromUtc && a.Date < window.ToUtc)
                .GroupBy(a => new { a.EmployeeId, a.Employee.FirstName, a.Employee.LastName })
                .Select(g => new
                {
                    Name = g.Key.FirstName + " " + g.Key.LastName,
                    Days = g.Count(),
                    Regular = g.Sum(a => a.RegularHours),
                    Overtime = g.Sum(a => a.OvertimeHours),
                    Absent = g.Count(a => a.Status == "Absent")
                })
                .OrderByDescending(g => g.Regular)
                .ToListAsync();

            view.Tables.Add(new AnalyticsTable
            {
                Key = "attendance-by-employee",
                Title = "Attendance by employee",
                Columns = new List<string>
                {
                    "Employee", "Days recorded", "Regular hours", "Overtime hours", "Absences"
                },
                Rows = byEmployee.Select(e => new List<string>
                {
                    e.Name, e.Days.ToString("N0"),
                    e.Regular.ToString("N2"), e.Overtime.ToString("N2"), e.Absent.ToString("N0")
                }).ToList()
            });
        }

        private async Task BuildFinanceAsync(AnalyticsView view, Window window)
        {
            view.Groups.Add(await FinanceKpisAsync(window));

            view.Charts.Add(await RevenueVersusExpensesAsync(window));

            var cash = await _finance.GetCashFlowAsync(window.FromUtc, window.ToUtc.AddDays(-1));

            view.Charts.Add(new ChartDefinition
            {
                Key = "cash-position",
                Title = "Cash position",
                Caption = "The running balance of cash and bank through the period.",
                ChartType = ChartTypes.Line,
                ValueFormat = KpiFormats.Money,
                Labels = cash.ByDay.Select(p => p.Label).ToList(),
                Series = new List<ChartSeries>
                {
                    new() { Name = "Cash on hand", Values = cash.ByDay.Select(p => p.Value).ToList() }
                }
            });

            var statement = await _finance.GetIncomeStatementAsync(
                window.FromUtc, window.ToUtc.AddDays(-1));

            var expenseLines = statement.ExpenseLines
                .Where(l => l.IsSubtotal && l.Label != "Total expenses" && l.Amount != 0m)
                .ToList();

            view.Charts.Add(new ChartDefinition
            {
                Key = "expense-breakdown",
                Title = "Where the money goes",
                Caption = "Operating expenses grouped as the income statement shows them.",
                ChartType = ChartTypes.Donut,
                ValueFormat = KpiFormats.Money,
                Labels = expenseLines.Select(l => l.Label).ToList(),
                Series = new List<ChartSeries>
                {
                    new() { Name = "Spent", Values = expenseLines.Select(l => l.Amount).ToList() }
                }
            });

            view.Tables.Add(new AnalyticsTable
            {
                Key = "income-statement",
                Title = "Income statement",
                Columns = new List<string> { "Line", "Amount" },
                Rows = statement.RevenueLines
                    .Concat(statement.CostOfSalesLines)
                    .Concat(statement.ExpenseLines)
                    .Select(l => new List<string>
                    {
                        new string(' ', l.Depth * 2) + l.Label,
                        l.Amount.ToString("N2")
                    })
                    .Append(new List<string> { "Net income", statement.NetIncome.ToString("N2") })
                    .ToList()
            });
        }

        private async Task BuildProfitabilityAsync(AnalyticsView view, Window window)
        {
            var current = await _finance.GetIncomeStatementAsync(
                window.FromUtc, window.ToUtc.AddDays(-1));

            var previous = await _finance.GetIncomeStatementAsync(
                window.ComparisonFromUtc, window.ComparisonToUtc.AddDays(-1));

            view.Groups.Add(new KpiGroup
            {
                Key = "profitability",
                Title = "Profitability",
                Cards = new List<KpiCard>
                {
                    Money("net-revenue", "Net revenue", current.NetRevenue, previous.NetRevenue),
                    Money("cogs", "Cost of goods sold", current.CostOfGoodsSold,
                        previous.CostOfGoodsSold, riseIsGood: false),
                    Money("gross-profit", "Gross profit", current.GrossProfit, previous.GrossProfit),
                    Percent("gross-margin", "Gross margin",
                        current.GrossMarginPercent, previous.GrossMarginPercent),
                    Money("operating-expenses", "Operating expenses",
                        current.OperatingExpenses, previous.OperatingExpenses, riseIsGood: false),
                    Money("payroll", "Payroll cost",
                        current.PayrollExpenses, previous.PayrollExpenses, riseIsGood: false),
                    Money("net-income", "Net income", current.NetIncome, previous.NetIncome),
                    Percent("net-margin", "Net margin",
                        current.NetMarginPercent, previous.NetMarginPercent)
                }
            });

            var months = window.TrailingMonths(12);
            var (revenue, cost, profit) = await MonthlyProfitabilityAsync(months);

            view.Charts.Add(new ChartDefinition
            {
                Key = "margin-trend",
                Title = "Revenue, cost of sales and net income",
                Caption = "Twelve months. The gap between the first two is gross profit.",
                ChartType = ChartTypes.Line,
                ValueFormat = KpiFormats.Money,
                Labels = months.Select(m => m.ToString("MMM yy")).ToList(),
                Series = new List<ChartSeries>
                {
                    new() { Name = "Net revenue", Values = revenue },
                    new() { Name = "Cost of goods sold", Values = cost },
                    new() { Name = "Net income", Values = profit }
                }
            });

            var products = await _context.SaleItems
                .AsNoTracking()
                .Where(i => i.Sale.Status == "Completed" &&
                            i.Sale.SaleDate >= window.FromUtc && i.Sale.SaleDate < window.ToUtc)
                .GroupBy(i => new { i.ProductId, i.Product.ProductName })
                .Select(g => new
                {
                    g.Key.ProductName,
                    Revenue = g.Sum(i => i.Quantity * i.UnitPrice),
                    Cost = g.Sum(i => i.Quantity * i.UnitCost)
                })
                .OrderByDescending(g => g.Revenue - g.Cost)
                .Take(15)
                .ToListAsync();

            view.Charts.Add(new ChartDefinition
            {
                Key = "margin-by-product",
                Title = "Gross profit by product",
                Caption = "What each line actually contributes after what it cost to buy.",
                ChartType = ChartTypes.HorizontalBar,
                ValueFormat = KpiFormats.Money,
                Labels = products.Select(p => p.ProductName).ToList(),
                Series = new List<ChartSeries>
                {
                    new() { Name = "Gross profit", Values = products.Select(p => p.Revenue - p.Cost).ToList() }
                }
            });
        }

        /// <summary>
        /// Net revenue, cost of goods sold and net income for each of the given months, in one
        /// query.
        ///
        /// This used to ask <see cref="IFinanceReportService.GetIncomeStatementAsync"/> for a
        /// complete statement per month. That is twelve statements for one chart, each of them
        /// several round trips - about forty against a database that is not on this machine -
        /// and the endpoint took fifteen seconds. Slow is the mild symptom: under any
        /// concurrency the query timed out and the screen got a 500 instead of a chart.
        ///
        /// A monthly trend does not need twelve statements. It needs the posted revenue and
        /// expense lines grouped by month, which is one query, and the arithmetic is then done
        /// here. The figures match the statement exactly because they use the same definitions:
        /// a revenue balance is credit less debit, so contra-revenue - discounts and returns,
        /// which are debited - nets itself out of revenue without being handled separately;
        /// cost of sales is the expense accounts marked as such; and net income is net revenue
        /// less every expense, cost of sales included.
        /// </summary>
        private async Task<(List<decimal> Revenue, List<decimal> Cost, List<decimal> NetIncome)>
            MonthlyProfitabilityAsync(List<DateTime> months)
        {
            var from = months[0];
            var to = months[^1].AddMonths(1);

            var rows = await _context.JournalEntryLines
                .AsNoTracking()
                .Where(l => l.Entry.Status == JournalStatuses.Posted &&
                            l.Entry.EntryDate >= from &&
                            l.Entry.EntryDate < to &&
                            (l.Account.AccountType == AccountTypes.Revenue ||
                             l.Account.AccountType == AccountTypes.Expense))
                .GroupBy(l => new
                {
                    l.Entry.PeriodYear,
                    l.Entry.PeriodMonth,
                    l.Account.AccountType,
                    l.Account.AccountSubType
                })
                .Select(g => new
                {
                    g.Key.PeriodYear,
                    g.Key.PeriodMonth,
                    g.Key.AccountType,
                    g.Key.AccountSubType,
                    Debit = g.Sum(l => l.Debit),
                    Credit = g.Sum(l => l.Credit)
                })
                .ToListAsync();

            var revenue = new List<decimal>();
            var cost = new List<decimal>();
            var netIncome = new List<decimal>();

            foreach (var month in months)
            {
                var forMonth = rows
                    .Where(r => r.PeriodYear == month.Year && r.PeriodMonth == month.Month)
                    .ToList();

                var monthRevenue = forMonth
                    .Where(r => r.AccountType == AccountTypes.Revenue)
                    .Sum(r => r.Credit - r.Debit);

                var monthExpenses = forMonth
                    .Where(r => r.AccountType == AccountTypes.Expense)
                    .Sum(r => r.Debit - r.Credit);

                var monthCost = forMonth
                    .Where(r => r.AccountType == AccountTypes.Expense &&
                                string.Equals(r.AccountSubType, ChartOfAccounts.CostOfSales,
                                    StringComparison.Ordinal))
                    .Sum(r => r.Debit - r.Credit);

                revenue.Add(monthRevenue);
                cost.Add(monthCost);
                netIncome.Add(monthRevenue - monthExpenses);
            }

            return (revenue, cost, netIncome);
        }

        // ================================================================== KPI groups

        private async Task<KpiGroup> MembershipKpisAsync(Window window)
        {
            var now = DateTime.UtcNow;

            var members = await _context.Members
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Active = g.Count(m => m.Status == "Active"),
                    NewInWindow = g.Count(m => m.JoinDate >= window.FromUtc && m.JoinDate < window.ToUtc),
                    NewInPrevious = g.Count(m =>
                        m.JoinDate >= window.ComparisonFromUtc && m.JoinDate < window.ComparisonToUtc)
                })
                .FirstOrDefaultAsync();

            var subscriptions = await _context.Subscriptions
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Active = g.Count(s => s.Status == "Active"),
                    Expired = g.Count(s => s.Status == "Expired"),
                    Expiring = g.Count(s =>
                        s.Status == "Active" && s.EndDate >= now && s.EndDate <= now.AddDays(7))
                })
                .FirstOrDefaultAsync();

            // A renewal is an existing subscription whose start date moved into the window -
            // renewing extends in place, so there is no second row to count.
            var renewals = await _context.Subscriptions
                .AsNoTracking()
                .CountAsync(s => s.StartDate >= window.FromUtc && s.StartDate < window.ToUtc &&
                                 s.CreatedAt < window.FromUtc);

            var previousRenewals = await _context.Subscriptions
                .AsNoTracking()
                .CountAsync(s => s.StartDate >= window.ComparisonFromUtc &&
                                 s.StartDate < window.ComparisonToUtc &&
                                 s.CreatedAt < window.ComparisonFromUtc);

            var total = members?.Total ?? 0;
            var active = members?.Active ?? 0;

            return new KpiGroup
            {
                Key = "membership",
                Title = "Membership",
                Cards = new List<KpiCard>
                {
                    Count("total-members", "Total members", total, total - (members?.NewInWindow ?? 0)),
                    Count("active-members", "Active members", active, active),
                    Count("expired-members", "Expired memberships",
                        subscriptions?.Expired ?? 0, subscriptions?.Expired ?? 0, riseIsGood: false),
                    Count("new-members", "New members",
                        members?.NewInWindow ?? 0, members?.NewInPrevious ?? 0),
                    Count("renewals", "Renewals", renewals, previousRenewals),
                    Count("expiring", "Expiring within 7 days",
                        subscriptions?.Expiring ?? 0, subscriptions?.Expiring ?? 0, riseIsGood: false),
                    Percent("retention", "Active share of members",
                        total == 0 ? 0m : Math.Round((decimal)active / total * 100m, 1),
                        total == 0 ? 0m : Math.Round((decimal)active / total * 100m, 1))
                }
            };
        }

        private async Task<KpiGroup> SalesKpisAsync(Window window)
        {
            var current = await SalesTotalsAsync(window.FromUtc, window.ToUtc);
            var previous = await SalesTotalsAsync(window.ComparisonFromUtc, window.ComparisonToUtc);

            return new KpiGroup
            {
                Key = "sales",
                Title = "Sales",
                Cards = new List<KpiCard>
                {
                    Money("sales-revenue", "Sales revenue", current.Revenue, previous.Revenue),
                    Count("transactions", "Transactions", current.Count, previous.Count),
                    Money("average-sale", "Average sale", current.Average, previous.Average),
                    Count("units-sold", "Units sold", current.Units, previous.Units),
                    Money("discounts", "Discounts given",
                        current.Discounts, previous.Discounts, riseIsGood: false),
                    Money("gross-profit", "Gross profit on sales",
                        current.Revenue - current.Cost, previous.Revenue - previous.Cost)
                }
            };
        }

        private async Task<KpiGroup> PaymentKpisAsync(Window window)
        {
            var current = await PaymentTotalsAsync(window.FromUtc, window.ToUtc);
            var previous = await PaymentTotalsAsync(window.ComparisonFromUtc, window.ComparisonToUtc);

            var aged = await _finance.GetReceivablesAsync(window.ToUtc.AddDays(-1));

            // Collected against everything billed in the window, which is what "are we
            // actually getting paid?" means.
            var billed = current.Collected + aged.Total;

            return new KpiGroup
            {
                Key = "payments",
                Title = "Payments",
                Cards = new List<KpiCard>
                {
                    Money("collected", "Collected", current.Collected, previous.Collected),
                    Count("payment-count", "Payments taken", current.Count, previous.Count),
                    Money("pending", "Pending", current.Pending, previous.Pending, riseIsGood: false),
                    Money("outstanding", "Outstanding", aged.Total, aged.Total, riseIsGood: false),
                    Money("overdue", "Overdue past 30 days",
                        aged.Days31To60 + aged.Days61To90 + aged.Over90Days,
                        aged.Days31To60 + aged.Days61To90 + aged.Over90Days, riseIsGood: false),
                    Percent("collection-rate", "Collection rate",
                        billed == 0m ? 0m : Math.Round(current.Collected / billed * 100m, 1),
                        billed == 0m ? 0m : Math.Round(current.Collected / billed * 100m, 1))
                }
            };
        }

        private async Task<KpiGroup> InventoryKpisAsync(Window window)
        {
            var stock = await _context.Inventories
                .AsNoTracking()
                .Select(i => new
                {
                    i.QuantityOnHand,
                    i.ReorderLevel,
                    i.AverageCost,
                    CostPrice = i.Product != null ? i.Product.CostPrice : 0m,
                    UnitPrice = i.Product != null ? i.Product.UnitPrice : 0m
                })
                .ToListAsync();

            var value = stock.Sum(s => s.QuantityOnHand *
                (s.AverageCost > 0m ? s.AverageCost : s.CostPrice));

            var cost = await _context.SaleItems
                .AsNoTracking()
                .Where(i => i.Sale.Status == "Completed" &&
                            i.Sale.SaleDate >= window.FromUtc && i.Sale.SaleDate < window.ToUtc)
                .SumAsync(i => (decimal?)(i.Quantity * i.UnitCost)) ?? 0m;

            var movements = await _context.StockMovements
                .AsNoTracking()
                .CountAsync(m => m.MovementDate >= window.FromUtc && m.MovementDate < window.ToUtc);

            return new KpiGroup
            {
                Key = "inventory",
                Title = "Inventory",
                Cards = new List<KpiCard>
                {
                    Count("total-products", "Products", stock.Count, stock.Count),
                    Money("inventory-value", "Stock value", value, value),
                    Count("low-stock", "Low stock",
                        stock.Count(s => s.QuantityOnHand > 0m && s.ReorderLevel > 0m &&
                                         s.QuantityOnHand <= s.ReorderLevel),
                        0, riseIsGood: false),
                    Count("out-of-stock", "Out of stock",
                        stock.Count(s => s.QuantityOnHand <= 0m), 0, riseIsGood: false),
                    Count("movements", "Stock movements", movements, movements),

                    // Cost of goods sold over the average stock held: how many times the shelf
                    // turned over. Low means money sitting still.
                    Decimals("turnover", "Stock turnover",
                        value == 0m ? 0m : Math.Round(cost / value, 2),
                        value == 0m ? 0m : Math.Round(cost / value, 2))
                }
            };
        }

        private async Task<KpiGroup> WorkforceKpisAsync(Window window)
        {
            var employees = await _context.Employees
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Active = g.Count(e => e.Status == "Active")
                })
                .FirstOrDefaultAsync();

            var attendance = await _context.Attendances
                .AsNoTracking()
                .Where(a => a.Date >= window.FromUtc && a.Date < window.ToUtc)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Days = g.Count(),
                    Present = g.Count(a => a.Status == "Present" || a.Status == "Late"),
                    Regular = g.Sum(a => a.RegularHours),
                    Overtime = g.Sum(a => a.OvertimeHours)
                })
                .FirstOrDefaultAsync();

            var leave = await _context.LeaveRequests
                .AsNoTracking()
                .Where(r => r.Status == LeaveStatuses.Approved &&
                            r.StartDate < window.ToUtc && r.EndDate >= window.FromUtc)
                .SumAsync(r => (decimal?)r.Days) ?? 0m;

            var recorded = attendance?.Days ?? 0;

            return new KpiGroup
            {
                Key = "workforce",
                Title = "Employees",
                Cards = new List<KpiCard>
                {
                    Count("total-employees", "Employees", employees?.Total ?? 0, employees?.Total ?? 0),
                    Count("active-employees", "Active", employees?.Active ?? 0, employees?.Active ?? 0),
                    Decimals("regular-hours", "Regular hours",
                        attendance?.Regular ?? 0m, attendance?.Regular ?? 0m),
                    Decimals("overtime-hours", "Overtime hours",
                        attendance?.Overtime ?? 0m, attendance?.Overtime ?? 0m, riseIsGood: false),
                    Percent("attendance-rate", "Attendance rate",
                        recorded == 0 ? 0m : Math.Round((decimal)(attendance?.Present ?? 0) / recorded * 100m, 1),
                        recorded == 0 ? 0m : Math.Round((decimal)(attendance?.Present ?? 0) / recorded * 100m, 1)),
                    Decimals("leave-days", "Leave days taken", leave, leave, riseIsGood: false)
                }
            };
        }

        private async Task<KpiGroup> PayrollKpisAsync(Window window)
        {
            var current = await PayrollTotalsAsync(window.FromUtc, window.ToUtc);
            var previous = await PayrollTotalsAsync(window.ComparisonFromUtc, window.ComparisonToUtc);

            return new KpiGroup
            {
                Key = "payroll",
                Title = "Payroll",
                Cards = new List<KpiCard>
                {
                    Money("payroll-gross", "Gross payroll", current.Gross, previous.Gross, riseIsGood: false),
                    Money("payroll-net", "Net paid", current.Net, previous.Net, riseIsGood: false),
                    Money("overtime-cost", "Overtime cost",
                        current.Overtime, previous.Overtime, riseIsGood: false),
                    Money("employer-contributions", "Employer contributions",
                        current.Employer, previous.Employer, riseIsGood: false),
                    Money("employment-cost", "Total employment cost",
                        current.Gross + current.Employer, previous.Gross + previous.Employer,
                        riseIsGood: false),
                    Count("pay-runs", "Pay runs", current.Count, previous.Count)
                }
            };
        }

        /// <summary>
        /// The nine finance cards.
        ///
        /// These used to be filled by asking for four complete finance reports - two income
        /// statements, a balance sheet and a cash flow - which is about ten round trips to build
        /// nine numbers, and this group is one of seven on the KPI dashboard. The statements are
        /// the right thing to read when somebody wants a statement; for a row of cards they are
        /// most of a second each in latency for detail nothing here uses.
        ///
        /// Both windows now come from one grouped query - the comparison window ends exactly
        /// where the current one begins, so the union is contiguous and a single read covers
        /// both - and the receivable and payable balances from another. Cash flow is still asked
        /// for, because opening and closing cash genuinely is what that report computes.
        /// </summary>
        private async Task<KpiGroup> FinanceKpisAsync(Window window)
        {
            var periods = await _context.JournalEntryLines
                .AsNoTracking()
                .Where(l => l.Entry.Status == JournalStatuses.Posted &&
                            l.Entry.EntryDate >= window.ComparisonFromUtc &&
                            l.Entry.EntryDate < window.ToUtc &&
                            (l.Account.AccountType == AccountTypes.Revenue ||
                             l.Account.AccountType == AccountTypes.Expense))
                .GroupBy(l => new
                {
                    IsCurrent = l.Entry.EntryDate >= window.FromUtc,
                    l.Account.AccountType,
                    l.Account.AccountSubType
                })
                .Select(g => new PeriodTotal(
                    g.Key.IsCurrent,
                    g.Key.AccountType,
                    g.Key.AccountSubType,
                    g.Sum(l => l.Debit),
                    g.Sum(l => l.Credit)))
                .ToListAsync();

            var current = Summarise(periods.Where(p => p.IsCurrent));
            var previous = Summarise(periods.Where(p => !p.IsCurrent));

            // Receivables and payables are balances rather than period activity, so they are
            // taken over all history up to the end of the window, and identified by their
            // system key rather than by matching the word "Receivable" in a name an operator is
            // free to change.
            var outstanding = await _context.JournalEntryLines
                .AsNoTracking()
                .Where(l => l.Entry.Status == JournalStatuses.Posted &&
                            l.Entry.EntryDate < window.ToUtc &&
                            (l.Account.SystemKey == AccountKeys.AccountsReceivable ||
                             l.Account.SystemKey == AccountKeys.AccountsPayable))
                .GroupBy(l => new { l.Account.SystemKey, l.Account.AccountType })
                .Select(g => new
                {
                    g.Key.SystemKey,
                    g.Key.AccountType,
                    Debit = g.Sum(l => l.Debit),
                    Credit = g.Sum(l => l.Credit)
                })
                .ToListAsync();

            decimal BalanceFor(string systemKey) => outstanding
                .Where(o => string.Equals(o.SystemKey, systemKey, StringComparison.OrdinalIgnoreCase))
                .Sum(o => AccountTypes.BalanceOf(o.AccountType, o.Debit, o.Credit));

            var receivable = BalanceFor(AccountKeys.AccountsReceivable);
            var payable = BalanceFor(AccountKeys.AccountsPayable);

            var cash = await _finance.GetCashFlowAsync(window.FromUtc, window.ToUtc.AddDays(-1));

            return new KpiGroup
            {
                Key = "finance",
                Title = "Finance",
                Cards = new List<KpiCard>
                {
                    Money("total-revenue", "Total revenue", current.NetRevenue, previous.NetRevenue),
                    Money("total-expenses", "Total expenses",
                        current.AllExpenses, previous.AllExpenses, riseIsGood: false),
                    Money("cogs", "Cost of goods sold",
                        current.CostOfGoodsSold, previous.CostOfGoodsSold, riseIsGood: false),
                    Money("gross-profit", "Gross profit", current.GrossProfit, previous.GrossProfit),
                    Money("net-income", "Net income", current.NetIncome, previous.NetIncome),
                    Money("cash-flow", "Net cash flow", cash.NetCashFlow, 0m),
                    Money("cash-on-hand", "Cash on hand", cash.ClosingCash, cash.OpeningCash),
                    Money("receivables", "Accounts receivable", receivable, receivable, riseIsGood: false),
                    Money("payables", "Accounts payable", payable, payable, riseIsGood: false)
                }
            };
        }

        /// <summary>
        /// Turns grouped revenue and expense totals into the same figures the income statement
        /// reports, using the same definitions so the cards and the statement cannot disagree:
        /// a revenue balance is credit less debit, which nets discounts and returns out of
        /// revenue by itself, and cost of sales is the expense accounts marked as such.
        /// </summary>
        private readonly record struct PeriodTotal(
            bool IsCurrent, string AccountType, string AccountSubType, decimal Debit, decimal Credit);

        private static (decimal NetRevenue, decimal CostOfGoodsSold, decimal AllExpenses,
            decimal GrossProfit, decimal NetIncome) Summarise(IEnumerable<PeriodTotal> rows)
        {
            var netRevenue = 0m;
            var allExpenses = 0m;
            var costOfSales = 0m;

            foreach (var row in rows)
            {
                if (string.Equals(row.AccountType, AccountTypes.Revenue, StringComparison.Ordinal))
                {
                    netRevenue += row.Credit - row.Debit;
                    continue;
                }

                var amount = row.Debit - row.Credit;
                allExpenses += amount;

                if (string.Equals(row.AccountSubType, ChartOfAccounts.CostOfSales,
                        StringComparison.Ordinal))
                {
                    costOfSales += amount;
                }
            }

            return (netRevenue, costOfSales, allExpenses,
                netRevenue - costOfSales, netRevenue - allExpenses);
        }

        // ================================================================== shared charts

        private async Task<ChartDefinition> RevenueVersusExpensesAsync(Window window)
        {
            var months = window.TrailingMonths(12);

            var lines = await _context.JournalEntryLines
                .AsNoTracking()
                .Where(l => l.Entry.Status == JournalStatuses.Posted &&
                            l.Entry.EntryDate >= months[0] && l.Entry.EntryDate < window.ToUtc &&
                            (l.Account.AccountType == AccountTypes.Revenue ||
                             l.Account.AccountType == AccountTypes.Expense))
                .Select(l => new
                {
                    l.Entry.PeriodYear,
                    l.Entry.PeriodMonth,
                    l.Account.AccountType,
                    l.Debit,
                    l.Credit
                })
                .ToListAsync();

            decimal Revenue(DateTime m) => lines
                .Where(l => l.PeriodYear == m.Year && l.PeriodMonth == m.Month &&
                            l.AccountType == AccountTypes.Revenue)
                .Sum(l => l.Credit - l.Debit);

            decimal Expenses(DateTime m) => lines
                .Where(l => l.PeriodYear == m.Year && l.PeriodMonth == m.Month &&
                            l.AccountType == AccountTypes.Expense)
                .Sum(l => l.Debit - l.Credit);

            return new ChartDefinition
            {
                Key = "revenue-vs-expenses",
                Title = "Revenue, expenses and net income",
                Caption = "Twelve months from the general ledger, so it agrees with the statements.",
                ChartType = ChartTypes.Line,
                ValueFormat = KpiFormats.Money,
                Labels = months.Select(m => m.ToString("MMM yy")).ToList(),
                Series = new List<ChartSeries>
                {
                    new() { Name = "Revenue", Values = months.Select(Revenue).ToList() },
                    new() { Name = "Expenses", Values = months.Select(Expenses).ToList() },
                    new()
                    {
                        Name = "Net income",
                        Values = months.Select(m => Revenue(m) - Expenses(m)).ToList()
                    }
                }
            };
        }

        private async Task<ChartDefinition> SalesByDayAsync(Window window)
        {
            var sales = await _context.Sales
                .AsNoTracking()
                .Where(s => s.Status == "Completed" &&
                            s.SaleDate >= window.FromUtc && s.SaleDate < window.ToUtc)
                .Select(s => new { s.SaleDate, s.TotalAmount })
                .ToListAsync();

            var days = window.Days();

            return new ChartDefinition
            {
                Key = "sales-by-day",
                Title = "Sales by day",
                Caption = "Takings over the period, day by day.",
                ChartType = ChartTypes.Area,
                ValueFormat = KpiFormats.Money,
                Labels = days.Select(d => d.ToString("d MMM")).ToList(),
                Series = new List<ChartSeries>
                {
                    new()
                    {
                        Name = "Sales",
                        Values = days.Select(d => sales
                            .Where(s => s.SaleDate.Date == d)
                            .Sum(s => s.TotalAmount)).ToList()
                    }
                }
            };
        }

        private async Task<ChartDefinition> MemberStatusMixAsync()
        {
            var rows = await _context.Members
                .AsNoTracking()
                .GroupBy(m => m.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToListAsync();

            return new ChartDefinition
            {
                Key = "member-status",
                Title = "Members by status",
                Caption = "The shape of the membership roll right now.",
                ChartType = ChartTypes.Donut,
                ValueFormat = KpiFormats.Number,
                Labels = rows.Select(r => r.Status).ToList(),
                Series = new List<ChartSeries>
                {
                    new() { Name = "Members", Values = rows.Select(r => (decimal)r.Count).ToList() }
                }
            };
        }

        // ================================================================== totals

        private sealed record SalesTotals(
            decimal Revenue, decimal Cost, decimal Discounts, int Count, int Units)
        {
            public decimal Average => Count == 0 ? 0m : Math.Round(Revenue / Count, 2);
        }

        private async Task<SalesTotals> SalesTotalsAsync(DateTime from, DateTime to)
        {
            var sales = await _context.Sales
                .AsNoTracking()
                .Where(s => s.Status == "Completed" && s.SaleDate >= from && s.SaleDate < to)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Revenue = g.Sum(s => s.TotalAmount),
                    Discounts = g.Sum(s => s.Discount),
                    Count = g.Count()
                })
                .FirstOrDefaultAsync();

            var items = await _context.SaleItems
                .AsNoTracking()
                .Where(i => i.Sale.Status == "Completed" &&
                            i.Sale.SaleDate >= from && i.Sale.SaleDate < to)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Units = g.Sum(i => i.Quantity),
                    Cost = g.Sum(i => i.Quantity * i.UnitCost)
                })
                .FirstOrDefaultAsync();

            return new SalesTotals(
                sales?.Revenue ?? 0m, items?.Cost ?? 0m, sales?.Discounts ?? 0m,
                sales?.Count ?? 0, items?.Units ?? 0);
        }

        private sealed record PaymentTotals(decimal Collected, decimal Pending, int Count);

        private async Task<PaymentTotals> PaymentTotalsAsync(DateTime from, DateTime to)
        {
            var rows = await _context.Payments
                .AsNoTracking()
                .Where(p => p.PaymentDate >= from && p.PaymentDate < to)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Collected = g.Where(p => p.Status == "Completed").Sum(p => p.Amount),
                    Pending = g.Where(p => p.Status == "Pending").Sum(p => p.Amount),
                    Count = g.Count(p => p.Status == "Completed")
                })
                .FirstOrDefaultAsync();

            return new PaymentTotals(rows?.Collected ?? 0m, rows?.Pending ?? 0m, rows?.Count ?? 0);
        }

        private sealed record PayrollTotals(
            decimal Gross, decimal Net, decimal Overtime, decimal Employer, int Count);

        private async Task<PayrollTotals> PayrollTotalsAsync(DateTime from, DateTime to)
        {
            var rows = await _context.Payrolls
                .AsNoTracking()
                .Where(p => p.PeriodEnd >= from && p.PeriodEnd < to)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Gross = g.Sum(p => p.GrossPay),
                    Net = g.Sum(p => p.NetPay),
                    Overtime = g.Sum(p => p.OvertimePay),
                    Employer = g.Sum(p => p.EmployerContributions),
                    Count = g.Count()
                })
                .FirstOrDefaultAsync();

            return new PayrollTotals(
                rows?.Gross ?? 0m, rows?.Net ?? 0m, rows?.Overtime ?? 0m,
                rows?.Employer ?? 0m, rows?.Count ?? 0);
        }

        // ================================================================== plumbing

        /// <summary>
        /// The window a report covers and the equal-length window before it.
        ///
        /// Comparing against "the same length of time immediately before" rather than against
        /// the same month last year is deliberate: a gym's week-on-week movement is what an
        /// operator can act on, and a year of history is not something a new tenant has.
        /// </summary>
        private sealed record Window(
            DateTime FromUtc, DateTime ToUtc, DateTime ComparisonFromUtc, DateTime ComparisonToUtc)
        {
            public static Window Resolve(DateTime? from, DateTime? to)
            {
                var range = ReportRange.Resolve(from, to);
                var length = range.ToUtc - range.FromUtc;

                if (length <= TimeSpan.Zero) length = TimeSpan.FromDays(1);

                return new Window(
                    range.FromUtc, range.ToUtc,
                    range.FromUtc - length, range.FromUtc);
            }

            /// <summary>Every day in the window, capped so a year-long range is not 365 bars.</summary>
            public List<DateTime> Days()
            {
                var days = new List<DateTime>();
                var total = (int)(ToUtc - FromUtc).TotalDays;

                var start = total > 90 ? ToUtc.AddDays(-90) : FromUtc;

                for (var day = start.Date; day < ToUtc.Date; day = day.AddDays(1))
                {
                    days.Add(day);
                }

                return days.Count == 0 ? new List<DateTime> { FromUtc.Date } : days;
            }

            /// <summary>The months ending with the window's own month, oldest first.</summary>
            public List<DateTime> TrailingMonths(int count)
            {
                var last = new DateTime(ToUtc.Year, ToUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

                // The window's end is exclusive, so a range ending on the first of a month
                // belongs to the month before it rather than opening a new empty one.
                if (ToUtc.Day == 1) last = last.AddMonths(-1);

                return Enumerable.Range(0, count)
                    .Select(offset => last.AddMonths(-(count - 1 - offset)))
                    .ToList();
            }
        }

        private AnalyticsView NewView(string key, Window window) => new()
        {
            Key = key,
            FromUtc = window.FromUtc,
            ToUtc = window.ToUtc,
            ComparisonFromUtc = window.ComparisonFromUtc,
            ComparisonToUtc = window.ComparisonToUtc
        };

        private static KpiCard Card(
            string key, string label, decimal value, decimal previous,
            string format, bool riseIsGood, string hint)
        {
            var hasComparison = previous != 0m;

            return new KpiCard
            {
                Key = key,
                Label = label,
                Value = value,
                PreviousValue = previous,
                Format = format,
                RiseIsGood = riseIsGood,
                HasComparison = hasComparison,

                // Growth from nothing is not infinite, it is unmeasurable. Showing a
                // percentage there is worse than showing none.
                DeltaPercent = hasComparison
                    ? Math.Round((value - previous) / Math.Abs(previous) * 100m, 1)
                    : 0m,

                Hint = hint
            };
        }

        private static KpiCard Money(
            string key, string label, decimal value, decimal previous,
            bool riseIsGood = true, string hint = "") =>
            Card(key, label, value, previous, KpiFormats.Money, riseIsGood, hint);

        private static KpiCard Count(
            string key, string label, int value, int previous,
            bool riseIsGood = true, string hint = "") =>
            Card(key, label, value, previous, KpiFormats.Number, riseIsGood, hint);

        private static KpiCard Percent(
            string key, string label, decimal value, decimal previous,
            bool riseIsGood = true, string hint = "") =>
            Card(key, label, value, previous, KpiFormats.Percent, riseIsGood, hint);

        private static KpiCard Decimals(
            string key, string label, decimal value, decimal previous,
            bool riseIsGood = true, string hint = "") =>
            Card(key, label, value, previous, KpiFormats.Decimal, riseIsGood, hint);
    }
}
