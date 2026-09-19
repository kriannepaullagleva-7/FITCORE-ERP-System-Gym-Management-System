using System.ComponentModel.DataAnnotations;

namespace ERP_api.DTOs
{
    public class CreateSaleItemDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "A valid product must be selected.")]
        public int ProductId { get; set; }

        [Range(1, 1000000, ErrorMessage = "Quantity must be at least one.")]
        public int Quantity { get; set; }
    }

    public class CreateSaleDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "A valid member must be selected.")]
        public int MemberId { get; set; }

        [MinLength(1, ErrorMessage = "A sale must have at least one line.")]
        public List<CreateSaleItemDto> Items { get; set; } = new();

        [Range(0, 10000000, ErrorMessage = "Discount cannot be negative.")]
        public decimal Discount { get; set; }

        /// <summary>Optional: the member of staff who rang the sale up.</summary>
        public int? CashierEmployeeId { get; set; }

        [StringLength(300)]
        public string Notes { get; set; } = "";
    }

    public class CancelSaleDto
    {
        [StringLength(200)]
        public string Reason { get; set; } = "";
    }

    public class SaleItemDto
    {
        public int SaleItemId { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    public class SaleDto
    {
        public int SaleId { get; set; }
        public int MemberId { get; set; }
        public DateTime SaleDate { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Discount { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "";
        public int? CashierEmployeeId { get; set; }
        public string Notes { get; set; } = "";
        public List<SaleItemDto> Items { get; set; } = new();
    }
}
