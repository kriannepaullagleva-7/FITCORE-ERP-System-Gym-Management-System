using System.Drawing;
using ERP_infrastructure.services;

namespace ERP_Project1
{
    // The Inventory module, backed by the tenant database.
    //
    // Quantity on hand is never written directly. Stock In, Stock Out and Set To Qty each go
    // through IInventoryService, which writes a StockMovement row recording the balance before
    // and after, so the ledger at the bottom accounts for the whole of every change - including
    // the deductions made by the Sales module.
    public partial class InventoryForm : Form
    {
        private readonly IProductService _productService;
        private readonly IInventoryService _inventoryService;

        private TextBox txtProductName = null!, txtSku = null!, txtUnitPrice = null!;
        private TextBox txtCostPrice = null!, txtReorderLevel = null!;
        private ComboBox cmbCategory = null!, cmbMovementProduct = null!, cmbMovementReason = null!;
        private NumericUpDown numMovementQty = null!;
        private Button btnAddProduct = null!, btnUpdateProduct = null!, btnDeleteProduct = null!;
        private Button btnClear = null!, btnRefresh = null!;
        private Button btnStockIn = null!, btnStockOut = null!, btnAdjust = null!, btnSaveReorder = null!;
        private DataGridView gridStock = null!, gridMovements = null!;
        private Label lblSummary = null!, lblSelectedStock = null!;

        private List<InventoryView> _stock = new();
        private int _selectedProductId = -1;
        private bool _isBusy;

