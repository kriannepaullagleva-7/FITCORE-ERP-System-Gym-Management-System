using System;
using System.Collections.Generic;

namespace ERP_infrastructure.services
{
    // Aggregated counters for the Dashboard module. Every figure is read live from
    // the tenant database when the dashboard is refreshed.
    public class DashboardSummary
    {
        // ---------------------------------------------------------------- Membership
        public int TotalMembers { get; set; }
        public int ActiveMembers { get; set; }
        public int InactiveMembers { get; set; }

        /// <summary>Members who joined since the first of the current month.</summary>
        public int NewMembersThisMonth { get; set; }

        public int ActiveSubscriptions { get; set; }
        public int ExpiringSoon { get; set; }
        public int ExpiredSubscriptions { get; set; }

        /// <summary>Active subscriptions whose end date is today or within the next N days.</summary>
        public int ExpiringWithin1Day { get; set; }
        public int ExpiringWithin3Days { get; set; }
        public int ExpiringWithin7Days { get; set; }

        public int TotalPlans { get; set; }

        // ---------------------------------------------------------------- Inventory
        public int TotalProducts { get; set; }
        public int LowStockProducts { get; set; }
        public int OutOfStockProducts { get; set; }
        public decimal InventoryValue { get; set; }

        // ---------------------------------------------------------------- Sales
        public int SalesCount { get; set; }
        public decimal SalesTotal { get; set; }
        public decimal SalesThisMonth { get; set; }
        public decimal SalesToday { get; set; }
        public int TransactionsToday { get; set; }
        public int TransactionsThisMonth { get; set; }

        // ---------------------------------------------------------------- Payments
        public int PaymentsCount { get; set; }
        public decimal PaymentsTotal { get; set; }
        public decimal PaymentsThisMonth { get; set; }
        public decimal PaymentsToday { get; set; }
        public int PendingPayments { get; set; }

        /// <summary>Money still owed across live sales and memberships.</summary>
        public decimal OutstandingBalance { get; set; }
        public int PartiallyPaidSales { get; set; }
        public int UnpaidSales { get; set; }

        // ---------------------------------------------------------------- Workforce and costs
        public int TotalEmployees { get; set; }
        public int ActiveEmployees { get; set; }
        public decimal PayrollThisMonth { get; set; }
        public decimal PayrollOutstanding { get; set; }

        /// <summary>Runs generated but not yet Approved or Paid.</summary>
        public int DraftPayrollRuns { get; set; }
        public decimal ExpensesThisMonth { get; set; }
        public decimal ExpensesTotal { get; set; }

        /// <summary>Money collected this month less expenses and payroll paid this month.</summary>
        public decimal NetThisMonth { get; set; }

        // ---------------------------------------------------------------- Charts
        /// <summary>Revenue per day for the last 14 days, oldest first.</summary>
        public List<TrendPoint> SalesTrend { get; set; } = new();

        /// <summary>Revenue per month for the last 6 months, oldest first.</summary>
        public List<TrendPoint> SalesByMonth { get; set; } = new();

        /// <summary>Money collected per day for the last 14 days, oldest first.</summary>
        public List<TrendPoint> PaymentTrend { get; set; } = new();

        /// <summary>Completed payment totals split by method.</summary>
        public List<CategorySlice> PaymentMethods { get; set; } = new();

        /// <summary>Active / Expiring Soon / Expired / No Plan member counts.</summary>
        public List<CategorySlice> MembershipBreakdown { get; set; } = new();

        /// <summary>In Stock / Low Stock / Out of Stock product counts.</summary>
        public List<CategorySlice> StockBreakdown { get; set; } = new();

        // ---------------------------------------------------------------- Recent activity
        public List<ActivityItem> RecentActivity { get; set; } = new();

        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>One point on a time series: a label, the date it covers and the value.</summary>
    public class TrendPoint
    {
        public string Label { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public decimal Value { get; set; }
        public int Count { get; set; }
    }

    /// <summary>One slice of a breakdown: a named bucket and how much is in it.</summary>
    public class CategorySlice
    {
        public string Label { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public int Count { get; set; }
    }

    /// <summary>One line in the recent-activity feed, drawn from a real record.</summary>
    public class ActivityItem
    {
        /// <summary>Sale, Payment, Member, Stock or Expense.</summary>
        public string Type { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public decimal? Amount { get; set; }
        public DateTime OccurredAt { get; set; }

        /// <summary>Route the UI can link to, e.g. "fitcore/sales".</summary>
        public string Link { get; set; } = string.Empty;
    }
}
