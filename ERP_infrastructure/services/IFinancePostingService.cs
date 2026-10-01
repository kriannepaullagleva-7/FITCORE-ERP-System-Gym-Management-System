using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    /// <summary>
    /// The bridge between what the gym does and what the books say about it.
    ///
    /// Every operational service calls into here after it has committed its own work: a sale
    /// completes, a payment is taken, stock is received, a pay run is paid, an expense is
    /// recorded - and each one becomes a balanced journal entry without the operational service
    /// knowing anything about debits, credits or account codes.
    ///
    /// Three properties are deliberate:
    ///
    ///   - **Posting never fails the operation.** A sale that reached the database is a sale.
    ///     If the ledger cannot be written - a closed period, a chart that has been mangled -
    ///     the failure is reported and the sale stands. Finance follows operations; it does not
    ///     get a veto over the till.
    ///   - **Posting is idempotent.** Each entry records the source record that caused it, and
    ///     a second attempt for the same record does nothing. Retrying an operation cannot
    ///     double the books.
    ///   - **Posting is switchable.** A tenant can turn automatic posting off in Settings, in
    ///     which case these methods do nothing and the ledger is theirs to write by hand.
    /// </summary>
    public interface IFinancePostingService
    {
        /// <summary>Whether this tenant has automatic posting switched on.</summary>
        Task<bool> IsEnabledAsync();

        /// <summary>
        /// A completed sale: revenue earned, and the goods that left the shelf charged to cost
        /// of sales.
        ///
        /// <code>
        ///   Dr Accounts Receivable          the whole sale, since payments settle it separately
        ///   Dr Sales Discounts              anything taken off at the till
        ///       Cr Product Sales            the gross value of the lines
        ///   Dr Cost of Goods Sold           what those goods cost the gym
        ///       Cr Inventory                the stock they came out of
        /// </code>
        ///
        /// Receivable rather than cash because a sale and its payment are separate events here;
        /// the payment posting below is what moves the money.
        /// </summary>
        Task<PostingResult> PostSaleAsync(int saleId);

        /// <summary>Reverses whatever a sale posted, for a cancellation or a deletion.</summary>
        Task<PostingResult> ReverseSaleAsync(int saleId, string reason);

        /// <summary>
        /// Goods coming back: revenue reduced, stock and cost of sales restored.
        ///
        /// <code>
        ///   Dr Sales Returns                the value coming off revenue
        ///       Cr Cash                     the refund handed over
        ///   Dr Inventory                    the goods back on the shelf
        ///       Cr Cost of Goods Sold       the cost that is no longer a cost
        /// </code>
        ///
        /// When the goods were too damaged to restock, the second pair debits inventory
        /// shrinkage instead, so the loss is visible as a loss rather than as stock that was
        /// never there.
        /// </summary>
        Task<PostingResult> PostSaleReturnAsync(int saleReturnId);

        /// <summary>Undoes what a return posted, for one that was recorded in error.</summary>
        Task<PostingResult> ReverseSaleReturnAsync(int saleReturnId, string reason);

        /// <summary>
        /// A membership sold: revenue earned, and the member now owing for it.
        ///
        /// <code>
        ///   Dr Accounts Receivable          the plan price
        ///       Cr Membership Revenue       earned when the membership is sold
        /// </code>
        ///
        /// Recognised when the subscription starts rather than when it is paid, so a member
        /// who has signed up and not yet paid appears as money owed rather than as nothing at
        /// all. Their payment then clears the receivable, exactly as a sale's does.
        /// </summary>
        Task<PostingResult> PostSubscriptionAsync(int subscriptionId);

        /// <summary>
        /// A membership renewed, which is the same revenue event happening again.
        ///
        /// Renewing extends the existing subscription in place rather than creating a second
        /// row, so the renewal is keyed by the subscription *and* the term it starts. Without
        /// that a renewal would look to the idempotency check like the original sale and be
        /// silently skipped, and a gym whose members all renew would show no revenue at all
        /// after the first month.
        ///
        /// <paramref name="termStart"/> identifies the renewal; it is not the date the entry is
        /// posted under. A membership renewed early has a term starting when the current one
        /// ends, which can be a year away, and the ledger records when the renewal was sold.
        /// </summary>
        Task<PostingResult> PostSubscriptionRenewalAsync(int subscriptionId, DateTime termStart);

        Task<PostingResult> ReverseSubscriptionAsync(int subscriptionId, string reason);

        /// <summary>
        /// Money in from a member.
        ///
        /// <code>
        ///   Dr Cash on Hand / Cash in Bank  by the method it was taken
        ///       Cr Accounts Receivable      when it settles a sale or a subscription
        ///       Cr Other Income             when it settles neither
        /// </code>
        /// </summary>
        Task<PostingResult> PostPaymentAsync(int paymentId);

        Task<PostingResult> ReversePaymentAsync(int paymentId, string reason);

        /// <summary>
        /// Stock received against a purchase.
        ///
        /// <code>
        ///   Dr Inventory                    what arrived, at what it cost
        ///       Cr Accounts Payable         what the supplier is now owed
        /// </code>
        ///
        /// Nothing is expensed here. Goods bought for resale are an asset until they are sold -
        /// treating a delivery as an operating expense is what makes a month with a big order
        /// look like a loss and the month it sells through look like a windfall.
        /// </summary>
        Task<PostingResult> PostPurchaseReceiptAsync(int purchaseId);

        /// <summary>
        /// Money out to a supplier.
        ///
        /// <code>
        ///   Dr Accounts Payable             the debt being settled
        ///       Cr Cash on Hand / Bank      the money leaving
        /// </code>
        /// </summary>
        Task<PostingResult> PostSupplierPaymentAsync(int supplierPaymentId);

        /// <summary>
        /// A pay run marked paid.
        ///
        /// <code>
        ///   Dr Salaries and Wages           basic and regular hours
        ///   Dr Overtime                     hours beyond the shift
        ///   Dr Employer Contributions       the gym's own statutory share
        ///       Cr SSS / PhilHealth / Pag-IBIG / Withholding Tax Payable
        ///                                   both halves, held until remittance
        ///       Cr Cash on Hand             the net actually handed over
        /// </code>
        ///
        /// The employer share is an expense *and* a liability at the same moment, which is why
        /// it appears on both sides: the gym has incurred the cost but has not yet remitted it.
        /// </summary>
        Task<PostingResult> PostPayrollAsync(int payrollId);

        Task<PostingResult> ReversePayrollAsync(int payrollId, string reason);

        /// <summary>
        /// An operating expense.
        ///
        /// <code>
        ///   Dr the account for its category  (or General Expense)
        ///       Cr Cash / Bank               when it was paid
        ///       Cr Accounts Payable          when it is owed
        /// </code>
        /// </summary>
        Task<PostingResult> PostExpenseAsync(int expenseId);

        Task<PostingResult> ReverseExpenseAsync(int expenseId, string reason);

        /// <summary>
        /// A stock count that found more or less than the books said.
        ///
        /// A shortfall is a loss: <c>Dr Inventory Shrinkage / Cr Inventory</c>. A surplus is the
        /// reverse. Either way the inventory account and the physical count agree afterwards,
        /// which is the entire purpose of counting.
        /// </summary>
        Task<PostingResult> PostStockAdjustmentAsync(int stockMovementId);

        /// <summary>
        /// Posts anything that happened while automatic posting was off, or that failed at the
        /// time. Returns how many entries it raised, so the Finance screen can report it.
        /// </summary>
        Task<int> PostOutstandingAsync(DateTime? fromUtc = null);

        /// <summary>How many completed transactions have no journal entry yet.</summary>
        Task<int> CountOutstandingAsync(DateTime? fromUtc = null);
    }

    /// <summary>
    /// What happened when something was posted.
    ///
    /// A result rather than an exception because posting must never fail the operation that
    /// triggered it. The caller records the outcome and carries on; the Finance screen reports
    /// anything that did not land and offers to post it again.
    /// </summary>
    public sealed record PostingResult(bool Posted, int? JournalEntryId, string Message)
    {
        public static PostingResult Ok(int journalEntryId) =>
            new(true, journalEntryId, "");

        /// <summary>Already posted. Not a failure - it is what makes posting safe to retry.</summary>
        public static PostingResult AlreadyPosted(int journalEntryId) =>
            new(false, journalEntryId, "This transaction has already been posted to the ledger.");

        public static PostingResult Skipped(string reason) => new(false, null, reason);

        public static PostingResult Failed(string reason) => new(false, null, reason);
    }
}
