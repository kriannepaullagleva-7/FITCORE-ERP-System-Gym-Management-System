using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    public class SaleReturnItemView
    {
        public int SaleReturnItemId { get; set; }
        public int SaleItemId { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal UnitCost { get; set; }
        public decimal LineTotal => Quantity * UnitPrice;
    }

    public class SaleReturnView
    {
        public int SaleReturnId { get; set; }
        public string ReturnNo { get; set; } = "";
        public int SaleId { get; set; }
        public int? MemberId { get; set; }
        public string MemberName { get; set; } = "";
        public DateTime ReturnDate { get; set; }
        public string Reason { get; set; } = "";
        public decimal Subtotal { get; set; }
        public decimal RefundAmount { get; set; }
        public string RefundMethod { get; set; } = "";
        public bool RestockedToInventory { get; set; }
        public string Status { get; set; } = "";
        public string Notes { get; set; } = "";
        public string ProcessedBy { get; set; } = "";
        public int ItemCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<SaleReturnItemView> Items { get; set; } = new();
    }

    /// <summary>One line an operator wants to send back, and how many of it.</summary>
    public class ReturnLineRequest
    {
        public int SaleItemId { get; set; }
        public int Quantity { get; set; }
    }

    /// <summary>
    /// What a sale still has available to return, so the till can offer it rather than the
    /// operator working it out and the server refusing them.
    /// </summary>
    public class ReturnableLineView
    {
        public int SaleItemId { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public int QuantitySold { get; set; }
        public int QuantityReturned { get; set; }
        public int QuantityReturnable => QuantitySold - QuantityReturned;
        public decimal UnitPrice { get; set; }
        public decimal UnitCost { get; set; }
    }

    /// <summary>
    /// Goods coming back over the counter.
    ///
    /// A return is its own transaction rather than an edit to the sale. The original stays
    /// exactly as it was rung up - which is what a receipt, a report and an audit all depend on
    /// - and the return records separately that some of it came back.
    /// </summary>
    public interface ISaleReturnService
    {
        Task<List<SaleReturnView>> GetReturnsAsync(
            DateTime? fromUtc = null, DateTime? toUtc = null, int? saleId = null);

        Task<SaleReturnView?> GetReturnAsync(int saleReturnId);

        /// <summary>What is still returnable on a sale, line by line.</summary>
        Task<List<ReturnableLineView>> GetReturnableLinesAsync(int saleId);

        Task<SaleReturnView> CreateReturnAsync(
            int saleId, IEnumerable<ReturnLineRequest> lines, string reason,
            decimal? refundAmount, string refundMethod, bool restockToInventory, string notes);

        Task<SaleReturnView?> CancelReturnAsync(int saleReturnId, string reason);
    }

    public class SaleReturnService : ISaleReturnService
    {
        private readonly TenantErpDbContext _context;
        private readonly IFinancePostingService _finance;
        private readonly ICurrentUserAccessor _actor;
        private readonly IAuditService _audit;

        public SaleReturnService(
            TenantErpDbContext context,
            IFinancePostingService finance,
            ICurrentUserAccessor actor,
            IAuditService audit)
        {
            _context = context;
            _finance = finance;
            _actor = actor;
            _audit = audit;
        }

        public async Task<List<SaleReturnView>> GetReturnsAsync(
            DateTime? fromUtc = null, DateTime? toUtc = null, int? saleId = null)
        {
            var query = _context.SaleReturns
                .AsNoTracking()
                .Include(r => r.Member)
                .Include(r => r.Items)
                .AsQueryable();

            if (fromUtc.HasValue) query = query.Where(r => r.ReturnDate >= fromUtc.Value);
            if (toUtc.HasValue) query = query.Where(r => r.ReturnDate < toUtc.Value);
            if (saleId.HasValue) query = query.Where(r => r.SaleId == saleId.Value);

            var rows = await query
                .OrderByDescending(r => r.ReturnDate)
                .ThenByDescending(r => r.SaleReturnId)
                .ToListAsync();

            return rows.Select(r => ToView(r, includeItems: false)).ToList();
        }

        public async Task<SaleReturnView?> GetReturnAsync(int saleReturnId)
        {
            var saleReturn = await _context.SaleReturns
                .AsNoTracking()
                .Include(r => r.Member)
                .Include(r => r.Items).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(r => r.SaleReturnId == saleReturnId);

            return saleReturn is null ? null : ToView(saleReturn, includeItems: true);
        }

        public async Task<List<ReturnableLineView>> GetReturnableLinesAsync(int saleId)
        {
            var items = await _context.SaleItems
                .AsNoTracking()
                .Include(i => i.Product)
                .Where(i => i.SaleId == saleId)
                .ToListAsync();

            if (items.Count == 0) return new List<ReturnableLineView>();

            // How much of each line has already gone back, counting only live returns. A
            // cancelled return released its quantity again.
            var alreadyReturned = await _context.SaleReturnItems
                .AsNoTracking()
                .Where(i => i.Return.SaleId == saleId && i.Return.Status == "Completed")
                .GroupBy(i => i.SaleItemId)
                .Select(g => new { SaleItemId = g.Key, Quantity = g.Sum(i => i.Quantity) })
                .ToListAsync();

            return items.Select(i => new ReturnableLineView
            {
                SaleItemId = i.SaleItemId,
                ProductId = i.ProductId,
                ProductCode = i.Product?.ProductCode ?? "",
                ProductName = i.Product?.ProductName ?? $"Product #{i.ProductId}",
                QuantitySold = i.Quantity,
                QuantityReturned =
                    alreadyReturned.FirstOrDefault(a => a.SaleItemId == i.SaleItemId)?.Quantity ?? 0,
                UnitPrice = i.UnitPrice,
                UnitCost = i.UnitCost
            }).ToList();
        }

        public async Task<SaleReturnView> CreateReturnAsync(
            int saleId, IEnumerable<ReturnLineRequest> lines, string reason,
            decimal? refundAmount, string refundMethod, bool restockToInventory, string notes)
        {
            var sale = await _context.Sales
                .Include(s => s.Items).ThenInclude(i => i.Product)
                .Include(s => s.Member)
                .FirstOrDefaultAsync(s => s.SaleId == saleId)
                ?? throw new ValidationException($"No sale with id {saleId} exists.");

            if (string.Equals(sale.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    $"Sale #{saleId} was cancelled, which already returned its stock. " +
                    "There is nothing left to send back.");
            }

            var requested = (lines ?? Enumerable.Empty<ReturnLineRequest>())
                .Where(l => l.Quantity > 0)
                .GroupBy(l => l.SaleItemId)
                .Select(g => new ReturnLineRequest
                {
                    SaleItemId = g.Key,
                    Quantity = g.Sum(l => l.Quantity)
                })
                .ToList();

            if (requested.Count == 0)
            {
                throw new ValidationException("Choose at least one item to return.");
            }

            var returnable = await GetReturnableLinesAsync(saleId);

            var prepared = new List<SaleReturnItem>();
            decimal subtotal = 0m;

            foreach (var line in requested)
            {
                var available = returnable.FirstOrDefault(r => r.SaleItemId == line.SaleItemId)
                    ?? throw new ValidationException(
                        "One of the lines being returned is not on this sale.");

                if (line.Quantity > available.QuantityReturnable)
                {
                    throw new ValidationException(
                        $"{available.ProductName}: only {available.QuantityReturnable} of the " +
                        $"{available.QuantitySold} sold can still be returned.");
                }

                prepared.Add(new SaleReturnItem
                {
                    SaleItemId = line.SaleItemId,
                    ProductId = available.ProductId,
                    Quantity = line.Quantity,
                    UnitPrice = available.UnitPrice,

                    // The cost the goods went out at, not today's average. Reversing cost of
                    // sales at the current average would change the gross profit reported on a
                    // sale that happened months ago.
                    UnitCost = available.UnitCost
                });

                subtotal += line.Quantity * available.UnitPrice;
            }

            var refund = refundAmount ?? subtotal;

            if (refund < 0m) throw new ValidationException("A refund cannot be negative.");

            if (refund > subtotal)
            {
                throw new ValidationException(
                    $"A refund of {refund:N2} is more than the {subtotal:N2} of goods being returned.");
            }

            var normalisedReason = ReturnReasons.All.FirstOrDefault(r =>
                string.Equals(r, Clean(reason), StringComparison.OrdinalIgnoreCase))
                ?? ReturnReasons.Other;

            // Goods sent back because they were damaged do not go back on the shelf, whatever
            // the operator ticked - restocking them would put stock in the system that cannot
            // be sold.
            var restock = restockToInventory &&
                          !string.Equals(normalisedReason, ReturnReasons.Damaged, StringComparison.Ordinal) &&
                          !string.Equals(normalisedReason, ReturnReasons.Expired, StringComparison.Ordinal);

            var actor = _actor.Current;

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var saleReturn = new SaleReturn
                {
                    ReturnNo = await NextReturnNumberAsync(),
                    SaleId = saleId,
                    MemberId = sale.MemberId,
                    ReturnDate = DateTime.UtcNow,
                    Reason = normalisedReason,
                    Subtotal = subtotal,
                    RefundAmount = refund,
                    RefundMethod = Clean(refundMethod).Length == 0 ? "Cash" : Clean(refundMethod),
                    RestockedToInventory = restock,
                    Status = "Completed",
                    Notes = Clean(notes),
                    ProcessedByUserId = actor.AppUserId,
                    ProcessedBy = actor.DisplayName,
                    CreatedAt = DateTime.UtcNow,
                    Items = prepared
                };

                _context.SaleReturns.Add(saleReturn);
                await _context.SaveChangesAsync();

                if (restock)
                {
                    foreach (var item in prepared)
                    {
                        await RestockAsync(item, saleReturn.ReturnNo);
                    }
                }

                // The money going back is recorded as a refunded payment so the member's
                // payment history shows it, rather than the original payment being edited.
                if (refund > 0m)
                {
                    _context.Payments.Add(new Payment
                    {
                        MemberId = sale.MemberId,
                        SaleId = saleId,
                        Category = PaymentCategories.Sales,
                        Amount = -refund,
                        PaymentDate = saleReturn.ReturnDate,
                        Method = saleReturn.RefundMethod,
                        Status = "Refunded",
                        ReferenceNo = saleReturn.ReturnNo,
                        Notes = $"Refund for {saleReturn.ReturnNo}",
                        ProcessedByUserId = actor.AppUserId,
                        ProcessedBy = actor.DisplayName,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                await _audit.RecordAsync(
                    AuditActions.Create, ErpModules.Sales, nameof(SaleReturn),
                    saleReturn.SaleReturnId.ToString(),
                    $"{saleReturn.ReturnNo}: {prepared.Sum(i => i.Quantity)} unit(s) returned " +
                    $"against sale #{saleId}, {refund:N2} refunded.");

                await _finance.PostSaleReturnAsync(saleReturn.SaleReturnId);

                return (await GetReturnAsync(saleReturn.SaleReturnId))!;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<SaleReturnView?> CancelReturnAsync(int saleReturnId, string reason)
        {
            var saleReturn = await _context.SaleReturns
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.SaleReturnId == saleReturnId);

            if (saleReturn is null) return null;

            if (!string.Equals(saleReturn.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException($"{saleReturn.ReturnNo} has already been cancelled.");
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Stock that was put back on the shelf comes off it again, or the cancelled
                // return would leave the gym believing it has goods it does not.
                if (saleReturn.RestockedToInventory)
                {
                    foreach (var item in saleReturn.Items)
                    {
                        await UnstockAsync(item, saleReturn.ReturnNo);
                    }
                }

                saleReturn.Status = "Cancelled";

                var trimmed = Clean(reason);
                if (trimmed.Length > 0)
                {
                    saleReturn.Notes = string.IsNullOrWhiteSpace(saleReturn.Notes)
                        ? $"Cancelled: {trimmed}"
                        : $"{saleReturn.Notes} | Cancelled: {trimmed}";

                    if (saleReturn.Notes.Length > 300) saleReturn.Notes = saleReturn.Notes[..300];
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                await _finance.ReverseSaleReturnAsync(
                    saleReturnId, $"{saleReturn.ReturnNo} cancelled");

                return await GetReturnAsync(saleReturnId);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // ------------------------------------------------------------------ stock

        private async Task RestockAsync(SaleReturnItem item, string returnNo)
        {
            var inventory = await _context.Inventories
                .FirstOrDefaultAsync(i => i.ProductId == item.ProductId);

            if (inventory is null)
            {
                inventory = new Inventory
                {
                    ProductId = item.ProductId,
                    QuantityOnHand = 0m,
                    ReorderLevel = 0m,
                    AverageCost = item.UnitCost,
                    LastUpdatedAt = DateTime.UtcNow
                };

                _context.Inventories.Add(inventory);
            }

            var before = inventory.QuantityOnHand;
            inventory.QuantityOnHand += item.Quantity;
            inventory.LastUpdatedAt = DateTime.UtcNow;

            var actor = _actor.Current;

            _context.StockMovements.Add(new StockMovement
            {
                ProductId = item.ProductId,
                MovementType = "In",
                Quantity = item.Quantity,
                BalanceBefore = before,
                BalanceAfter = inventory.QuantityOnHand,

                // At the cost it left at, so returning goods does not disturb the weighted
                // average - they never stopped being worth what they were worth.
                UnitCost = item.UnitCost,
                TotalCost = Math.Round(item.Quantity * item.UnitCost, 2, MidpointRounding.AwayFromZero),

                Reference = returnNo,
                Notes = "Stock returned by the customer",
                RecordedByEmployeeId = actor.EmployeeId,
                PerformedByUserId = actor.AppUserId,
                PerformedBy = actor.DisplayName,
                MovementDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        }

        private async Task UnstockAsync(SaleReturnItem item, string returnNo)
        {
            var inventory = await _context.Inventories
                .FirstOrDefaultAsync(i => i.ProductId == item.ProductId);

            if (inventory is null) return;

            var before = inventory.QuantityOnHand;
            inventory.QuantityOnHand = Math.Max(0m, inventory.QuantityOnHand - item.Quantity);
            inventory.LastUpdatedAt = DateTime.UtcNow;

            var actor = _actor.Current;

            _context.StockMovements.Add(new StockMovement
            {
                ProductId = item.ProductId,
                MovementType = "Out",
                Quantity = item.Quantity,
                BalanceBefore = before,
                BalanceAfter = inventory.QuantityOnHand,
                UnitCost = item.UnitCost,
                TotalCost = Math.Round(item.Quantity * item.UnitCost, 2, MidpointRounding.AwayFromZero),
                Reference = $"{returnNo}-CANCEL",
                Notes = "Return cancelled, stock removed again",
                RecordedByEmployeeId = actor.EmployeeId,
                PerformedByUserId = actor.AppUserId,
                PerformedBy = actor.DisplayName,
                MovementDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        }

        // ------------------------------------------------------------------ helpers

        private async Task<string> NextReturnNumberAsync()
        {
            var last = await _context.SaleReturns
                .AsNoTracking()
                .OrderByDescending(r => r.SaleReturnId)
                .Select(r => r.ReturnNo)
                .FirstOrDefaultAsync();

            var next = 1;

            if (!string.IsNullOrWhiteSpace(last) &&
                int.TryParse(last.Replace("RET-", "", StringComparison.OrdinalIgnoreCase), out var parsed))
            {
                next = parsed + 1;
            }
            else
            {
                next = await _context.SaleReturns.CountAsync() + 1;
            }

            return $"RET-{next:D6}";
        }

        private static SaleReturnView ToView(SaleReturn r, bool includeItems) => new()
        {
            SaleReturnId = r.SaleReturnId,
            ReturnNo = r.ReturnNo,
            SaleId = r.SaleId,
            MemberId = r.MemberId,
            MemberName = r.Member is null
                ? "Walk-In"
                : $"{r.Member.FirstName} {r.Member.LastName}".Trim(),
            ReturnDate = r.ReturnDate,
            Reason = r.Reason,
            Subtotal = r.Subtotal,
            RefundAmount = r.RefundAmount,
            RefundMethod = r.RefundMethod,
            RestockedToInventory = r.RestockedToInventory,
            Status = r.Status,
            Notes = r.Notes,
            ProcessedBy = string.IsNullOrWhiteSpace(r.ProcessedBy) ? "—" : r.ProcessedBy,
            ItemCount = r.Items.Count,
            CreatedAt = r.CreatedAt,

            Items = includeItems
                ? r.Items.Select(i => new SaleReturnItemView
                {
                    SaleReturnItemId = i.SaleReturnItemId,
                    SaleItemId = i.SaleItemId,
                    ProductId = i.ProductId,
                    ProductCode = i.Product?.ProductCode ?? "",
                    ProductName = i.Product?.ProductName ?? $"Product #{i.ProductId}",
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    UnitCost = i.UnitCost
                }).ToList()
                : new List<SaleReturnItemView>()
        };

        private static string Clean(string? value) => (value ?? "").Trim();
    }
}
