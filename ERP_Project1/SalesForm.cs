using System.Drawing;
using ERP_infrastructure.services;

namespace ERP_Project1
{
    // The Sales module, backed by the tenant database.
    //
    // The basket is staged in memory while the cashier builds it, but committing it calls
    // ISaleService.CreateSaleAsync, which writes the Sale and its SaleItems and deducts the
    // stock inside one transaction. Prices are read from the Products table rather than typed,
    // and the grids below are the real Sales and SaleItems rows.
    public partial class SalesForm : Form
    {
        private sealed class BasketLine
        {
            public int ProductId { get; init; }
            public string Product { get; init; } = string.Empty;
            public int Quantity { get; set; }
            public decimal UnitPrice { get; init; }
            public decimal OnHand { get; init; }
            public decimal LineTotal => Quantity * UnitPrice;
        }

        private readonly ISaleService _saleService;
        private readonly IMemberService _memberService;
        private readonly IInventoryService _inventoryService;

        private ComboBox cmbMember = null!, cmbProduct = null!;
        private NumericUpDown numQuantity = null!;
        private TextBox txtUnitPrice = null!;
        private Label lblStockOnHand = null!, lblCartTotal = null!, lblSalesSummary = null!;
        private Button btnAddItem = null!, btnRemoveItem = null!, btnClearCart = null!;
        private Button btnCreateSale = null!, btnCancelSale = null!, btnRefresh = null!;
        private DataGridView gridCart = null!, gridSales = null!, gridSaleItems = null!;

        private readonly List<BasketLine> _basket = new();

        // The catalogue as it stood at the last refresh, so the price and stock shown beside a
        // product are the ones the database actually holds.
        private List<InventoryView> _catalogue = new();

        private int _selectedSaleId = -1;
        private bool _isBusy;

