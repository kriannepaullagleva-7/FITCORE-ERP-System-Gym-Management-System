using System;
using System.Collections.Generic;

namespace ERP_infrastructure.services
{
    // One row per sales transaction for the Sales grid. The payment figures come from the
    // payments actually recorded against the sale, so "Partially Paid" is a fact about the
    // database rather than something the screen decides.
    public class SaleView
    {
        public int SaleId { get; set; }
        public int MemberId { get; set; }
        public string MemberName { get; set; } = string.Empty;
        public DateTime SaleDate { get; set; }
        public int ItemCount { get; set; }
        public int TotalQuantity { get; set; }

        public decimal Subtotal { get; set; }
        public decimal Discount { get; set; }
        public decimal TotalAmount { get; set; }

        public string Status { get; set; } = "Completed";
        public int? CashierEmployeeId { get; set; }
        public string CashierName { get; set; } = "- unassigned -";
        public string Notes { get; set; } = string.Empty;

        public decimal AmountPaid { get; set; }
        public decimal Balance { get; set; }

        /// <summary>Paid, Partially Paid, Unpaid, or Cancelled.</summary>
        public string PaymentStatus { get; set; } = "Unpaid";
    }

    // One line item within a sale.
    public class SaleLineView
    {
        public int SaleItemId { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Subtotal => Quantity * UnitPrice;
    }

    // A sale together with its lines, for the detail view.
    public class SaleDetailView
    {
        public SaleView Sale { get; set; } = new();
        public List<SaleLineView> Lines { get; set; } = new();
        public List<PaymentView> Payments { get; set; } = new();
    }

    // A line the cashier has staged but not yet committed as a sale.
    public class SaleCartLine
    {
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Subtotal => Quantity * UnitPrice;
    }

    // What the caller asks for when creating a sale. Prices are resolved on the server from
    // the product record, so a browser cannot dictate what something costs.
    public class SaleLineRequest
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }
}
