using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    // ---------------------------------------------------------------------- views

    public class PurchaseItemView
    {
        public int PurchaseItemId { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal QuantityReceived { get; set; }
        public decimal OutstandingQuantity => Quantity - QuantityReceived;
        public decimal LineTotal => Quantity * UnitCost;
    }

    public class PurchaseView
    {
        public int PurchaseId { get; set; }
        public string PurchaseNo { get; set; } = "";
        public int SupplierId { get; set; }
        public string SupplierName { get; set; } = "";
        public DateTime OrderDate { get; set; }
        public DateTime? ExpectedDate { get; set; }
        public DateTime? ReceivedDate { get; set; }
        public string Status { get; set; } = "";
        public string SupplierReference { get; set; } = "";
        public decimal Subtotal { get; set; }
        public decimal Discount { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal Balance => Total - AmountPaid;
        public string PaymentStatus { get; set; } = "";
        public string Notes { get; set; } = "";
        public string ProcessedBy { get; set; } = "";
        public int ItemCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<PurchaseItemView> Items { get; set; } = new();
    }

    public class SupplierPaymentView
    {
        public int SupplierPaymentId { get; set; }
        public int SupplierId { get; set; }
        public string SupplierName { get; set; } = "";
        public int? PurchaseId { get; set; }
        public string PurchaseNo { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string Method { get; set; } = "";
        public string ReferenceNo { get; set; } = "";
        public string Notes { get; set; } = "";
        public string ProcessedBy { get; set; } = "";
    }

    /// <summary>One line the caller wants on a purchase.</summary>
    public class PurchaseLineRequest
    {
        public int ProductId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
    }

    public class PurchaseSummary
    {
        public int Count { get; set; }
        public int Draft { get; set; }
        public int Ordered { get; set; }
        public int AwaitingDelivery { get; set; }
        public decimal TotalOrdered { get; set; }
        public decimal TotalReceived { get; set; }
        public decimal TotalOutstanding { get; set; }
        public decimal PayableBalance { get; set; }
    }

    // ---------------------------------------------------------------------- service

    /// <summary>
    /// Buying stock: the order, the delivery, and the money owed for it.
    ///
    /// This is the half of Inventory that makes it financially complete. Stock-in on its own
    /// says quantity arrived; a purchase says what was agreed, what it cost, and who is owed -
    /// which is what lets stock be carried as an asset at what was actually paid for it, and
    /// what turns a delivery into an account payable rather than an unexplained rise in stock.
    /// </summary>
    public interface IPurchaseService
    {
        Task<List<PurchaseView>> GetPurchasesAsync(
            DateTime? fromUtc = null, DateTime? toUtc = null,
            string? status = null, int? supplierId = null);

        Task<PurchaseView?> GetPurchaseAsync(int purchaseId);

        Task<PurchaseSummary> GetSummaryAsync();

        Task<PurchaseView> CreatePurchaseAsync(
            int supplierId, DateTime orderDate, DateTime? expectedDate, string supplierReference,
            decimal discount, decimal tax, string notes, IEnumerable<PurchaseLineRequest> lines);

        Task<PurchaseView?> UpdateDraftAsync(
            int purchaseId, int supplierId, DateTime orderDate, DateTime? expectedDate,
            string supplierReference, decimal discount, decimal tax, string notes,
            IEnumerable<PurchaseLineRequest> lines);

        /// <summary>Places the order with the supplier. Nothing has arrived yet.</summary>
        Task<PurchaseView?> MarkOrderedAsync(int purchaseId);

        /// <summary>
        /// Receives the goods.
        ///
        /// This is the event that matters: it writes the stock movements, folds the purchase
        /// cost into each product's weighted average, and posts
        /// <c>Dr Inventory / Cr Accounts Payable</c>. Passing no quantities receives everything
        /// still outstanding, which is what a complete delivery means.
        /// </summary>
        Task<PurchaseView?> ReceiveAsync(
            int purchaseId, IDictionary<int, decimal>? receivedByItemId = null);

        Task<PurchaseView?> CancelAsync(int purchaseId, string reason);

        Task<bool> DeleteDraftAsync(int purchaseId);

        // ---------------------------------------------------------------- payables

        Task<List<SupplierPaymentView>> GetSupplierPaymentsAsync(
            int? supplierId = null, int? purchaseId = null,
            DateTime? fromUtc = null, DateTime? toUtc = null);

        Task<SupplierPaymentView> PaySupplierAsync(
            int supplierId, int? purchaseId, decimal amount, DateTime paymentDate,
            string method, string referenceNo, string notes, int? bankAccountId);
    }

    public class PurchaseService : IPurchaseService
    {
        private static readonly string[] PaymentMethods =
            { "Cash", "Card", "Transfer", "Check", "GCash" };

        private readonly TenantErpDbContext _context;
        private readonly IInventoryService _inventory;
        private readonly IFinancePostingService _finance;
        private readonly ICurrentUserAccessor _actor;
        private readonly IAuditService _audit;

        public PurchaseService(
            TenantErpDbContext context,
            IInventoryService inventory,
            IFinancePostingService finance,
            ICurrentUserAccessor actor,
            IAuditService audit)
        {
            _context = context;
            _inventory = inventory;
            _finance = finance;
            _actor = actor;
            _audit = audit;
        }

        // ------------------------------------------------------------------ reading

        public async Task<List<PurchaseView>> GetPurchasesAsync(
            DateTime? fromUtc = null, DateTime? toUtc = null,
            string? status = null, int? supplierId = null)
        {
            var query = _context.Purchases
                .AsNoTracking()
                .Include(p => p.Supplier)
                .Include(p => p.Items)
                .AsQueryable();

            if (fromUtc.HasValue) query = query.Where(p => p.OrderDate >= fromUtc.Value);
            if (toUtc.HasValue) query = query.Where(p => p.OrderDate < toUtc.Value);
            if (supplierId.HasValue) query = query.Where(p => p.SupplierId == supplierId.Value);

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(p => p.Status == status);
            }

            var rows = await query
                .OrderByDescending(p => p.OrderDate)
                .ThenByDescending(p => p.PurchaseId)
                .ToListAsync();

            return rows.Select(p => ToView(p, includeItems: false)).ToList();
        }

        public async Task<PurchaseView?> GetPurchaseAsync(int purchaseId)
        {
            var purchase = await _context.Purchases
                .AsNoTracking()
                .Include(p => p.Supplier)
                .Include(p => p.Items).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(p => p.PurchaseId == purchaseId);

            return purchase is null ? null : ToView(purchase, includeItems: true);
        }

        public async Task<PurchaseSummary> GetSummaryAsync()
        {
            var rows = await _context.Purchases
                .AsNoTracking()
                .Select(p => new { p.Status, p.Total, p.AmountPaid })
                .ToListAsync();

            return new PurchaseSummary
            {
                Count = rows.Count,
                Draft = rows.Count(r => r.Status == PurchaseStatuses.Draft),
                Ordered = rows.Count(r => r.Status == PurchaseStatuses.Ordered),
                AwaitingDelivery = rows.Count(r =>
                    r.Status == PurchaseStatuses.Ordered ||
                    r.Status == PurchaseStatuses.PartiallyReceived),
                TotalOrdered = rows.Sum(r => r.Total),
                TotalReceived = rows
                    .Where(r => r.Status == PurchaseStatuses.Received)
                    .Sum(r => r.Total),
                TotalOutstanding = rows
                    .Where(r => r.Status == PurchaseStatuses.Ordered ||
                                r.Status == PurchaseStatuses.PartiallyReceived)
                    .Sum(r => r.Total),
                PayableBalance = rows
                    .Where(r => PurchaseStatuses.HasReceipts(r.Status))
                    .Sum(r => r.Total - r.AmountPaid)
            };
        }

        // ------------------------------------------------------------------ writing

        public async Task<PurchaseView> CreatePurchaseAsync(
            int supplierId, DateTime orderDate, DateTime? expectedDate, string supplierReference,
            decimal discount, decimal tax, string notes, IEnumerable<PurchaseLineRequest> lines)
        {
            var supplier = await RequireSupplierAsync(supplierId);

            var prepared = await ValidateLinesAsync(lines);

            var subtotal = prepared.Sum(l => l.Quantity * l.UnitCost);

            ValidateTotals(subtotal, discount, tax);

            var actor = _actor.Current;

            var purchase = new Purchase
            {
                PurchaseNo = await NextPurchaseNumberAsync(),
                SupplierId = supplierId,
                OrderDate = orderDate == default ? DateTime.UtcNow : orderDate,
                ExpectedDate = expectedDate,
                SupplierReference = Clean(supplierReference),
                Status = PurchaseStatuses.Draft,
                Subtotal = subtotal,
                Discount = discount,
                Tax = tax,
                Total = subtotal - discount + tax,
                AmountPaid = 0m,
                PaymentStatus = SettlementStatuses.Unpaid,
                Notes = Clean(notes),
                ProcessedByUserId = actor.AppUserId,
                ProcessedBy = actor.DisplayName,
                CreatedAt = DateTime.UtcNow,
                Items = prepared
            };

            _context.Purchases.Add(purchase);
            await _context.SaveChangesAsync();

            await _audit.RecordAsync(
                AuditActions.Create, ErpModules.Inventory, nameof(Purchase),
                purchase.PurchaseId.ToString(),
                $"{purchase.PurchaseNo} raised for {supplier.SupplierName}, " +
                $"{prepared.Count} line(s), total {purchase.Total:N2}.");

            return (await GetPurchaseAsync(purchase.PurchaseId))!;
        }

        public async Task<PurchaseView?> UpdateDraftAsync(
            int purchaseId, int supplierId, DateTime orderDate, DateTime? expectedDate,
            string supplierReference, decimal discount, decimal tax, string notes,
            IEnumerable<PurchaseLineRequest> lines)
        {
            var purchase = await _context.Purchases
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.PurchaseId == purchaseId);

            if (purchase is null) return null;

            if (purchase.Status != PurchaseStatuses.Draft)
            {
                throw new ValidationException(
                    $"{purchase.PurchaseNo} is {purchase.Status.ToLowerInvariant()}. " +
                    "Only a draft can be edited - once it is ordered, the supplier has the order.");
            }

            await RequireSupplierAsync(supplierId);

            var prepared = await ValidateLinesAsync(lines);
            var subtotal = prepared.Sum(l => l.Quantity * l.UnitCost);

            ValidateTotals(subtotal, discount, tax);

            _context.PurchaseItems.RemoveRange(purchase.Items);

            purchase.SupplierId = supplierId;
            purchase.OrderDate = orderDate == default ? purchase.OrderDate : orderDate;
            purchase.ExpectedDate = expectedDate;
            purchase.SupplierReference = Clean(supplierReference);
            purchase.Subtotal = subtotal;
            purchase.Discount = discount;
            purchase.Tax = tax;
            purchase.Total = subtotal - discount + tax;
            purchase.Notes = Clean(notes);
            purchase.Items = prepared;

            await _context.SaveChangesAsync();

            return await GetPurchaseAsync(purchaseId);
        }

        public async Task<PurchaseView?> MarkOrderedAsync(int purchaseId)
        {
            var purchase = await _context.Purchases
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.PurchaseId == purchaseId);

            if (purchase is null) return null;

            if (purchase.Status != PurchaseStatuses.Draft)
            {
                throw new ValidationException(
                    $"{purchase.PurchaseNo} is already {purchase.Status.ToLowerInvariant()}.");
            }

            if (purchase.Items.Count == 0)
            {
                throw new ValidationException("An order with no lines on it has nothing to order.");
            }

            purchase.Status = PurchaseStatuses.Ordered;
            await _context.SaveChangesAsync();

            return await GetPurchaseAsync(purchaseId);
        }

        public async Task<PurchaseView?> ReceiveAsync(
            int purchaseId, IDictionary<int, decimal>? receivedByItemId = null)
        {
            var purchase = await _context.Purchases
                .Include(p => p.Supplier)
                .Include(p => p.Items).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(p => p.PurchaseId == purchaseId);

            if (purchase is null) return null;

            if (purchase.Status == PurchaseStatuses.Cancelled)
            {
                throw new ValidationException($"{purchase.PurchaseNo} has been cancelled.");
            }

            if (purchase.Status == PurchaseStatuses.Received)
            {
                throw new ValidationException($"{purchase.PurchaseNo} has already been received in full.");
            }

            // Work out what is arriving before anything is written, so a bad quantity on the
            // fifth line does not leave the first four already in stock.
            var arrivals = new List<(PurchaseItem Item, decimal Quantity)>();

            foreach (var item in purchase.Items)
            {
                var outstanding = item.Quantity - item.QuantityReceived;
                if (outstanding <= 0m) continue;

                var quantity = outstanding;

                if (receivedByItemId is not null &&
                    receivedByItemId.TryGetValue(item.PurchaseItemId, out var requested))
                {
                    if (requested < 0m)
                    {
                        throw new ValidationException("A received quantity cannot be negative.");
                    }

                    if (requested > outstanding)
                    {
                        throw new ValidationException(
                            $"{item.Product?.ProductName ?? $"Product #{item.ProductId}"}: " +
                            $"{requested:N2} is more than the {outstanding:N2} still outstanding. " +
                            "Raise a second order if the supplier sent extra.");
                    }

                    quantity = requested;
                }

                if (quantity > 0m) arrivals.Add((item, quantity));
            }

            if (arrivals.Count == 0)
            {
                throw new ValidationException("Nothing on this order is still outstanding.");
            }

            foreach (var (item, quantity) in arrivals)
            {
                // Goes through the inventory service rather than writing stock here, so the
                // weighted average, the movement ledger and the audit trail are all maintained
                // by the one place that knows how.
                await _inventory.StockInAsync(
                    item.ProductId, quantity, purchase.SupplierId,
                    purchase.PurchaseNo, $"Received on {purchase.PurchaseNo}",
                    recordedByEmployeeId: null,
                    unitCost: item.UnitCost,
                    purchaseId: purchase.PurchaseId);

                item.QuantityReceived += quantity;
            }

            var complete = purchase.Items.All(i => i.QuantityReceived >= i.Quantity);

            purchase.Status = complete ? PurchaseStatuses.Received : PurchaseStatuses.PartiallyReceived;
            purchase.ReceivedDate = complete ? DateTime.UtcNow : purchase.ReceivedDate;

            await _context.SaveChangesAsync();

            await _audit.RecordAsync(
                AuditActions.StockReceived, ErpModules.Inventory, nameof(Purchase),
                purchase.PurchaseId.ToString(),
                $"{purchase.PurchaseNo} {(complete ? "received in full" : "partially received")} " +
                $"from {purchase.Supplier?.SupplierName}.");

            // Now the goods are here, the gym owes for them. Posted after the stock itself has
            // been committed, for the same reason every other posting is.
            await _finance.PostPurchaseReceiptAsync(purchase.PurchaseId);

            return await GetPurchaseAsync(purchaseId);
        }

        public async Task<PurchaseView?> CancelAsync(int purchaseId, string reason)
        {
            var purchase = await _context.Purchases
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.PurchaseId == purchaseId);

            if (purchase is null) return null;

            if (PurchaseStatuses.HasReceipts(purchase.Status))
            {
                throw new ValidationException(
                    $"{purchase.PurchaseNo} has stock against it already. Cancelling it would " +
                    "leave that stock unexplained - return the goods to the supplier instead.");
            }

            if (purchase.Status == PurchaseStatuses.Cancelled)
            {
                throw new ValidationException($"{purchase.PurchaseNo} is already cancelled.");
            }

            purchase.Status = PurchaseStatuses.Cancelled;

            var trimmed = Clean(reason);
            if (trimmed.Length > 0)
            {
                purchase.Notes = string.IsNullOrWhiteSpace(purchase.Notes)
                    ? $"Cancelled: {trimmed}"
                    : $"{purchase.Notes} | Cancelled: {trimmed}";

                if (purchase.Notes.Length > 300) purchase.Notes = purchase.Notes[..300];
            }

            await _context.SaveChangesAsync();

            return await GetPurchaseAsync(purchaseId);
        }

        public async Task<bool> DeleteDraftAsync(int purchaseId)
        {
            var purchase = await _context.Purchases
                .FirstOrDefaultAsync(p => p.PurchaseId == purchaseId);

            if (purchase is null) return false;

            if (purchase.Status != PurchaseStatuses.Draft)
            {
                throw new ValidationException(
                    $"{purchase.PurchaseNo} is {purchase.Status.ToLowerInvariant()} and is part of " +
                    "the purchase history. Cancel it instead so the record stays.");
            }

            _context.Purchases.Remove(purchase);
            await _context.SaveChangesAsync();

            return true;
        }

        // ------------------------------------------------------------------ payables

        public async Task<List<SupplierPaymentView>> GetSupplierPaymentsAsync(
            int? supplierId = null, int? purchaseId = null,
            DateTime? fromUtc = null, DateTime? toUtc = null)
        {
            var query = _context.SupplierPayments
                .AsNoTracking()
                .Include(p => p.Supplier)
                .Include(p => p.Purchase)
                .AsQueryable();

            if (supplierId.HasValue) query = query.Where(p => p.SupplierId == supplierId.Value);
            if (purchaseId.HasValue) query = query.Where(p => p.PurchaseId == purchaseId.Value);
            if (fromUtc.HasValue) query = query.Where(p => p.PaymentDate >= fromUtc.Value);
            if (toUtc.HasValue) query = query.Where(p => p.PaymentDate < toUtc.Value);

            var rows = await query
                .OrderByDescending(p => p.PaymentDate)
                .ThenByDescending(p => p.SupplierPaymentId)
                .ToListAsync();

            return rows.Select(p => new SupplierPaymentView
            {
                SupplierPaymentId = p.SupplierPaymentId,
                SupplierId = p.SupplierId,
                SupplierName = p.Supplier?.SupplierName ?? "",
                PurchaseId = p.PurchaseId,
                PurchaseNo = p.Purchase?.PurchaseNo ?? "",
                Amount = p.Amount,
                PaymentDate = p.PaymentDate,
                Method = p.Method,
                ReferenceNo = p.ReferenceNo,
                Notes = p.Notes,
                ProcessedBy = string.IsNullOrWhiteSpace(p.ProcessedBy) ? "—" : p.ProcessedBy
            }).ToList();
        }

        public async Task<SupplierPaymentView> PaySupplierAsync(
            int supplierId, int? purchaseId, decimal amount, DateTime paymentDate,
            string method, string referenceNo, string notes, int? bankAccountId)
        {
            var supplier = await RequireSupplierAsync(supplierId);

            if (amount <= 0m)
            {
                throw new ValidationException("The payment amount must be greater than zero.");
            }

            Purchase? purchase = null;

            if (purchaseId.HasValue)
            {
                purchase = await _context.Purchases
                    .FirstOrDefaultAsync(p => p.PurchaseId == purchaseId.Value)
                    ?? throw new ValidationException($"No purchase with id {purchaseId.Value} exists.");

                if (purchase.SupplierId != supplierId)
                {
                    throw new ValidationException(
                        $"{purchase.PurchaseNo} belongs to a different supplier.");
                }

                if (!PurchaseStatuses.HasReceipts(purchase.Status))
                {
                    throw new ValidationException(
                        $"Nothing has been received on {purchase.PurchaseNo} yet, so nothing is owed for it.");
                }

                var balance = purchase.Total - purchase.AmountPaid;

                // Paying more than is owed is almost always a typo, and letting it through
                // makes a payables report show a negative debt nobody can explain.
                if (amount > balance)
                {
                    throw new ValidationException(
                        $"That is more than {purchase.PurchaseNo} still owes. " +
                        $"The outstanding balance is {balance:N2}.");
                }
            }

            if (bankAccountId.HasValue &&
                !await _context.BankAccounts.AnyAsync(a => a.BankAccountId == bankAccountId.Value))
            {
                throw new ValidationException("The selected cash or bank account does not exist.");
            }

            var actor = _actor.Current;

            var payment = new SupplierPayment
            {
                SupplierId = supplierId,
                PurchaseId = purchaseId,
                Amount = amount,
                PaymentDate = paymentDate == default ? DateTime.UtcNow : paymentDate,
                Method = NormaliseMethod(method),
                ReferenceNo = Clean(referenceNo),
                Notes = Clean(notes),
                BankAccountId = bankAccountId,
                ProcessedByUserId = actor.AppUserId,
                ProcessedBy = actor.DisplayName,
                CreatedAt = DateTime.UtcNow
            };

            _context.SupplierPayments.Add(payment);

            if (purchase is not null)
            {
                purchase.AmountPaid += amount;
                purchase.PaymentStatus = SettlementStatuses.For(purchase.Total, purchase.AmountPaid);
            }

            await _context.SaveChangesAsync();

            await _audit.RecordAsync(
                AuditActions.PaymentRecorded, ErpModules.Inventory, nameof(SupplierPayment),
                payment.SupplierPaymentId.ToString(),
                $"Paid {amount:N2} to {supplier.SupplierName}" +
                (purchase is null ? " on account." : $" against {purchase.PurchaseNo}."));

            await _finance.PostSupplierPaymentAsync(payment.SupplierPaymentId);

            var saved = await GetSupplierPaymentsAsync(purchaseId: purchaseId, supplierId: supplierId);

            return saved.First(p => p.SupplierPaymentId == payment.SupplierPaymentId);
        }

        // ------------------------------------------------------------------ helpers

        private async Task<Supplier> RequireSupplierAsync(int supplierId)
        {
            var supplier = await _context.Suppliers
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SupplierId == supplierId)
                ?? throw new ValidationException($"No supplier with id {supplierId} exists.");

            if (!supplier.IsActive)
            {
                throw new ValidationException($"{supplier.SupplierName} is inactive.");
            }

            return supplier;
        }

        private async Task<List<PurchaseItem>> ValidateLinesAsync(IEnumerable<PurchaseLineRequest> lines)
        {
            var requested = (lines ?? Enumerable.Empty<PurchaseLineRequest>()).ToList();

            if (requested.Count == 0)
            {
                throw new ValidationException("A purchase needs at least one line.");
            }

            // Two lines for the same product would each carry their own cost and their own
            // received quantity, which makes "how much of this product is still outstanding?"
            // ambiguous. They are merged at the average of what was asked for.
            var merged = requested
                .GroupBy(l => l.ProductId)
                .Select(g => new PurchaseLineRequest
                {
                    ProductId = g.Key,
                    Quantity = g.Sum(l => l.Quantity),
                    UnitCost = g.Sum(l => l.Quantity) > 0m
                        ? g.Sum(l => l.Quantity * l.UnitCost) / g.Sum(l => l.Quantity)
                        : g.First().UnitCost
                })
                .ToList();

            var productIds = merged.Select(l => l.ProductId).ToList();

            var products = await _context.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId))
                .ToDictionaryAsync(p => p.ProductId);

            var prepared = new List<PurchaseItem>();

            foreach (var line in merged)
            {
                if (!products.TryGetValue(line.ProductId, out var product))
                {
                    throw new ValidationException($"No product with id {line.ProductId} exists.");
                }

                if (!product.IsActive)
                {
                    throw new ValidationException(
                        $"{product.ProductName} is inactive and cannot be ordered.");
                }

                if (line.Quantity <= 0m)
                {
                    throw new ValidationException(
                        $"{product.ProductName}: the quantity must be greater than zero.");
                }

                if (line.UnitCost < 0m)
                {
                    throw new ValidationException(
                        $"{product.ProductName}: the unit cost cannot be negative.");
                }

                prepared.Add(new PurchaseItem
                {
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,

                    // Falls back to the catalogue cost so a quick order does not silently
                    // value the delivery at nothing and wreck the weighted average.
                    UnitCost = line.UnitCost > 0m ? line.UnitCost : product.CostPrice,
                    QuantityReceived = 0m
                });
            }

            return prepared;
        }

        private static void ValidateTotals(decimal subtotal, decimal discount, decimal tax)
        {
            if (discount < 0m) throw new ValidationException("A discount cannot be negative.");
            if (tax < 0m) throw new ValidationException("Tax cannot be negative.");

            if (discount > subtotal)
            {
                throw new ValidationException(
                    $"A discount of {discount:N2} is more than the order subtotal of {subtotal:N2}.");
            }
        }

        private static string NormaliseMethod(string? method) =>
            PaymentMethods.FirstOrDefault(m =>
                string.Equals(m, (method ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            ?? "Cash";

        /// <summary>
        /// The next running number, derived from the highest existing one rather than a
        /// counter row, so restoring a database cannot produce a collision.
        /// </summary>
        private async Task<string> NextPurchaseNumberAsync()
        {
            var last = await _context.Purchases
                .AsNoTracking()
                .OrderByDescending(p => p.PurchaseId)
                .Select(p => p.PurchaseNo)
                .FirstOrDefaultAsync();

            var next = 1;

            if (!string.IsNullOrWhiteSpace(last) &&
                int.TryParse(last.Replace("PO-", "", StringComparison.OrdinalIgnoreCase), out var parsed))
            {
                next = parsed + 1;
            }
            else
            {
                next = await _context.Purchases.CountAsync() + 1;
            }

            return $"PO-{next:D6}";
        }

        private static PurchaseView ToView(Purchase p, bool includeItems) => new()
        {
            PurchaseId = p.PurchaseId,
            PurchaseNo = p.PurchaseNo,
            SupplierId = p.SupplierId,
            SupplierName = p.Supplier?.SupplierName ?? "",
            OrderDate = p.OrderDate,
            ExpectedDate = p.ExpectedDate,
            ReceivedDate = p.ReceivedDate,
            Status = p.Status,
            SupplierReference = p.SupplierReference,
            Subtotal = p.Subtotal,
            Discount = p.Discount,
            Tax = p.Tax,
            Total = p.Total,
            AmountPaid = p.AmountPaid,
            PaymentStatus = p.PaymentStatus,
            Notes = p.Notes,
            ProcessedBy = string.IsNullOrWhiteSpace(p.ProcessedBy) ? "—" : p.ProcessedBy,
            ItemCount = p.Items.Count,
            CreatedAt = p.CreatedAt,

            Items = includeItems
                ? p.Items.Select(i => new PurchaseItemView
                {
                    PurchaseItemId = i.PurchaseItemId,
                    ProductId = i.ProductId,
                    ProductCode = i.Product?.ProductCode ?? "",
                    ProductName = i.Product?.ProductName ?? $"Product #{i.ProductId}",
                    Quantity = i.Quantity,
                    UnitCost = i.UnitCost,
                    QuantityReceived = i.QuantityReceived
                }).ToList()
                : new List<PurchaseItemView>()
        };

        private static string Clean(string? value) => (value ?? "").Trim();
    }
}