        public SalesForm(
            ISaleService saleService,
            IMemberService memberService,
            IInventoryService inventoryService)
        {
            _saleService = saleService;
            _memberService = memberService;
            _inventoryService = inventoryService;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.BackColor = UiTheme.Canvas;
            this.Name = "SalesForm";

            var heading = UiTheme.CreateHeading("Sales", 16, 10, 300);

            var boxNew = new GroupBox
            {
                Text = "New Sale",
                Location = new Point(16, 40),
                Size = new Size(950, 210),
                BackColor = UiTheme.PaleBlue,
                ForeColor = UiTheme.TextBlue,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };

            boxNew.Controls.Add(UiTheme.CreateLabel("Member:", 16, 30, 60));
            cmbMember = new ComboBox
            {
                Location = new Point(82, 27),
                Size = new Size(240, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = "Text",
                ValueMember = "Value"
            };
            boxNew.Controls.Add(cmbMember);

            boxNew.Controls.Add(UiTheme.CreateLabel("Product:", 340, 30, 58));
            cmbProduct = new ComboBox
            {
                Location = new Point(402, 27),
                Size = new Size(260, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbProduct.SelectedIndexChanged += CmbProduct_SelectedIndexChanged;
            boxNew.Controls.Add(cmbProduct);

            boxNew.Controls.Add(UiTheme.CreateLabel("Qty:", 676, 30, 34));
            numQuantity = new NumericUpDown
            {
                Location = new Point(712, 27),
                Size = new Size(64, 25),
                Minimum = 1,
                Maximum = 100000,
                Value = 1
            };
            boxNew.Controls.Add(numQuantity);

            boxNew.Controls.Add(UiTheme.CreateLabel("Price:", 786, 30, 40));

            // Read-only: the server prices every line from the Products table when it commits
            // the sale, so letting the cashier type a price here would be a lie.
            txtUnitPrice = new TextBox
            {
                Location = new Point(828, 27),
                Size = new Size(90, 25),
                ReadOnly = true,
                BackColor = Color.White
            };
            boxNew.Controls.Add(txtUnitPrice);

            lblStockOnHand = UiTheme.CreateLabel("Pick a product to see its stock level.", 402, 58, 380);
            lblStockOnHand.ForeColor = UiTheme.Neutral;
            boxNew.Controls.Add(lblStockOnHand);

            btnAddItem = UiTheme.CreateButton("Add Item", 82, 56, UiTheme.Success, BtnAddItem_Click, 100, 28);
            btnRemoveItem = UiTheme.CreateButton("Remove", 190, 56, UiTheme.Danger, BtnRemoveItem_Click, 100, 28);
            boxNew.Controls.Add(btnAddItem);
            boxNew.Controls.Add(btnRemoveItem);

            gridCart = UiTheme.CreateGrid(82, 90, 660, 108);
            gridCart.AlternatingRowsDefaultCellStyle.BackColor = Color.White;
            boxNew.Controls.Add(gridCart);

            lblCartTotal = new Label
            {
                Text = "Total: 0.00",
                Location = new Point(756, 92),
                Size = new Size(176, 30),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = UiTheme.Success,
                TextAlign = ContentAlignment.MiddleLeft
            };
            boxNew.Controls.Add(lblCartTotal);

            btnCreateSale = UiTheme.CreateButton("Create Sale", 756, 126, UiTheme.Success, BtnCreateSale_Click, 176, 34);
            btnClearCart = UiTheme.CreateButton("Clear Basket", 756, 166, UiTheme.Neutral, BtnClearCart_Click, 176, 28);
            boxNew.Controls.Add(btnCreateSale);
            boxNew.Controls.Add(btnClearCart);

            btnCancelSale = UiTheme.CreateButton("Cancel Sale", 16, 258, UiTheme.Danger, BtnCancelSale_Click);
            btnRefresh = UiTheme.CreateButton("Refresh", 134, 258, UiTheme.Primary, BtnRefresh_Click);

            lblSalesSummary = UiTheme.CreateLabel("", 258, 266, 620);
            lblSalesSummary.ForeColor = UiTheme.Neutral;

            gridSales = UiTheme.CreateGrid(0, 0, 100, 100);
            gridSales.Dock = DockStyle.Fill;
            gridSales.SelectionChanged += GridSales_SelectionChanged;

            gridSaleItems = UiTheme.CreateGrid(0, 0, 100, 100);
            gridSaleItems.Dock = DockStyle.Bottom;
            gridSaleItems.Height = 150;

            var lblItems = new Label
            {
                Text = "Items on the selected sale",
                Dock = DockStyle.Bottom,
                Height = 22,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = UiTheme.TextBlue,
                Padding = new Padding(2, 4, 0, 0)
            };

            var header = UiTheme.CreateHeaderPanel(292);
            header.Controls.Add(heading);
            header.Controls.Add(boxNew);
            header.Controls.Add(btnCancelSale);
            header.Controls.Add(btnRefresh);
            header.Controls.Add(lblSalesSummary);

            var body = UiTheme.CreateBodyPanel();
            body.Controls.Add(gridSales);
            body.Controls.Add(lblItems);
            body.Controls.Add(gridSaleItems);

            this.Controls.Add(body);
            this.Controls.Add(header);

            this.Load += SalesForm_Load;
            this.ResumeLayout(false);
        }

        private async void SalesForm_Load(object? sender, EventArgs e)
        {
            await LoadLookupsAsync();
            await LoadSalesAsync();
            RefreshCart();
        }

        /// <summary>
        /// Fills the member and product pickers from the database. Products come from the
        /// inventory view so the stock figure beside each one is the real quantity on hand.
        /// </summary>
        private async Task LoadLookupsAsync()
        {
            SetBusy(true);
            try
            {
                var members = await _memberService.GetAllMembersAsync();

                cmbMember.DataSource = members
                    .OrderBy(m => m.FirstName)
                    .Select(m => new
                    {
                        Text = $"{m.FirstName} {m.LastName} (#{m.MemberId})",
                        Value = m.MemberId
                    })
                    .ToList();

                _catalogue = (await _inventoryService.GetInventoryAsync())
                    .Where(i => i.IsActive)
                    .OrderBy(i => i.ProductName)
                    .ToList();

                cmbProduct.Items.Clear();
                foreach (var item in _catalogue)
                    cmbProduct.Items.Add($"{item.ProductName} ({item.ProductCode})");

                if (cmbProduct.Items.Count > 0) cmbProduct.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                ShowError("Error loading members and products", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task LoadSalesAsync()
        {
            SetBusy(true);
            try
            {
                var sales = await _saleService.GetAllSalesAsync();

                gridSales.DataSource = sales
                    .Select(s => new
                    {
                        s.SaleId,
                        Member = s.MemberName,
                        Date = s.SaleDate,
                        Items = s.ItemCount,
                        Total = s.TotalAmount,
                        Paid = s.AmountPaid,
                        Balance = s.Balance,
                        Payment = s.PaymentStatus,
                        s.Status
                    })
                    .ToList();

                foreach (var column in new[] { "Total", "Paid", "Balance" })
                    if (gridSales.Columns.Contains(column))
                        gridSales.Columns[column]!.DefaultCellStyle.Format = "N2";

                var live = sales.Where(s => s.Status != "Cancelled").ToList();

                lblSalesSummary.Text =
                    $"{sales.Count} sale(s)  |  {live.Count} live worth {live.Sum(s => s.TotalAmount):N2}  " +
                    $"|  outstanding {live.Sum(s => s.Balance):N2}";
            }
            catch (Exception ex)
            {
                gridSales.DataSource = null;
                ShowError("Error loading sales", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void GridSales_SelectionChanged(object? sender, EventArgs e)
        {
            if (gridSales.CurrentRow is not { Selected: true }) return;
            if (gridSales.CurrentRow.Cells["SaleId"].Value is not int saleId) return;

            _selectedSaleId = saleId;

            try
            {
                var lines = await _saleService.GetSaleLinesAsync(saleId);

                gridSaleItems.DataSource = lines
                    .Select(l => new
                    {
                        l.ProductCode,
                        Product = l.ProductName,
                        Qty = l.Quantity,
                        Price = l.UnitPrice,
                        Total = l.Subtotal
                    })
                    .ToList();

                foreach (var column in new[] { "Price", "Total" })
                    if (gridSaleItems.Columns.Contains(column))
                        gridSaleItems.Columns[column]!.DefaultCellStyle.Format = "N2";
            }
            catch (Exception ex)
            {
                gridSaleItems.DataSource = null;
                ShowError("Error loading the items on that sale", ex);
            }
        }

        private void CmbProduct_SelectedIndexChanged(object? sender, EventArgs e)
        {
            var product = SelectedProduct();
            if (product is null) return;

            txtUnitPrice.Text = product.UnitPrice.ToString("0.00");

            // What is left once whatever is already staged in the basket is accounted for.
            var remaining = RemainingFor(product.ProductId);

            lblStockOnHand.Text = remaining > 0
                ? $"On hand: {product.QuantityOnHand:N0}  |  still available to add: {remaining:N0}"
                : $"On hand: {product.QuantityOnHand:N0} - nothing left to add to this sale.";

            lblStockOnHand.ForeColor = remaining > 0 ? UiTheme.Neutral : UiTheme.Danger;
        }

        private InventoryView? SelectedProduct() =>
            cmbProduct.SelectedIndex >= 0 && cmbProduct.SelectedIndex < _catalogue.Count
                ? _catalogue[cmbProduct.SelectedIndex]
                : null;

        private decimal RemainingFor(int productId)
        {
            var onHand = _catalogue.FirstOrDefault(c => c.ProductId == productId)?.QuantityOnHand ?? 0m;
            var staged = _basket.Where(b => b.ProductId == productId).Sum(b => b.Quantity);
            return onHand - staged;
        }

        private void BtnAddItem_Click(object? sender, EventArgs e)
        {
            var product = SelectedProduct();

            if (product is null)
            {
                MessageBox.Show("Pick a product first.", "Selection Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var quantity = (int)numQuantity.Value;
            var remaining = RemainingFor(product.ProductId);

            // Checked here so the cashier is told immediately; the server checks it again when
            // the sale is committed, which is what actually guarantees it.
            if (quantity > remaining)
            {
                MessageBox.Show(
                    $"Only {remaining:N0} unit(s) of {product.ProductName} are still available.\n\n" +
                    $"There are {product.QuantityOnHand:N0} on hand and " +
                    $"{product.QuantityOnHand - remaining:N0} already staged in this basket.",
                    "Not Enough Stock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var existing = _basket.FirstOrDefault(b => b.ProductId == product.ProductId);

            if (existing is not null)
            {
                existing.Quantity += quantity;
            }
            else
            {
                _basket.Add(new BasketLine
                {
                    ProductId = product.ProductId,
                    Product = product.ProductName,
                    Quantity = quantity,
                    UnitPrice = product.UnitPrice,
                    OnHand = product.QuantityOnHand
                });
            }

            RefreshCart();
            CmbProduct_SelectedIndexChanged(sender, e);
        }

        private void BtnRemoveItem_Click(object? sender, EventArgs e)
        {
            if (_basket.Count == 0)
            {
                MessageBox.Show("The basket is already empty.", "Nothing to Remove",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var index = gridCart.CurrentRow?.Index ?? _basket.Count - 1;
            if (index < 0 || index >= _basket.Count) index = _basket.Count - 1;

            _basket.RemoveAt(index);
            RefreshCart();
            CmbProduct_SelectedIndexChanged(sender, e);
        }

        private void BtnClearCart_Click(object? sender, EventArgs e)
        {
            _basket.Clear();
            RefreshCart();
            CmbProduct_SelectedIndexChanged(sender, e);
        }

        private async void BtnCreateSale_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;

            if (_basket.Count == 0)
            {
                MessageBox.Show("Add at least one item before creating the sale.", "Empty Basket",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (cmbMember.SelectedValue is not int memberId)
            {
                MessageBox.Show("Select the member this sale belongs to.", "Selection Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetBusy(true);
            try
            {
                var lines = _basket
                    .Select(b => new SaleLineRequest { ProductId = b.ProductId, Quantity = b.Quantity })
                    .ToList();

                var sale = await _saleService.CreateSaleAsync(memberId, lines);

                MessageBox.Show(
                    $"Sale #{sale.SaleId} created for {sale.TotalAmount:N2}.\n\n" +
                    "The sale, its items and the stock deductions were all written in one transaction.",
                    "Sale Created", MessageBoxButtons.OK, MessageBoxIcon.Information);

                _basket.Clear();
                RefreshCart();

                // Stock has moved, so the catalogue is re-read before the next sale is built.
                await LoadLookupsAsync();
                await LoadSalesAsync();
            }
            catch (Exception ex)
            {
                ShowError("Error creating the sale", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnCancelSale_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;

            if (_selectedSaleId == -1)
            {
                MessageBox.Show("Select a sale in the grid first.", "Selection Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Cancel sale #{_selectedSaleId}?\n\n" +
                "Its items go back into stock and any settled payments are marked refunded. " +
                "The sale itself is kept so the history still adds up.",
                "Confirm Cancellation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            SetBusy(true);
            try
            {
                var cancelled = await _saleService.CancelSaleAsync(_selectedSaleId, "Cancelled from the desktop app");

                if (cancelled is null)
                {
                    MessageBox.Show("That sale no longer exists.", "Not Found",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(
                        $"Sale #{cancelled.SaleId} was cancelled and its stock returned.",
                        "Sale Cancelled", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                await LoadLookupsAsync();
                await LoadSalesAsync();
            }
            catch (Exception ex)
            {
                ShowError("Error cancelling the sale", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnRefresh_Click(object? sender, EventArgs e)
        {
            await LoadLookupsAsync();
            await LoadSalesAsync();
        }

        private void RefreshCart()
        {
            gridCart.DataSource = _basket
                .Select(l => new
                {
                    l.Product,
                    Qty = l.Quantity,
                    Price = l.UnitPrice,
                    Total = l.LineTotal
                })
                .ToList();

            foreach (var column in new[] { "Price", "Total" })
                if (gridCart.Columns.Contains(column))
                    gridCart.Columns[column]!.DefaultCellStyle.Format = "N2";

            lblCartTotal.Text = $"Total: {_basket.Sum(l => l.LineTotal):N2}";
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;

            btnAddItem.Enabled = !busy;
            btnRemoveItem.Enabled = !busy;
            btnClearCart.Enabled = !busy;
            btnCreateSale.Enabled = !busy;
            btnCancelSale.Enabled = !busy;
            btnRefresh.Enabled = !busy;
        }

        private static void ShowError(string context, Exception ex)
        {
            MessageBox.Show($"{context}:\n\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
