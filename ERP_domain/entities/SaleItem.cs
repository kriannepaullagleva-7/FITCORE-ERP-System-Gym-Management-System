using System.ComponentModel.DataAnnotations.Schema;

namespace ERP_domain.entities
{
    public class SaleItem
    {
        public int SaleItemId { get; set; }
        public int SaleId { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }

        /// <summary>
        /// What one unit cost the gym at the moment it was sold, taken from the weighted
        /// average cost of the stock on hand.
        ///
        /// Stored on the line rather than looked up later because the average moves with every
        /// receipt: a line sold in January must keep charging January's cost to cost of goods
        /// sold, or last year's gross profit changes every time new stock arrives.
        /// </summary>
        public decimal UnitCost { get; set; }

        // Line total, derived from quantity and unit price - never stored.
        [NotMapped]
        public decimal Subtotal => Quantity * UnitPrice;

        /// <summary>What this line cost the gym. The cost-of-goods-sold side of the posting.</summary>
        [NotMapped]
        public decimal LineCost => Quantity * UnitCost;

        /// <summary>Gross profit on the line.</summary>
        [NotMapped]
        public decimal Margin => Subtotal - LineCost;

        // Foreign keys
        public Sale Sale { get; set; } = null!;
        public Product Product { get; set; } = null!;
    }
}
