namespace ERP_Project1.Api
{
    /// <summary>One point on a time series returned by the API.</summary>
    public class TrendPointDto
    {
        public string Label { get; set; } = "";
        public DateTime Date { get; set; }
        public decimal Value { get; set; }
        public int Count { get; set; }
    }

    /// <summary>One named bucket in a breakdown.</summary>
    public class CategorySliceDto
    {
        public string Label { get; set; } = "";
        public decimal Value { get; set; }
        public int Count { get; set; }
    }

    /// <summary>One line in the dashboard activity feed.</summary>
    public class ActivityItemDto
    {
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public decimal? Amount { get; set; }
        public DateTime OccurredAt { get; set; }
        public string Link { get; set; } = "";
    }

    /// <summary>
    /// Mirrors the DashboardSummary the API returns from api/reports/dashboard. Every figure
    /// here is calculated from records in the tenant database.
    /// </summary>
    public class DashboardSummaryDto
    {
        public int TotalMembers { get; set; }
        public int ActiveMembers { get; set; }
        public int InactiveMembers { get; set; }
        public int NewMembersThisMonth { get; set; }
        public int ActiveSubscriptions { get; set; }
        public int ExpiringSoon { get; set; }
        public int ExpiredSubscriptions { get; set; }
        public int TotalPlans { get; set; }

        public int TotalProducts { get; set; }
        public int LowStockProducts { get; set; }
        public int OutOfStockProducts { get; set; }
        public decimal InventoryValue { get; set; }

        public int SalesCount { get; set; }
        public decimal SalesTotal { get; set; }
        public decimal SalesThisMonth { get; set; }
        public decimal SalesToday { get; set; }
        public int TransactionsToday { get; set; }
        public int TransactionsThisMonth { get; set; }

        public int PaymentsCount { get; set; }
        public decimal PaymentsTotal { get; set; }
        public decimal PaymentsThisMonth { get; set; }
        public decimal PaymentsToday { get; set; }
        public int PendingPayments { get; set; }
        public decimal OutstandingBalance { get; set; }
        public int PartiallyPaidSales { get; set; }
        public int UnpaidSales { get; set; }

        public int TotalEmployees { get; set; }
        public int ActiveEmployees { get; set; }
        public decimal PayrollThisMonth { get; set; }
        public decimal PayrollOutstanding { get; set; }
        public decimal ExpensesThisMonth { get; set; }
        public decimal ExpensesTotal { get; set; }
        public decimal NetThisMonth { get; set; }

        public List<TrendPointDto> SalesTrend { get; set; } = new();
        public List<TrendPointDto> SalesByMonth { get; set; } = new();
        public List<TrendPointDto> PaymentTrend { get; set; } = new();
        public List<CategorySliceDto> PaymentMethods { get; set; } = new();
        public List<CategorySliceDto> MembershipBreakdown { get; set; } = new();
        public List<CategorySliceDto> StockBreakdown { get; set; } = new();

        public List<ActivityItemDto> RecentActivity { get; set; } = new();

        public DateTime GeneratedAtUtc { get; set; }
    }

    /// <summary>
    /// Mirrors the tenant diagnostics the API returns from api/tenant/current. The API never
    /// sends a connection string, so there is nothing sensitive to model here.
    /// </summary>
    public class TenantInfoDto
    {
        public bool Resolved { get; set; }
        public int? CompanyId { get; set; }
        public string Source { get; set; } = string.Empty;
        public bool UsingFallbackConnection { get; set; }
    }

    // ------------------------------------------------------------------ Reports

    public class ProductSalesRowDto
    {
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string Category { get; set; } = "";
        public int QuantitySold { get; set; }
        public decimal Revenue { get; set; }
        public decimal Cost { get; set; }
        public decimal Margin { get; set; }
    }

    public class SalesReportDto
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public int TransactionCount { get; set; }
        public int CancelledCount { get; set; }
        public decimal GrossSales { get; set; }
        public decimal Discounts { get; set; }
        public decimal NetSales { get; set; }
        public decimal AverageSale { get; set; }
        public int UnitsSold { get; set; }
        public decimal Collected { get; set; }
        public decimal Outstanding { get; set; }
        public List<TrendPointDto> ByDay { get; set; } = new();
        public List<ProductSalesRowDto> TopProducts { get; set; } = new();
        public List<CategorySliceDto> ByCategory { get; set; } = new();
        public List<SaleViewDto> Sales { get; set; } = new();
    }

    public class OutstandingRowDto
    {
        public string Source { get; set; } = "";
        public int ReferenceId { get; set; }
        public int MemberId { get; set; }
        public string MemberName { get; set; } = "";
        public DateTime Date { get; set; }
        public decimal Total { get; set; }
        public decimal Paid { get; set; }
        public decimal Balance { get; set; }
        public string Status { get; set; } = "";
    }

    public class PaymentReportDto
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public int Count { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal Pending { get; set; }
        public decimal Refunded { get; set; }
        public decimal OutstandingFromSales { get; set; }
        public decimal OutstandingFromMemberships { get; set; }
        public decimal TotalOutstanding { get; set; }
        public int PaidCount { get; set; }
        public int PartiallyPaidCount { get; set; }
        public int UnpaidCount { get; set; }
        public List<CategorySliceDto> ByMethod { get; set; } = new();
        public List<TrendPointDto> ByDay { get; set; } = new();
        public List<OutstandingRowDto> Outstanding { get; set; } = new();
        public List<PaymentViewDto> Payments { get; set; } = new();
    }

    public class InventoryReportDto
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public InventorySummaryDto Summary { get; set; } = new();
        public List<InventoryDto> Stock { get; set; } = new();
        public List<InventoryDto> LowStock { get; set; } = new();
        public List<InventoryDto> OutOfStock { get; set; } = new();
        public List<StockMovementDto> Movements { get; set; } = new();
        public List<CategorySliceDto> ValueByCategory { get; set; } = new();
    }

    public class MembershipReportDto
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public int TotalMembers { get; set; }
        public int ActiveMembers { get; set; }
        public int InactiveMembers { get; set; }
        public int NewMembersInRange { get; set; }
        public int ActiveSubscriptions { get; set; }
        public int ExpiringSoon { get; set; }
        public int ExpiredSubscriptions { get; set; }
        public int CancelledSubscriptions { get; set; }
        public decimal MembershipRevenue { get; set; }
        public decimal MembershipOutstanding { get; set; }
        public List<CategorySliceDto> ByPlan { get; set; } = new();
        public List<TrendPointDto> JoinsByDay { get; set; } = new();
        public List<MembershipOverviewDto> Members { get; set; } = new();
    }

    public class ExpenseReportDto
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public int Count { get; set; }
        public decimal Total { get; set; }
        public decimal PayrollPaid { get; set; }
        public decimal TotalOutgoings { get; set; }
        public List<CategorySliceDto> ByCategory { get; set; } = new();
        public List<TrendPointDto> ByDay { get; set; } = new();
        public List<ExpenseDto> Expenses { get; set; } = new();
    }
}
