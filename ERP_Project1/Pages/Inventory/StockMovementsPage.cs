using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The ledger of every stock movement: each in, out and adjustment, with the balance before
    /// and after it.
    ///
    /// Read-only on purpose. Stock is moved by the operation that caused it - a sale, a received
    /// purchase, a counted adjustment - and each of those writes its own movement row carrying
    /// the balance either side. Letting this screen edit a movement would let somebody change
    /// the history without changing the stock, which is the one thing a stock ledger exists to
    /// make impossible.
    /// </summary>
    internal sealed class StockMovementsPage : CrudPageBase<StockMovementDto>
    {
        private readonly ComboBox _type;
        private readonly Label _summary;

        public StockMovementsPage(FitCoreSession session)
            : base(session, "Stock Movements",
                   "Every in, out and adjustment, with the balance before and after.",
                   "movement", "Product, reference, supplier or who recorded it")
        {
            FilterBar.Controls.Add(UiKit.FilterLabel("Type"));

            _type = UiKit.Select(150);
            _type.DisplayMember = "Value";
            _type.ValueMember = "Key";
            // The four values the services actually write: "In" from a stock-in or a customer
            // return, "Sale" from the till, "Out" from goods going back to a supplier, and
            // "Adjustment" from a physical count. Offering a type nothing writes would give the
            // operator a filter that always comes back empty.
            _type.DataSource = new List<KeyValuePair<string, string>>
            {
                new("", "All movements"),
                new("In", "Stock in"),
                new("Sale", "Sold"),
                new("Out", "Stock out"),
                new("Adjustment", "Adjustment")
            };
            _type.SelectedIndexChanged += async (_, _) => await LoadAsync();
            FilterBar.Controls.Add(_type);

            _summary = new Label
            {
                AutoSize = true,
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                Margin = new Padding(16, 10, 0, 0),
                UseMnemonic = false
            };
            FilterBar.Controls.Add(_summary);
        }

        protected override bool SupportsAdd => false;
        protected override bool SupportsEdit => false;
        protected override bool SupportsDelete => false;

        protected override string EmptyHeadline => "No stock movements yet";
        protected override string EmptyDetail =>
            "Receiving a purchase, ringing up a sale or adjusting a count all record a movement here.";

        protected override async Task<List<StockMovementDto>?> FetchAsync()
        {
            var all = Unwrap(await Session.Inventory.GetMovementsAsync(take: 500));
            if (all is null) return null;

            // The endpoint does not filter by type, so the narrowing happens here. It is a
            // choice of what to show rather than a business rule, and the rows were already
            // fetched - which is also why the empty state and the counts below stay honest.
            var wanted = _type.SelectedValue?.ToString();

            return string.IsNullOrWhiteSpace(wanted)
                ? all
                : all.Where(m => string.Equals(m.MovementType, wanted, StringComparison.OrdinalIgnoreCase))
                     .ToList();
        }

        protected override void DefineColumns()
        {
            Column(nameof(StockMovementDto.MovementDate), "When", 100, "d MMM yyyy HH:mm");
            Column(nameof(StockMovementDto.ProductCode), "Code", 80);
            Column(nameof(StockMovementDto.ProductName), "Product", 160);
            StatusColumn(nameof(StockMovementDto.MovementType), "Type", 80);
            Column(nameof(StockMovementDto.Quantity), "Qty", 60, "N2", rightAlign: true);
            Column(nameof(StockMovementDto.BalanceBefore), "Before", 70, "N2", rightAlign: true);
            Column(nameof(StockMovementDto.BalanceAfter), "After", 70, "N2", rightAlign: true);
            Column(nameof(StockMovementDto.SupplierName), "Supplier", 110);
            StatusColumn(nameof(StockMovementDto.Reference), "Reference", 100);
            Column(nameof(StockMovementDto.PerformedBy), "Recorded by", 100);
        }

        protected override bool Matches(StockMovementDto m, string term) =>
            (m.ProductName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (m.ProductCode ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (m.Reference ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (m.SupplierName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (m.PerformedBy ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (m.Notes ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override void AfterLoad()
        {
            // Sales leave the building exactly as supplier returns do, so both count as stock
            // out. Summing only "Out" reported nothing sold, which is the one number a gym's
            // stock ledger is most often opened to check.
            //
            // Adjustments are left out of both totals rather than guessed at: an adjustment
            // records a counted quantity and can move the balance either way, so folding it
            // into one side would misstate that side.
            var inward = Items
                .Where(m => string.Equals(m.MovementType, "In", StringComparison.OrdinalIgnoreCase))
                .Sum(m => m.Quantity);

            var outward = Items
                .Where(m => string.Equals(m.MovementType, "Out", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(m.MovementType, "Sale", StringComparison.OrdinalIgnoreCase))
                .Sum(m => m.Quantity);

            var adjusted = Items
                .Count(m => string.Equals(m.MovementType, "Adjustment", StringComparison.OrdinalIgnoreCase));

            _summary.Text = $"{Items.Count:N0} movement(s)      " +
                            $"in {inward:N2}      out {outward:N2}" +
                            (adjusted == 0 ? "" : $"      {adjusted:N0} adjustment(s)");
        }
    }
}
