using ERP_domain.entities;
using ERP_infrastructure.data;
using ERP_infrastructure.repositories;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    public class SaleService : ISaleService
    {
        private readonly ISaleRepository _saleRepo;
        private readonly IMemberRepository _memberRepo;
        private readonly IProductRepository _productRepo;
        private readonly IPaymentRepository _paymentRepo;
        private readonly TenantErpDbContext _context;

        public SaleService(
            ISaleRepository saleRepo,
            IMemberRepository memberRepo,
            IProductRepository productRepo,
            IPaymentRepository paymentRepo,
            TenantErpDbContext context)
        {
            _saleRepo = saleRepo;
            _memberRepo = memberRepo;
            _productRepo = productRepo;
            _paymentRepo = paymentRepo;
            _context = context;
        }

        /// <summary>
        /// Works out where a sale stands against the money actually received for it.
        /// A cancelled sale is reported as such rather than as an unpaid debt.
        /// </summary>
        public static string ResolvePaymentStatus(string saleStatus, decimal total, decimal paid)
        {
            if (string.Equals(saleStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
                return "Cancelled";

            if (paid >= total) return "Paid";
            if (paid > 0) return "Partially Paid";
            return "Unpaid";
        }

        private static SaleView ToView(Sale sale, decimal amountPaid)
        {
            var balance = sale.TotalAmount - amountPaid;

            return new SaleView
            {
                SaleId = sale.SaleId,
                MemberId = sale.MemberId,
                MemberName = sale.Member == null
                    ? $"Member #{sale.MemberId}"
                    : $"{sale.Member.FirstName} {sale.Member.LastName}".Trim(),
                SaleDate = sale.SaleDate,
                ItemCount = sale.Items.Count,
                TotalQuantity = sale.Items.Sum(i => i.Quantity),
                Subtotal = sale.Subtotal,
                Discount = sale.Discount,
                TotalAmount = sale.TotalAmount,
                Status = sale.Status,
                CashierEmployeeId = sale.CashierEmployeeId,
                CashierName = sale.CashierEmployee == null
                    ? "- unassigned -"
                    : $"{sale.CashierEmployee.FirstName} {sale.CashierEmployee.LastName}".Trim(),
                Notes = sale.Notes,
                AmountPaid = amountPaid,
                Balance = balance < 0 ? 0m : balance,
                PaymentStatus = ResolvePaymentStatus(sale.Status, sale.TotalAmount, amountPaid)
            };
        }

        /// <summary>
        /// Attaches the paid totals to a batch of sales using one grouped query, rather than
        /// one query per row.
        /// </summary>
        private async Task<List<SaleView>> ToViewsAsync(List<Sale> sales)
        {
            if (sales.Count == 0) return new List<SaleView>();

            var paidTotals = await _paymentRepo.GetPaidTotalsBySaleAsync(sales.Select(s => s.SaleId));

            return sales
                .Select(s => ToView(s, paidTotals.TryGetValue(s.SaleId, out var paid) ? paid : 0m))
                .ToList();
        }

        public async Task<Sale?> GetSaleByIdAsync(int id)
        {
            return await _saleRepo.GetSaleWithItemsAsync(id);
        }

        public async Task<List<SaleView>> GetAllSalesAsync()
        {
            return await ToViewsAsync(await _saleRepo.GetAllWithDetailsAsync());
        }

        public async Task<List<SaleView>> GetSalesInRangeAsync(DateTime fromUtc, DateTime toUtc)
        {
            return await ToViewsAsync(await _saleRepo.GetInRangeAsync(fromUtc, toUtc));
        }

        public async Task<List<SaleView>> GetSalesForMemberAsync(int memberId)
        {
            return await ToViewsAsync(await _saleRepo.GetMemberSalesAsync(memberId));
        }

        public async Task<SaleView?> GetSaleViewAsync(int id)
        {
            var sale = await _saleRepo.GetSaleWithItemsAsync(id);
            if (sale == null) return null;

            return ToView(sale, await _paymentRepo.GetSalePaidTotalAsync(id));
        }

        public async Task<SaleDetailView?> GetSaleDetailAsync(int id)
        {
            var sale = await _saleRepo.GetSaleWithItemsAsync(id);
            if (sale == null) return null;

            var payments = await _paymentRepo.GetSalePaymentsAsync(id);
            var paid = payments
                .Where(p => p.Status == "Completed")
                .Sum(p => p.Amount);

            return new SaleDetailView
            {
                Sale = ToView(sale, paid),
                Lines = ToLines(sale),
                Payments = payments.Select(PaymentService.ToView).ToList()
            };
        }

        private static List<SaleLineView> ToLines(Sale sale) => sale.Items
            .Select(i => new SaleLineView
            {
                SaleItemId = i.SaleItemId,
                ProductId = i.ProductId,
                ProductCode = i.Product?.ProductCode ?? string.Empty,
                ProductName = i.Product?.ProductName ?? $"Product #{i.ProductId}",
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            })
            .ToList();

        public async Task<List<SaleLineView>> GetSaleLinesAsync(int saleId)
        {
            var sale = await _saleRepo.GetSaleWithItemsAsync(saleId);
            return sale == null ? new List<SaleLineView>() : ToLines(sale);
        }

        public async Task<List<Sale>> GetMemberSalesAsync(int memberId)
        {
            return await _saleRepo.GetMemberSalesAsync(memberId);
        }

        public async Task<Sale> CreateSaleAsync(
            int memberId,
            List<SaleLineRequest> items,
            decimal discount = 0m,
            int? cashierEmployeeId = null,
            string notes = "")
        {
            if (items == null || items.Count == 0)
                throw new InvalidOperationException("A sale needs at least one item.");

            if (discount < 0)
                throw new InvalidOperationException("Discount cannot be negative.");

            var member = await _memberRepo.GetByIdAsync(memberId);
            if (member == null) throw new InvalidOperationException("Member not found.");

            if (cashierEmployeeId.HasValue &&
                !await _context.Employees.AnyAsync(e => e.EmployeeId == cashierEmployeeId.Value))
            {
                throw new InvalidOperationException("The selected cashier is not a known employee.");
            }

            // The same product added twice is merged so stock is checked against the real total.
            var merged = items
                .GroupBy(i => i.ProductId)
                .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
                .ToList();

            foreach (var line in merged)
            {
                if (line.Quantity <= 0)
                    throw new InvalidOperationException("Item quantity must be greater than zero.");
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var sale = new Sale
                {
                    MemberId = memberId,
                    SaleDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    Status = "Completed",
                    CashierEmployeeId = cashierEmployeeId,
                    Notes = (notes ?? string.Empty).Trim(),
                    Items = new List<SaleItem>()
                };

                decimal subtotal = 0m;

                foreach (var line in merged)
                {
                    var product = await _productRepo.GetByIdAsync(line.ProductId);
                    if (product == null)
                        throw new InvalidOperationException($"Product {line.ProductId} not found.");

                    if (!product.IsActive)
                        throw new InvalidOperationException(
                            $"{product.ProductName} is inactive and cannot be sold.");

                    // The price is whatever the catalogue says right now; the browser does not
                    // get to name it. It is then frozen onto the line for the receipt.
                    sale.Items.Add(new SaleItem
                    {
                        ProductId = line.ProductId,
                        Quantity = line.Quantity,
                        UnitPrice = product.UnitPrice
                    });

                    subtotal += line.Quantity * product.UnitPrice;
                }

                if (discount > subtotal)
                    throw new InvalidOperationException(
                        $"Discount of {discount:N2} is more than the sale subtotal of {subtotal:N2}.");

                sale.Subtotal = subtotal;
                sale.Discount = discount;
                sale.TotalAmount = subtotal - discount;

                _context.Sales.Add(sale);
                await _context.SaveChangesAsync();

                // Stock only moves once the sale itself has an id to reference.
                foreach (var line in merged)
                    await DeductStockAsync(line.ProductId, line.Quantity, sale.SaleId);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return sale;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<SaleView?> CancelSaleAsync(int id, string reason = "")
        {
            var sale = await _saleRepo.GetSaleWithItemsAsync(id);
            if (sale == null) return null;

            if (string.Equals(sale.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This sale has already been cancelled.");

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                foreach (var item in sale.Items)
                    await ReturnStockAsync(item.ProductId, item.Quantity, sale.SaleId, "CANCEL");

                sale.Status = "Cancelled";

                var trimmed = (reason ?? string.Empty).Trim();
                if (trimmed.Length > 0)
                {
                    var note = $"Cancelled: {trimmed}";
                    sale.Notes = string.IsNullOrWhiteSpace(sale.Notes)
                        ? note
                        : $"{sale.Notes} | {note}";

                    if (sale.Notes.Length > 300) sale.Notes = sale.Notes[..300];
                }

                // Money already taken for a cancelled sale is refunded rather than deleted, so
                // the ledger still shows it happened.
                var payments = await _context.Payments
                    .Where(p => p.SaleId == id && p.Status == "Completed")
                    .ToListAsync();

                foreach (var payment in payments)
                    payment.Status = "Refunded";

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return ToView(sale, 0m);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> DeleteSaleAsync(int id)
        {
            var sale = await _saleRepo.GetSaleWithItemsAsync(id);
            if (sale == null) return false;

            // Deleting a sale that money was taken for would leave orphaned payments and a
            // ledger that no longer adds up. Cancelling keeps both sides intact.
            if (await _context.Payments.AnyAsync(p => p.SaleId == id))
            {
                throw new InvalidOperationException(
                    "Payments have been recorded against this sale, so it cannot be deleted. Cancel it instead.");
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                if (!string.Equals(sale.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var item in sale.Items)
                        await ReturnStockAsync(item.ProductId, item.Quantity, sale.SaleId, "VOID");
                }

                _context.Sales.Remove(sale);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task DeductStockAsync(int productId, int quantity, int saleId)
        {
            var inventory = await _context.Inventories
                .FirstOrDefaultAsync(i => i.ProductId == productId);

            if (inventory == null)
            {
                inventory = new Inventory
                {
                    ProductId = productId,
                    QuantityOnHand = 0m,
                    ReorderLevel = 0m,
                    LastUpdatedAt = DateTime.UtcNow
                };
                _context.Inventories.Add(inventory);
            }

            if (inventory.QuantityOnHand < quantity)
            {
                var product = await _productRepo.GetByIdAsync(productId);
                var name = product?.ProductName ?? $"Product #{productId}";
                throw new InvalidOperationException(
                    $"Not enough stock for {name}: {inventory.QuantityOnHand:N0} on hand, {quantity:N0} requested.");
            }

            var before = inventory.QuantityOnHand;
            inventory.QuantityOnHand -= quantity;
            inventory.LastUpdatedAt = DateTime.UtcNow;

            _context.StockMovements.Add(new StockMovement
            {
                ProductId = productId,
                MovementType = "Sale",
                Quantity = quantity,
                BalanceBefore = before,
                BalanceAfter = inventory.QuantityOnHand,
                Reference = $"SALE-{saleId}",
                Notes = "Stock issued for sale",
                MovementDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        }

        private async Task ReturnStockAsync(int productId, int quantity, int saleId, string prefix)
        {
            var inventory = await _context.Inventories
                .FirstOrDefaultAsync(i => i.ProductId == productId);

            if (inventory == null) return;

            var before = inventory.QuantityOnHand;
            inventory.QuantityOnHand += quantity;
            inventory.LastUpdatedAt = DateTime.UtcNow;

            _context.StockMovements.Add(new StockMovement
            {
                ProductId = productId,
                MovementType = "In",
                Quantity = quantity,
                BalanceBefore = before,
                BalanceAfter = inventory.QuantityOnHand,
                Reference = $"{prefix}-{saleId}",
                Notes = prefix == "CANCEL"
                    ? "Stock returned after the sale was cancelled"
                    : "Stock returned after the sale was deleted",
                MovementDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        }
    }
}
