using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// What the stock on hand is worth, at weighted average cost.
    ///
    /// Two different numbers sit side by side here and the difference between them matters. Stock
    /// value is quantity times weighted average cost - what the gym actually paid for what is on
    /// the shelf, and the figure that appears on the balance sheet as an asset. Retail value is
    /// quantity times selling price, which is what it would fetch if all of it sold. The gap is
    /// margin that has not been earned yet, so it belongs in neither the books nor a revenue
    /// figure until something is actually sold.
    ///
    /// The costing is the server's: weighted average, maintained by the receipts that feed it,
    /// and frozen onto a sale line when goods go out. Nothing here recalculates it - a valuation
    /// screen that did its own arithmetic would be a second opinion about the same asset.
    /// </summary>
    internal sealed class ValuationPage : CrudPageBase<InventoryDto>
    {
        private readonly ComboBox _status;
        private readonly Label _summary;

        public ValuationPage(FitCoreSession session)
            : base(session, "Valuation",
                   "What the stock on hand is worth, at weighted average cost.",
                   "product", "Product name, code or category")
        {
            FilterBar.Controls.Add(UiKit.FilterLabel("Stock"));

            _status = UiKit.Select(150);
            _status.DisplayMember = "Value";
            _status.ValueMember = "Key";
            _status.DataSource = new List<KeyValuePair<string, string>>
            {
                new("", "All products"),
                new("In Stock", "In stock"),
                new("Low Stock", "Low stock"),
                new("Out of Stock", "Out of stock")
            };
            _status.SelectedIndexChanged += async (_, _) => await LoadAsync();
            FilterBar.Controls.Add(_status);

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

        protected override string EmptyHeadline => "Nothing to value yet";
        protected override string EmptyDetail =>
            "Add products under Products, then receive stock against a purchase to give them a cost.";

        protected override async Task<List<InventoryDto>?> FetchAsync()
        {
            var all = Unwrap(await Session.Inventory.GetAllAsync());
            if (all is null) return null;

            var wanted = _status.SelectedValue?.ToString();

            var rows = string.IsNullOrWhiteSpace(wanted)
                ? all
                : all.Where(i => string.Equals(i.StockStatus, wanted, StringComparison.OrdinalIgnoreCase))
                     .ToList();

            // Most valuable first: the question this screen answers is "where is the money
            // sitting", and that is not the order the catalogue happens to be in.
            return rows.OrderByDescending(i => i.StockValue).ToList();
        }

        protected override void DefineColumns()
        {
            Column(nameof(InventoryDto.ProductCode), "Code", 80);
            Column(nameof(InventoryDto.ProductName), "Product", 170);
            Column(nameof(InventoryDto.Category), "Category", 110);
            Column(nameof(InventoryDto.QuantityOnHand), "On hand", 70, "N2", rightAlign: true);
            MoneyColumn(nameof(InventoryDto.AverageCost), "Avg cost", 80);
            MoneyColumn(nameof(InventoryDto.LastUnitCost), "Last cost", 80);
            MoneyColumn(nameof(InventoryDto.StockValue), "Stock value", 90);
            MoneyColumn(nameof(InventoryDto.UnitPrice), "Sell price", 80);
            MoneyColumn(nameof(InventoryDto.RetailValue), "Retail value", 90);
            MoneyColumn(nameof(InventoryDto.PotentialMargin), "Margin", 80);
            StatusColumn(nameof(InventoryDto.StockStatus), "Status", 85);
        }

        protected override bool Matches(InventoryDto i, string term) =>
            (i.ProductName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (i.ProductCode ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (i.Category ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override void AfterLoad()
        {
            var stockValue = Items.Sum(i => i.StockValue);
            var retailValue = Items.Sum(i => i.RetailValue);
            var margin = retailValue - stockValue;

            var marginPercent = retailValue == 0m ? 0m : margin * 100m / retailValue;

            _summary.Text =
                $"{Items.Count:N0} product(s)      " +
                $"stock value {UiKit.Money(stockValue)}      " +
                $"retail {UiKit.Money(retailValue)}      " +
                $"unearned margin {UiKit.Money(margin)} ({marginPercent:N1}%)";
        }
    }
}
