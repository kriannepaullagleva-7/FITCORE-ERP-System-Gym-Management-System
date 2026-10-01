using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// The consistency sweep over one tenant database. See <see cref="IDataIntegrityService"/>
    /// for why it never repairs anything.
    /// </summary>
    public class DataIntegrityService : IDataIntegrityService
    {
        /// <summary>How many offending ids a finding carries. A diagnostic, not an export.</summary>
        private const int MaxExamples = 10;

        private readonly TenantErpDbContext _context;

        public DataIntegrityService(TenantErpDbContext context) => _context = context;

        public async Task<IntegrityReport> RunAsync()
        {
            var report = new IntegrityReport();

            await CheckOrphansAsync(report);
            await CheckSaleArithmeticAsync(report);
            await CheckStockLedgerAsync(report);
            await CheckPayrollArithmeticAsync(report);
            await CheckLedgerAsync(report);

            return report;
        }

        // ------------------------------------------------------------------ orphans

        /// <summary>
        /// Rows pointing at parents that are not there.
        ///
        /// Every one of these is prevented by a foreign key, so the expected result is zero
        /// across the board. They are run anyway because a schema applied to three databases
        /// over time is exactly the place where "the constraint is definitely there" stops
        /// being something to take on trust - and because a restored backup or a hand-run
        /// repair script does not know about the model.
        /// </summary>
        private async Task CheckOrphansAsync(IntegrityReport report)
        {
            await OrphanAsync(report, "orphan.subscription.member", "Membership",
                "Every subscription naming a member names one that exists",
                _context.Subscriptions
                    .Where(s => s.MemberId != null
                             && !_context.Members.Any(m => m.MemberId == s.MemberId))
                    .Select(s => s.SubscriptionId));

            await OrphanAsync(report, "orphan.subscription.plan", "Membership",
                "Every subscription names a plan that exists",
                _context.Subscriptions
                    .Where(s => !_context.MembershipPlans.Any(p => p.PlanId == s.PlanId))
                    .Select(s => s.SubscriptionId));

            await OrphanAsync(report, "orphan.note.member", "Membership",
                "Every member note belongs to a member that exists",
                _context.MemberNotes
                    .Where(n => !_context.Members.Any(m => m.MemberId == n.MemberId))
                    .Select(n => n.MemberNoteId));

            await OrphanAsync(report, "orphan.payment.member", "Payments",
                "Every payment naming a member names one that exists",
                _context.Payments
                    .Where(p => p.MemberId != null
                             && !_context.Members.Any(m => m.MemberId == p.MemberId))
                    .Select(p => p.PaymentId));

            await OrphanAsync(report, "orphan.payment.subscription", "Payments",
                "Every payment against a subscription names one that exists",
                _context.Payments
                    .Where(p => p.SubscriptionId != null
                             && !_context.Subscriptions.Any(s => s.SubscriptionId == p.SubscriptionId))
                    .Select(p => p.PaymentId));

            await OrphanAsync(report, "orphan.payment.sale", "Payments",
                "Every payment against a sale names one that exists",
                _context.Payments
                    .Where(p => p.SaleId != null
                             && !_context.Sales.Any(s => s.SaleId == p.SaleId))
                    .Select(p => p.PaymentId));

            await OrphanAsync(report, "orphan.saleitem.sale", "Sales",
                "Every sale line belongs to a sale that exists",
                _context.SaleItems
                    .Where(i => !_context.Sales.Any(s => s.SaleId == i.SaleId))
                    .Select(i => i.SaleItemId));

            await OrphanAsync(report, "orphan.saleitem.product", "Sales",
                "Every sale line names a product that exists",
                _context.SaleItems
                    .Where(i => !_context.Products.Any(p => p.ProductId == i.ProductId))
                    .Select(i => i.SaleItemId));

            await OrphanAsync(report, "orphan.return.sale", "Sales",
                "Every return traces back to a sale that exists",
                _context.SaleReturns
                    .Where(r => !_context.Sales.Any(s => s.SaleId == r.SaleId))
                    .Select(r => r.SaleReturnId));

            await OrphanAsync(report, "orphan.movement.product", "Inventory",
                "Every stock movement names a product that exists",
                _context.StockMovements
                    .Where(m => !_context.Products.Any(p => p.ProductId == m.ProductId))
                    .Select(m => m.StockMovementId));

            await OrphanAsync(report, "orphan.inventory.product", "Inventory",
                "Every inventory row names a product that exists",
                _context.Inventories
                    .Where(i => !_context.Products.Any(p => p.ProductId == i.ProductId))
                    .Select(i => i.InventoryId));

            await OrphanAsync(report, "orphan.movement.supplier", "Inventory",
                "Every stock movement from a supplier names one that exists",
                _context.StockMovements
                    .Where(m => m.SupplierId != null
                             && !_context.Suppliers.Any(s => s.SupplierId == m.SupplierId))
                    .Select(m => m.StockMovementId));

            await OrphanAsync(report, "orphan.purchaseitem.purchase", "Inventory",
                "Every purchase line belongs to a purchase that exists",
                _context.PurchaseItems
                    .Where(i => !_context.Purchases.Any(p => p.PurchaseId == i.PurchaseId))
                    .Select(i => i.PurchaseItemId));

            await OrphanAsync(report, "orphan.purchase.supplier", "Inventory",
                "Every purchase names a supplier that exists",
                _context.Purchases
                    .Where(p => !_context.Suppliers.Any(s => s.SupplierId == p.SupplierId))
                    .Select(p => p.PurchaseId));

            await OrphanAsync(report, "orphan.attendance.employee", "Employees",
                "Every attendance row belongs to an employee that exists",
                _context.Attendances
                    .Where(a => !_context.Employees.Any(e => e.EmployeeId == a.EmployeeId))
                    .Select(a => a.AttendanceId));

            await OrphanAsync(report, "orphan.leave.employee", "Employees",
                "Every leave request belongs to an employee that exists",
                _context.LeaveRequests
                    .Where(l => !_context.Employees.Any(e => e.EmployeeId == l.EmployeeId))
                    .Select(l => l.LeaveRequestId));

            await OrphanAsync(report, "orphan.payroll.employee", "Payroll",
                "Every pay run belongs to an employee that exists",
                _context.Payrolls
                    .Where(p => !_context.Employees.Any(e => e.EmployeeId == p.EmployeeId))
                    .Select(p => p.PayrollId));

            await OrphanAsync(report, "orphan.journalline.entry", "Finance",
                "Every journal line belongs to an entry that exists",
                _context.JournalEntryLines
                    .Where(l => !_context.JournalEntries.Any(e => e.JournalEntryId == l.JournalEntryId))
                    .Select(l => l.JournalEntryLineId));

            await OrphanAsync(report, "orphan.journalline.account", "Finance",
                "Every journal line posts to an account that exists",
                _context.JournalEntryLines
                    .Where(l => !_context.Accounts.Any(a => a.AccountId == l.AccountId))
                    .Select(l => l.JournalEntryLineId));
        }

        // ------------------------------------------------------------------ sales

        private async Task CheckSaleArithmeticAsync(IntegrityReport report)
        {
            // A sale's stated total has to be the sum of what is on it. Nothing in the schema
            // makes that true: Subtotal, Discount and TotalAmount are plain columns, and a
            // service that wrote one without the others would leave a sale that reports a
            // figure its own lines do not support - and that figure is what revenue is built
            // from.
            var sales = await _context.Sales
                .AsNoTracking()
                .Where(s => s.Status != "Cancelled")
                .Select(s => new
                {
                    s.SaleId,
                    s.Subtotal,
                    s.Discount,
                    s.TotalAmount,
                    LineTotal = _context.SaleItems
                        .Where(i => i.SaleId == s.SaleId)
                        .Sum(i => (decimal?)(i.Quantity * i.UnitPrice)) ?? 0m
                })
                .ToListAsync();

            var mismatched = sales
                .Where(s => s.Subtotal != s.LineTotal)
                .Select(s => s.SaleId.ToString())
                .ToList();

            Add(report, "sale.subtotal", "Sales",
                "A sale's subtotal is the sum of its lines",
                mismatched, IntegritySeverities.Critical,
                "The subtotal recorded on the sale does not match its own lines, so revenue " +
                "and the ledger entry raised from it disagree with what was actually sold.");

            var badTotals = sales
                .Where(s => s.TotalAmount != s.Subtotal - s.Discount)
                .Select(s => s.SaleId.ToString())
                .ToList();

            Add(report, "sale.total", "Sales",
                "A sale's total is its subtotal less its discount",
                badTotals, IntegritySeverities.Critical,
                "The total charged is not the subtotal less the discount, so the amount taken " +
                "cannot be reconciled against what was billed.");

            // A line that was priced at nothing is usually a bug rather than a giveaway, but it
            // is not wrong by itself - so it is reported for attention, not as a failure of the
            // books.
            var freeLines = await _context.SaleItems
                .AsNoTracking()
                .Where(i => i.UnitPrice <= 0m)
                .Select(i => i.SaleItemId)
                .Take(MaxExamples + 1)
                .ToListAsync();

            Add(report, "sale.zeroprice", "Sales",
                "No sale line was priced at zero",
                freeLines.Select(i => i.ToString()).ToList(), IntegritySeverities.Info,
                "These lines carry no unit price. That is legitimate for a giveaway and a " +
                "mistake otherwise; nothing is inconsistent either way.");
        }

        // ------------------------------------------------------------------ inventory

        private async Task CheckStockLedgerAsync(IntegrityReport report)
        {
            // The invariant the whole inventory module rests on: the quantity on hand is
            // whatever the last movement said it was. If these drift, the stock figure and the
            // history that is supposed to explain it are two different answers, and the
            // inventory asset on the balance sheet belongs to neither.
            var products = await _context.Inventories
                .AsNoTracking()
                .Select(i => new
                {
                    i.ProductId,
                    i.QuantityOnHand,
                    LastBalance = _context.StockMovements
                        .Where(m => m.ProductId == i.ProductId)
                        .OrderByDescending(m => m.StockMovementId)
                        .Select(m => (decimal?)m.BalanceAfter)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var drifted = products
                .Where(p => p.LastBalance != null && p.LastBalance != p.QuantityOnHand)
                .Select(p => p.ProductId.ToString())
                .ToList();

            Add(report, "stock.ledger", "Inventory",
                "Stock on hand equals the closing balance of the movement ledger",
                drifted, IntegritySeverities.Critical,
                "The quantity on hand does not match the last stock movement's closing " +
                "balance, so the movement history no longer explains the stock figure.");

            var negative = await _context.Inventories
                .AsNoTracking()
                .Where(i => i.QuantityOnHand < 0m)
                .Select(i => i.ProductId)
                .Take(MaxExamples + 1)
                .ToListAsync();

            Add(report, "stock.negative", "Inventory",
                "No product holds negative stock",
                negative.Select(p => p.ToString()).ToList(), IntegritySeverities.Critical,
                "Stock cannot go below zero through any supported operation, so a negative " +
                "balance means something wrote to inventory without going through the service.");
        }

        // ------------------------------------------------------------------ payroll

        private async Task CheckPayrollArithmeticAsync(IntegrityReport report)
        {
            var runs = await _context.Payrolls
                .AsNoTracking()
                .Select(p => new
                {
                    p.PayrollId,
                    p.GrossPay,
                    p.Deductions,
                    p.NetPay,
                    p.SssDeduction,
                    p.PhilHealthDeduction,
                    p.PagIbigDeduction,
                    p.WithholdingTax,
                    p.OtherDeductions
                })
                .ToListAsync();

            var badNet = runs
                .Where(p => p.NetPay != p.GrossPay - p.Deductions)
                .Select(p => p.PayrollId.ToString())
                .ToList();

            Add(report, "payroll.net", "Payroll",
                "Net pay is gross pay less total deductions",
                badNet, IntegritySeverities.Critical,
                "The net on the payslip is not the gross less the deductions, so the payslip " +
                "disagrees with itself and the journal entry behind it cannot balance.");

            // Two different failures hide behind "the parts do not add up", and they deserve
            // different answers.
            //
            // If the net is also wrong, the payslip contradicts itself and the journal entry
            // behind it cannot balance - that is the Critical case, and it is caught above.
            //
            // If the net is right but the parts are not, the total is correct and merely
            // unattributed: the money was deducted, it just is not filed under any named
            // heading. That is what rows written before the statutory breakdown columns existed
            // look like, because the migration that added those columns defaulted them to zero
            // without backfilling. Nothing is out by a centavo; the payslip simply cannot
            // itemise itself. Calling that Critical would cry wolf about correct money.
            var unattributed = runs
                .Where(p => p.Deductions != p.SssDeduction + p.PhilHealthDeduction
                                         + p.PagIbigDeduction + p.WithholdingTax
                                         + p.OtherDeductions
                         && p.NetPay == p.GrossPay - p.Deductions)
                .Select(p => p.PayrollId.ToString())
                .ToList();

            Add(report, "payroll.deductions", "Payroll",
                "Total deductions are the sum of the named statutory figures",
                unattributed, IntegritySeverities.Warning,
                "The deduction total on these runs is correct and the net pay is right, but the " +
                "amount is not filed under SSS, PhilHealth, Pag-IBIG, withholding tax or other " +
                "deductions, so the payslip cannot itemise itself. This is the signature of runs " +
                "written before the breakdown columns existed. Every write path in the current " +
                "code keeps the parts in step, so no new run can arrive this way.");

            // Two paid runs covering the same day pay that day twice. PayrollService refuses
            // overlapping periods on write; this is the same rule read back over what is
            // already stored.
            var periods = await _context.Payrolls
                .AsNoTracking()
                .Where(p => p.Status != "Cancelled")
                .Select(p => new { p.PayrollId, p.EmployeeId, p.PeriodStart, p.PeriodEnd })
                .ToListAsync();

            var overlapping = periods
                .SelectMany(a => periods
                    .Where(b => b.EmployeeId == a.EmployeeId
                             && b.PayrollId > a.PayrollId
                             && a.PeriodStart <= b.PeriodEnd
                             && b.PeriodStart <= a.PeriodEnd)
                    .Select(b => $"{a.PayrollId}+{b.PayrollId}"))
                .Distinct()
                .ToList();

            Add(report, "payroll.overlap", "Payroll",
                "No employee has two pay runs covering the same day",
                overlapping, IntegritySeverities.Critical,
                "These pairs of runs share days for the same employee, which pays those days " +
                "twice.");
        }

        // ------------------------------------------------------------------ finance

        private async Task CheckLedgerAsync(IntegrityReport report)
        {
            // Nothing is checked if the tenant has no chart of accounts - a Micro or Small
            // tenant has no ledger by design, and reporting "0 of 0" as a finding would turn a
            // tier boundary into a fault.
            if (!await _context.Accounts.AnyAsync()) return;

            var entries = await _context.JournalEntries
                .AsNoTracking()
                .Where(e => e.Status != JournalStatuses.Void)
                .Select(e => new
                {
                    e.JournalEntryId,
                    e.EntryNo,
                    LineDebit = _context.JournalEntryLines
                        .Where(l => l.JournalEntryId == e.JournalEntryId)
                        .Sum(l => (decimal?)l.Debit) ?? 0m,
                    LineCredit = _context.JournalEntryLines
                        .Where(l => l.JournalEntryId == e.JournalEntryId)
                        .Sum(l => (decimal?)l.Credit) ?? 0m,
                    e.TotalDebit,
                    e.TotalCredit,
                    LineCount = _context.JournalEntryLines
                        .Count(l => l.JournalEntryId == e.JournalEntryId)
                })
                .ToListAsync();

            // The rule double entry exists for.
            var unbalanced = entries
                .Where(e => e.LineDebit != e.LineCredit)
                .Select(e => string.IsNullOrWhiteSpace(e.EntryNo)
                    ? e.JournalEntryId.ToString()
                    : e.EntryNo)
                .ToList();

            Add(report, "ledger.balanced", "Finance",
                "Every journal entry has equal debits and credits",
                unbalanced, IntegritySeverities.Critical,
                "These entries do not balance. Every financial statement is derived from the " +
                "ledger, so an unbalanced entry puts the trial balance out by its own error.");

            // The header carries its own totals for reporting; they have to agree with the
            // lines they summarise or two places would answer the same question differently.
            var headerDrift = entries
                .Where(e => e.TotalDebit != e.LineDebit || e.TotalCredit != e.LineCredit)
                .Select(e => string.IsNullOrWhiteSpace(e.EntryNo)
                    ? e.JournalEntryId.ToString()
                    : e.EntryNo)
                .ToList();

            Add(report, "ledger.header", "Finance",
                "An entry's recorded totals match the sum of its lines",
                headerDrift, IntegritySeverities.Critical,
                "The totals stored on the entry disagree with its own lines, so a report built " +
                "from the header and one built from the lines will not reconcile.");

            var emptyEntries = entries
                .Where(e => e.LineCount == 0)
                .Select(e => e.JournalEntryId.ToString())
                .ToList();

            Add(report, "ledger.empty", "Finance",
                "No journal entry is missing its lines",
                emptyEntries, IntegritySeverities.Warning,
                "These entries carry no lines at all, so they post nothing and cannot be " +
                "explained.");

            // Completed money the books were never told about. This is the one finding with a
            // documented, safe recovery, so it says so: posting is deliberately allowed to fall
            // behind the till, and the catch-up sweep is the supported way to close the gap.
            var unposted = await _context.Payments
                .AsNoTracking()
                .Where(p => p.Status == "Completed"
                         && !_context.JournalEntries.Any(
                                e => e.SourceEntityName == "Payment"
                                  && e.SourceEntityId == p.PaymentId.ToString()
                                  && e.Status != JournalStatuses.Void))
                .Select(p => p.PaymentId)
                .Take(MaxExamples + 1)
                .ToListAsync();

            Add(report, "ledger.unposted.payments", "Finance",
                "Every completed payment reached the ledger",
                unposted.Select(p => p.ToString()).ToList(), IntegritySeverities.Warning,
                "These payments were taken but never posted. Automatic posting is allowed to " +
                "fail without failing the payment, so this is recoverable: run the catch-up " +
                "sweep on the Finance module, or Post unposted on Payments → Reconciliation.");

            var unpostedSales = await _context.Sales
                .AsNoTracking()
                .Where(s => s.Status != "Cancelled"
                         && !_context.JournalEntries.Any(
                                e => e.SourceEntityName == "Sale"
                                  && e.SourceEntityId == s.SaleId.ToString()
                                  && e.Status != JournalStatuses.Void))
                .Select(s => s.SaleId)
                .Take(MaxExamples + 1)
                .ToListAsync();

            Add(report, "ledger.unposted.sales", "Finance",
                "Every live sale reached the ledger",
                unpostedSales.Select(s => s.ToString()).ToList(), IntegritySeverities.Warning,
                "These sales were rung up but never posted, so revenue and cost of sales are " +
                "understated by their value. The Finance catch-up sweep posts them.");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Runs an orphan query and records it. Always Critical: a parentless row is never intended.</summary>
        private async Task OrphanAsync(
            IntegrityReport report, string key, string area, string check, IQueryable<int> query)
        {
            var found = await query.Take(MaxExamples + 1).Select(id => id.ToString()).ToListAsync();

            Add(report, key, area, check, found, IntegritySeverities.Critical,
                "These rows reference a parent record that no longer exists. A foreign key " +
                "should make this impossible, so finding any means something wrote to the " +
                "database outside the application.");
        }

        private static void Add(
            IntegrityReport report, string key, string area, string check,
            List<string> found, string severity, string detail)
        {
            report.Checks.Add(new IntegrityCheckResult
            {
                Key = key,
                Area = area,
                Check = check,
                IssueCount = found.Count,
                Severity = severity,
                Detail = found.Count == 0 ? string.Empty : detail,
                Examples = found.Take(MaxExamples).ToList()
            });
        }
    }
}