        public InventoryForm(IProductService productService, IInventoryService inventoryService)
        {
            _productService = productService;
            _inventoryService = inventoryService;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.BackColor = UiTheme.Canvas;
            this.Name = "InventoryForm";

            var heading = UiTheme.CreateHeading("Inventory", 16, 10, 300);

            // The module is docked into the shell, which leaves roughly 1035px of usable
            // width - both group boxes are sized to stay inside that.
            var boxProduct = new GroupBox
            {
                Text = "Product",
                Location = new Point(16, 40),
                Size = new Size(600, 132),
                BackColor = UiTheme.PaleBlue,
                ForeColor = UiTheme.TextBlue,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };

            boxProduct.Controls.Add(UiTheme.CreateLabel("Name:", 16, 32, 50));
            txtProductName = new TextBox { Location = new Point(70, 29), Size = new Size(170, 25) };
            boxProduct.Controls.Add(txtProductName);

            boxProduct.Controls.Add(UiTheme.CreateLabel("SKU:", 248, 32, 40));
            txtSku = new TextBox { Location = new Point(292, 29), Size = new Size(100, 25) };
            boxProduct.Controls.Add(txtSku);

            boxProduct.Controls.Add(UiTheme.CreateLabel("Cost:", 400, 32, 42));
            txtCostPrice = new TextBox { Location = new Point(446, 29), Size = new Size(90, 25) };
            boxProduct.Controls.Add(txtCostPrice);

            boxProduct.Controls.Add(UiTheme.CreateLabel("Category:", 16, 66, 62));
            cmbCategory = new ComboBox
            {
                Location = new Point(84, 63),
                Size = new Size(126, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            boxProduct.Controls.Add(cmbCategory);

            boxProduct.Controls.Add(UiTheme.CreateLabel("Price:", 216, 66, 40));
            txtUnitPrice = new TextBox { Location = new Point(258, 63), Size = new Size(80, 25) };
            boxProduct.Controls.Add(txtUnitPrice);

            boxProduct.Controls.Add(UiTheme.CreateLabel("Reorder:", 344, 66, 56));
            txtReorderLevel = new TextBox { Location = new Point(402, 63), Size = new Size(50, 25) };
            boxProduct.Controls.Add(txtReorderLevel);

            btnSaveReorder = UiTheme.CreateButton("Save Lvl", 458, 62, UiTheme.Neutral, BtnSaveReorder_Click, 78, 28);
            boxProduct.Controls.Add(btnSaveReorder);

            btnAddProduct = UiTheme.CreateButton("Add Product", 16, 96, UiTheme.Success, BtnAddProduct_Click, 104, 28);
            btnUpdateProduct = UiTheme.CreateButton("Update", 126, 96, UiTheme.Warning, BtnUpdateProduct_Click, 92, 28);
            btnDeleteProduct = UiTheme.CreateButton("Delete", 224, 96, UiTheme.Danger, BtnDeleteProduct_Click, 92, 28);
            btnClear = UiTheme.CreateButton("Clear", 322, 96, UiTheme.Neutral, BtnClear_Click, 84, 28);
            btnRefresh = UiTheme.CreateButton("Refresh", 412, 96, UiTheme.Primary, BtnRefresh_Click, 92, 28);

            boxProduct.Controls.Add(btnAddProduct);
            boxProduct.Controls.Add(btnUpdateProduct);
            boxProduct.Controls.Add(btnDeleteProduct);
            boxProduct.Controls.Add(btnClear);
            boxProduct.Controls.Add(btnRefresh);

            var boxMovement = new GroupBox
            {
                Text = "Stock Movement",
                Location = new Point(628, 40),
                Size = new Size(396, 132),
                BackColor = UiTheme.PaleBlue,
                ForeColor = UiTheme.TextBlue,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };

            boxMovement.Controls.Add(UiTheme.CreateLabel("Product:", 16, 32, 58));
            cmbMovementProduct = new ComboBox
            {
                Location = new Point(80, 29),
                Size = new Size(296, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbMovementProduct.SelectedIndexChanged += (_, _) => ShowSelectedStock();
            boxMovement.Controls.Add(cmbMovementProduct);

            boxMovement.Controls.Add(UiTheme.CreateLabel("Qty:", 16, 66, 32));
            numMovementQty = new NumericUpDown
            {
                Location = new Point(52, 63),
                Size = new Size(55, 25),
                Minimum = 0,
                Maximum = 100000,
                Value = 1
            };
            boxMovement.Controls.Add(numMovementQty);

            boxMovement.Controls.Add(UiTheme.CreateLabel("Reason:", 115, 66, 54));
            cmbMovementReason = new ComboBox
            {
                Location = new Point(173, 63),
                Size = new Size(203, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbMovementReason.Items.AddRange(new object[]
            {
                "Supplier delivery", "Cycle count correction", "Damaged / written off",
                "Returned by customer", "Transferred to another branch"
            });
            cmbMovementReason.SelectedIndex = 0;
            boxMovement.Controls.Add(cmbMovementReason);

            btnStockIn = UiTheme.CreateButton("Stock In", 16, 96, UiTheme.Success, BtnStockIn_Click, 92, 28);
            btnStockOut = UiTheme.CreateButton("Stock Out", 112, 96, UiTheme.Warning, BtnStockOut_Click, 92, 28);
            btnAdjust = UiTheme.CreateButton("Set To Qty", 208, 96, UiTheme.Primary, BtnAdjust_Click, 92, 28);
            boxMovement.Controls.Add(btnStockIn);
            boxMovement.Controls.Add(btnStockOut);
            boxMovement.Controls.Add(btnAdjust);

            lblSelectedStock = UiTheme.CreateLabel("", 306, 100, 80);
            lblSelectedStock.ForeColor = UiTheme.TextBlue;
            lblSelectedStock.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            boxMovement.Controls.Add(lblSelectedStock);

            lblSummary = UiTheme.CreateLabel("", 18, 180, 900);
            lblSummary.ForeColor = UiTheme.Neutral;

            gridStock = UiTheme.CreateGrid(0, 0, 100, 100);
            gridStock.Dock = DockStyle.Fill;
            gridStock.SelectionChanged += GridStock_SelectionChanged;
            gridStock.CellFormatting += GridStock_CellFormatting;

            gridMovements = UiTheme.CreateGrid(0, 0, 100, 100);
            gridMovements.Dock = DockStyle.Bottom;
            gridMovements.Height = 150;

            var lblMovements = new Label
            {
                Text = "Recent stock movements",
                Dock = DockStyle.Bottom,
                Height = 22,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = UiTheme.TextBlue,
                Padding = new Padding(2, 4, 0, 0)
            };

            var header = UiTheme.CreateHeaderPanel(204);
            header.Controls.Add(heading);
            header.Controls.Add(boxProduct);
            header.Controls.Add(boxMovement);
            header.Controls.Add(lblSummary);

            var body = UiTheme.CreateBodyPanel();
            body.Controls.Add(gridStock);
            body.Controls.Add(lblMovements);
            body.Controls.Add(gridMovements);

            this.Controls.Add(body);
            this.Controls.Add(header);

            this.Load += InventoryForm_Load;
            this.ResumeLayout(false);
        }

        private async void InventoryForm_Load(object? sender, EventArgs e)
        {
            await LoadCategoriesAsync();
            await LoadStockAsync();
            await LoadMovementsAsync();
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                var categories = await _productService.GetCategoriesAsync();

                cmbCategory.Items.Clear();
                foreach (var category in categories)
                    cmbCategory.Items.Add(category);

                if (cmbCategory.Items.Count > 0) cmbCategory.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                ShowError("Error loading product categories", ex);
            }
        }

        private async Task LoadStockAsync()
        {
            SetBusy(true);
            try
            {
                _stock = await _inventoryService.GetInventoryAsync();

                gridStock.DataSource = _stock
                    .Select(s => new
                    {
                        s.ProductId,
                        Product = s.ProductName,
                        Sku = s.ProductCode,
                        s.Category,
                        Cost = s.CostPrice,
                        Price = s.UnitPrice,
                        OnHand = s.QuantityOnHand,
                        Reorder = s.ReorderLevel,
                        Status = s.StockStatus,
                        Value = s.StockValue
                    })
                    .ToList();

                foreach (var column in new[] { "Cost", "Price", "OnHand", "Reorder", "Value" })
                    if (gridStock.Columns.Contains(column))
                        gridStock.Columns[column]!.DefaultCellStyle.Format = "N2";

                // The movement picker follows the same list, so it can never offer a product
                // that is not really there.
                var previous = cmbMovementProduct.SelectedIndex;
                cmbMovementProduct.Items.Clear();
                foreach (var row in _stock)
                    cmbMovementProduct.Items.Add($"{row.ProductName} ({row.ProductCode})");

                if (cmbMovementProduct.Items.Count > 0)
                {
                    cmbMovementProduct.SelectedIndex =
                        previous >= 0 && previous < cmbMovementProduct.Items.Count ? previous : 0;
                }

                var summary = await _inventoryService.GetSummaryAsync();

                lblSummary.Text =
                    $"{summary.TotalProducts} product(s)  |  In stock {summary.InStock}   " +
                    $"Low {summary.LowStock}   Out {summary.OutOfStock}  |  " +
                    $"Stock value {summary.StockValue:N2}  (retail {summary.RetailValue:N2})";

                ShowSelectedStock();
            }
            catch (Exception ex)
            {
                gridStock.DataSource = null;
                ShowError("Error loading stock", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task LoadMovementsAsync()
        {
            try
            {
                var movements = await _inventoryService.GetMovementsAsync(take: 100);

                gridMovements.DataSource = movements
                    .Select(m => new
                    {
                        Date = m.MovementDate,
                        Product = m.ProductName,
                        Type = m.MovementType,
                        Qty = m.Quantity,
                        Before = m.BalanceBefore,
                        After = m.BalanceAfter,
                        Reference = m.Reference,
                        Staff = m.RecordedByName,
                        Notes = m.Notes
                    })
                    .ToList();

                foreach (var column in new[] { "Qty", "Before", "After" })
                    if (gridMovements.Columns.Contains(column))
                        gridMovements.Columns[column]!.DefaultCellStyle.Format = "N2";
            }
            catch (Exception ex)
            {
                gridMovements.DataSource = null;
                ShowError("Error loading stock movements", ex);
            }
        }

        private void GridStock_SelectionChanged(object? sender, EventArgs e)
        {
            if (gridStock.CurrentRow is not { Selected: true }) return;
            if (gridStock.CurrentRow.Cells["ProductId"].Value is not int productId) return;

            var row = _stock.FirstOrDefault(s => s.ProductId == productId);
            if (row is null) return;

            _selectedProductId = productId;

            txtProductName.Text = row.ProductName;
            txtSku.Text = row.ProductCode;
            txtCostPrice.Text = row.CostPrice.ToString("0.00");
            txtUnitPrice.Text = row.UnitPrice.ToString("0.00");
            txtReorderLevel.Text = row.ReorderLevel.ToString("0");

            if (cmbCategory.Items.Contains(row.Category))
                cmbCategory.SelectedItem = row.Category;
        }

        private void GridStock_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= gridStock.Rows.Count) return;
            if (gridStock.Columns[e.ColumnIndex].Name != "Status") return;

            e.CellStyle.ForeColor = (e.Value as string) switch
            {
                "Out of Stock" => UiTheme.Danger,
                "Low Stock" => UiTheme.Warning,
                _ => UiTheme.Success
            };
            e.CellStyle.Font = new Font(gridStock.Font, FontStyle.Bold);
        }

        private InventoryView? SelectedMovementProduct() =>
            cmbMovementProduct.SelectedIndex >= 0 && cmbMovementProduct.SelectedIndex < _stock.Count
                ? _stock[cmbMovementProduct.SelectedIndex]
                : null;

        private void ShowSelectedStock()
        {
            var row = SelectedMovementProduct();
            lblSelectedStock.Text = row is null ? string.Empty : $"On hand: {row.QuantityOnHand:N0}";
        }

        private bool TryReadProductForm(
            out string name, out string sku, out string category,
            out decimal cost, out decimal price)
        {
            name = txtProductName.Text?.Trim() ?? string.Empty;
            sku = txtSku.Text?.Trim() ?? string.Empty;
            category = cmbCategory.SelectedItem?.ToString() ?? "Other";
            cost = 0m;
            price = 0m;

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Enter a product name.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtProductName.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(sku))
            {
                MessageBox.Show("Enter a SKU.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtSku.Focus();
                return false;
            }

            // Blank cost is treated as zero, which is how a product with no purchase price
            // recorded yet is handled everywhere else.
            if (!string.IsNullOrWhiteSpace(txtCostPrice.Text) &&
                (!decimal.TryParse(txtCostPrice.Text, out cost) || cost < 0))
            {
                MessageBox.Show("Cost must be a number and cannot be negative.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtCostPrice.Focus();
                return false;
            }

            if (!decimal.TryParse(txtUnitPrice.Text, out price) || price < 0)
            {
                MessageBox.Show("Selling price must be a number and cannot be negative.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtUnitPrice.Focus();
                return false;
            }

            return true;
        }

        private async void BtnAddProduct_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;
            if (!TryReadProductForm(out var name, out var sku, out var category, out var cost, out var price))
                return;

            decimal.TryParse(txtReorderLevel.Text, out var reorder);

            SetBusy(true);
            try
            {
                var product = await _productService.CreateProductAsync(
                    sku, name, category, cost, price, 0m, reorder);

                MessageBox.Show(
                    $"Product created. ID: {product.ProductId}\n\n" +
                    "Its stock record was opened at zero - use Stock In to receive the first delivery.",
                    "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                ClearForm();
                await LoadCategoriesAsync();
                await LoadStockAsync();
                await LoadMovementsAsync();
            }
            catch (Exception ex)
            {
                ShowError("Error creating the product", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnUpdateProduct_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;

            if (_selectedProductId == -1)
            {
                MessageBox.Show("Select a product to update.", "Selection Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!TryReadProductForm(out var name, out var sku, out var category, out var cost, out var price))
                return;

            SetBusy(true);
            try
            {
                var updated = await _productService.UpdateProductAsync(
                    _selectedProductId, sku, name, category, cost, price, true);

                if (updated is null)
                {
                    MessageBox.Show("That product no longer exists.", "Not Found",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show("Product updated.", "Success",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    ClearForm();
                }

                await LoadStockAsync();
            }
            catch (Exception ex)
            {
                ShowError("Error updating the product", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnDeleteProduct_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;

            if (_selectedProductId == -1)
            {
                MessageBox.Show("Select a product to delete.", "Selection Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Delete '{txtProductName.Text}'?\n\n" +
                "A product that appears on a past sale cannot be deleted, because that would " +
                "break the receipt it is on.",
                "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            SetBusy(true);
            try
            {
                var deleted = await _productService.DeleteProductAsync(_selectedProductId);

                MessageBox.Show(
                    deleted ? "Product deleted." : "That product no longer exists.",
                    deleted ? "Success" : "Not Found",
                    MessageBoxButtons.OK,
                    deleted ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

                ClearForm();
                await LoadStockAsync();
                await LoadMovementsAsync();
            }
            catch (Exception ex)
            {
                ShowError("Error deleting the product", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void BtnClear_Click(object? sender, EventArgs e) => ClearForm();

        private async void BtnRefresh_Click(object? sender, EventArgs e)
        {
            await LoadCategoriesAsync();
            await LoadStockAsync();
            await LoadMovementsAsync();
        }

        private async void BtnStockIn_Click(object? sender, EventArgs e) => await ApplyMovementAsync("In");

        private async void BtnStockOut_Click(object? sender, EventArgs e) => await ApplyMovementAsync("Out");

        private async void BtnAdjust_Click(object? sender, EventArgs e) => await ApplyMovementAsync("Adjust");

        private async void BtnSaveReorder_Click(object? sender, EventArgs e)
        {
            if (_isBusy) return;

            if (_selectedProductId == -1)
            {
                MessageBox.Show("Select a product in the grid first.", "Selection Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!decimal.TryParse(txtReorderLevel.Text, out var reorder) || reorder < 0)
            {
                MessageBox.Show("Enter a reorder level of zero or more.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtReorderLevel.Focus();
                return;
            }

            SetBusy(true);
            try
            {
                var row = await _inventoryService.SetReorderLevelAsync(_selectedProductId, reorder);

                MessageBox.Show(
                    $"Reorder level for {row.ProductName} set to {reorder:N0}. It is now {row.StockStatus}.",
                    "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                await LoadStockAsync();
            }
            catch (Exception ex)
            {
                ShowError("Error saving the reorder level", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        /// <summary>
        /// Applies a stock movement through the service, which records the ledger entry and
        /// enforces the rules - notably that stock cannot be taken below zero.
        /// </summary>
        private async Task ApplyMovementAsync(string kind)
        {
            if (_isBusy) return;

            var product = SelectedMovementProduct();

            if (product is null)
            {
                MessageBox.Show("Pick a product in the Stock Movement box first.", "Selection Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var quantity = (decimal)numMovementQty.Value;
            var reason = cmbMovementReason.SelectedItem?.ToString() ?? string.Empty;

            if (kind != "Adjust" && quantity <= 0)
            {
                MessageBox.Show("Enter a quantity greater than zero.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetBusy(true);
            try
            {
                var result = kind switch
                {
                    "In" => await _inventoryService.StockInAsync(
                        product.ProductId, quantity, "DESKTOP", reason),

                    "Out" => await _inventoryService.StockOutAsync(
                        product.ProductId, quantity, "DESKTOP", reason),

                    _ => await _inventoryService.AdjustAsync(
                        product.ProductId, quantity, reason)
                };

                MessageBox.Show(
                    $"{product.ProductName} is now {result.QuantityOnHand:N0} on hand ({result.StockStatus}).",
                    "Stock Updated", MessageBoxButtons.OK, MessageBoxIcon.Information);

                await LoadStockAsync();
                await LoadMovementsAsync();
            }
            catch (Exception ex)
            {
                ShowError("Error applying the stock movement", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ClearForm()
        {
            _selectedProductId = -1;
            txtProductName.Clear();
            txtSku.Clear();
            txtCostPrice.Clear();
            txtUnitPrice.Clear();
            txtReorderLevel.Clear();
            if (cmbCategory.Items.Count > 0) cmbCategory.SelectedIndex = 0;
            gridStock.ClearSelection();
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;

            btnAddProduct.Enabled = !busy;
            btnUpdateProduct.Enabled = !busy;
            btnDeleteProduct.Enabled = !busy;
            btnClear.Enabled = !busy;
            btnRefresh.Enabled = !busy;
            btnStockIn.Enabled = !busy;
            btnStockOut.Enabled = !busy;
            btnAdjust.Enabled = !busy;
            btnSaveReorder.Enabled = !busy;
        }

        private static void ShowError(string context, Exception ex)
        {
            MessageBox.Show($"{context}:\n\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
