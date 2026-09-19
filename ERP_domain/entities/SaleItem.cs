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

        // Line total, derived from quantity and unit price - never stored.
        [NotMapped]
        public decimal Subtotal => Quantity * UnitPrice;

        // Foreign keys
        public Sale Sale { get; set; } = null!;
        public Product Product { get; set; } = null!;
    }
}
