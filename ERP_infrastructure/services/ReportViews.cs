using System;
using System.Collections.Generic;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// The window a report covers. Held as an explicit pair so every report agrees on what
    /// "from 1 March to 31 March" means: inclusive of the first day, exclusive of the day
    /// after the last, which is how the queries are written.
    /// </summary>
    public class ReportRange
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public static ReportRange Resolve(DateTime? from, DateTime? to)
        {
            // Default to the current month, which is what a report screen is usually opened for.
            var now = DateTime.UtcNow;
            var start = from?.Date ?? new DateTime(now.Year, now.Month, 1);
            var endInclusive = to?.Date ?? now.Date;

            if (endInclusive < start) endInclusive = start;

            return new ReportRange
            {
                FromUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc),
                // Exclusive upper bound, so the whole of the last day is included.
                ToUtc = DateTime.SpecifyKind(endInclusive.AddDays(1), DateTimeKind.Utc)
            };
        }
    }

    // ---------------------------------------------------------------------- Sales

    public class ProductSalesRow
    {
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int QuantitySold { get; set; }
        public decimal Revenue { get; set; }
        public decimal Cost { get; set; }
        public decimal Margin => Revenue - Cost;
    }

    public class SalesReport
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

        public List<TrendPoint> ByDay { get; set; } = new();
        public List<ProductSalesRow> TopProducts { get; set; } = new();
        public List<CategorySlice> ByCategory { get; set; } = new();
        public List<SaleView> Sales { get; set; } = new();
    }

    // ---------------------------------------------------------------------- Payments

    public class OutstandingRow
    {
        public string Source { get; set; } = string.Empty; // "Sale" or "Membership"
        public int ReferenceId { get; set; }
        public int MemberId { get; set; }
        public string MemberName { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public decimal Total { get; set; }
        public decimal Paid { get; set; }
        public decimal Balance { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class PaymentReport
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public int Count { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal Pending { get; set; }
        public decimal Refunded { get; set; }

        public decimal OutstandingFromSales { get; set; }
        public decimal OutstandingFromMemberships { get; set; }
        public decimal TotalOutstanding => OutstandingFromSales + OutstandingFromMemberships;

        public int PaidCount { get; set; }
        public int PartiallyPaidCount { get; set; }
        public int UnpaidCount { get; set; }

        public List<CategorySlice> ByMethod { get; set; } = new();
        public List<TrendPoint> ByDay { get; set; } = new();
        public List<OutstandingRow> Outstanding { get; set; } = new();
        public List<PaymentView> Payments { get; set; } = new();
    }

    // ---------------------------------------------------------------------- Inventory

    public class InventoryReport
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public InventorySummary Summary { get; set; } = new();
        public List<InventoryView> Stock { get; set; } = new();
        public List<InventoryView> LowStock { get; set; } = new();
        public List<InventoryView> OutOfStock { get; set; } = new();
        public List<StockMovementView> Movements { get; set; } = new();
        public List<CategorySlice> ValueByCategory { get; set; } = new();
    }

    // ---------------------------------------------------------------------- Membership

    public class MembershipReport
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

        public List<CategorySlice> ByPlan { get; set; } = new();
        public List<TrendPoint> JoinsByDay { get; set; } = new();
        public List<MembershipView> Members { get; set; } = new();
    }

    // ---------------------------------------------------------------------- Expenses

    public class ExpenseReport
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public int Count { get; set; }
        public decimal Total { get; set; }
        public decimal PayrollPaid { get; set; }
        public decimal TotalOutgoings => Total + PayrollPaid;

        public List<CategorySlice> ByCategory { get; set; } = new();
        public List<TrendPoint> ByDay { get; set; } = new();
        public List<ExpenseView> Expenses { get; set; } = new();
    }
}
