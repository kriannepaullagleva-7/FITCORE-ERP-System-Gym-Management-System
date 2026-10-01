using System.ComponentModel;
using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    // -------------------------------------------------------------------------------- POS

    /// <summary>
    /// The till. Prices come from the catalogue, the total is recomputed by the server on
    /// commit, and stock is deducted inside the same transaction - so this screen never
    /// decides what anything costs or whether there is enough stock. What it does do is stop
    /// the obvious mistakes before they become a round trip: an empty cart, no member, a
    /// quantity beyond what is on the shelf.
    /// </summary>
    internal sealed class PosPage : ModulePageBase
    {
        private sealed class CartLine
        {
            public int ProductId { get; set; }
            public string ProductCode { get; set; } = "";
            public string ProductName { get; set; } = "";
            public decimal UnitPrice { get; set; }
            public int Quantity { get; set; }
            public decimal OnHand { get; set; }
            public decimal LineTotal => UnitPrice * Quantity;
        }

        private readonly BindingList<CartLine> _cart = new();
        private List<InventoryDto> _stock = new();

        private DataGridView _catalogue = null!;
        private DataGridView _cartGrid = null!;
        private TextBox _productSearch = null!;
        private TextBox _discount = null!;
        private Label _subtotalValue = null!;
        private Label _discountValue = null!;
        private Label _totalValue = null!;
        private Label _cartEmpty = null!;
        private CheckBox _takePayment = null!;
        private ComboBox _method = null!;
        private TextBox _tendered = null!;
        private Label _changeValue = null!;
        private Button _confirm = null!;
        private Button _addToCart = null!;

        protected override bool UsesGrid => false;

        public PosPage(FitCoreSession session)
            : base(session, "New Sale", "Add products, then confirm.")
        {
            BuildBody();
        }

        // ------------------------------------------------------------------ layout

        private void BuildBody()
        {
            var split = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UiTheme.Canvas
            };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

            split.Controls.Add(BuildCatalogue(), 0, 0);
            split.Controls.Add(BuildTicket(), 1, 0);

            SetBody(split);
        }

        private Panel BuildCatalogue()
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Canvas,
                Padding = new Padding(1),
                Margin = new Padding(0, 0, UiTheme.SpaceM, 0)
            };
            card.Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, card.Width, card.Height), UiTheme.Surface, UiTheme.Border);

            var head = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = UiTheme.Surface };

            head.Controls.Add(new Label
            {
                Text = "Catalogue",
                Location = new Point(16, 14),
                AutoSize = true,
                Font = UiTheme.SectionTitle,
                ForeColor = UiTheme.TextPrimary,
                UseMnemonic = false
            });

            var searchField = UiKit.SearchField(out _productSearch, "Product name or SKU", 300);
            searchField.Location = new Point(16, 44);
            _productSearch.TextChanged += (_, _) => BindCatalogue();
            head.Controls.Add(searchField);

            _addToCart = UiKit.Action("Add to cart", ButtonTone.Primary, (_, _) => AddSelected(), 132);
            _addToCart.Location = new Point(328, 44);
            head.Controls.Add(_addToCart);

            _catalogue = UiKit.Grid();
            _catalogue.AutoGenerateColumns = false;
            _catalogue.Columns.Add(new DataGridViewTextBoxColumn
            { DataPropertyName = nameof(InventoryDto.ProductCode), HeaderText = "SKU", FillWeight = 58 });
            _catalogue.Columns.Add(new DataGridViewTextBoxColumn
            { DataPropertyName = nameof(InventoryDto.ProductName), HeaderText = "Product", FillWeight = 150 });
            _catalogue.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(InventoryDto.UnitPrice), HeaderText = "Price", FillWeight = 62,
                DefaultCellStyle = { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight }
            });
            _catalogue.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(InventoryDto.QuantityOnHand), HeaderText = "On hand", FillWeight = 60,
                DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight }
            });
            _catalogue.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = nameof(InventoryDto.StockStatus),
                DataPropertyName = nameof(InventoryDto.StockStatus), HeaderText = "Status", FillWeight = 70
            });
            UiKit.PaintStatusColumns(_catalogue, nameof(InventoryDto.StockStatus));
            UiKit.SetMinimumColumnWidths(_catalogue);

            _catalogue.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) AddSelected(); };

            card.Controls.Add(_catalogue);
            card.Controls.Add(head);
            return card;
        }

        private Panel BuildTicket()
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Canvas,
                Padding = new Padding(16, 14, 16, 14)
            };
            card.Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, card.Width, card.Height), UiTheme.Surface, UiTheme.Border);

            // Bottom-up docking: the totals and the confirm button are pinned, the cart grid
            // takes whatever is left, so a long ticket scrolls instead of pushing the button
            // off the screen.
            var footer = BuildTotals();
            var lineActions = BuildLineActions();
            var header = BuildTicketHeader();

            var cartHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Surface,
                Padding = new Padding(0, 6, 0, 6)
            };

            _cartGrid = UiKit.Grid();
            _cartGrid.AutoGenerateColumns = false;
            _cartGrid.Columns.Add(new DataGridViewTextBoxColumn
            { DataPropertyName = nameof(CartLine.ProductName), HeaderText = "Product", FillWeight = 130 });
            _cartGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(CartLine.Quantity), HeaderText = "Qty", FillWeight = 42,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
            });
            _cartGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(CartLine.UnitPrice), HeaderText = "Unit price", FillWeight = 62,
                DefaultCellStyle = { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight }
            });
            _cartGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(CartLine.LineTotal), HeaderText = "Total", FillWeight = 66,
                DefaultCellStyle = { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight }
            });
            _cartGrid.DataSource = _cart;
            UiKit.SetMinimumColumnWidths(_cartGrid);

            _cartEmpty = new Label
            {
                Dock = DockStyle.Fill,
                Text = "The cart is empty.\r\n\r\nFind a product on the left and choose Add to cart.",
                Font = UiTheme.Body,
                ForeColor = UiTheme.TextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = UiTheme.Surface,
                UseMnemonic = false
            };

            cartHost.Controls.Add(_cartGrid);
            cartHost.Controls.Add(_cartEmpty);

            card.Controls.Add(cartHost);
            card.Controls.Add(lineActions);
            card.Controls.Add(footer);
            card.Controls.Add(header);

            return card;
        }

        private Panel BuildTicketHeader()
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 30,
                BackColor = UiTheme.Surface
            };

            // Every sale here is a walk-in - who rang it up and who it was for are both taken
            // from the server side of the transaction (the signed-in account, and "Walk-In" when
            // no customer record applies) rather than picked from a list on this screen.
            header.Controls.Add(new Label
            {
                Text = "Current sale",
                Location = new Point(0, 0),
                AutoSize = true,
                Font = UiTheme.SectionTitle,
                ForeColor = UiTheme.TextPrimary,
                UseMnemonic = false
            });

            return header;
        }

        private FlowLayoutPanel BuildLineActions()
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.Surface,
                Padding = new Padding(0, 6, 0, 0)
            };

            row.Controls.Add(UiKit.Action("＋", ButtonTone.Secondary, (_, _) => Bump(1), 46));
            row.Controls.Add(UiKit.Action("−", ButtonTone.Secondary, (_, _) => Bump(-1), 46));
            row.Controls.Add(UiKit.Action("Remove line", ButtonTone.Secondary, (_, _) => RemoveLine(), 106));
            row.Controls.Add(UiKit.Action("Clear cart", ButtonTone.Secondary, (_, _) => ClearCart(), 100));

            return row;
        }

        private Panel BuildTotals()
        {
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 288,
                BackColor = UiTheme.Surface,
                Padding = new Padding(0, 10, 0, 0)
            };
            footer.Paint += (_, e) =>
            {
                using var pen = new Pen(UiTheme.Border);
                e.Graphics.DrawLine(pen, 0, 4, footer.Width, 4);
            };

            Label Row(string caption, int y, bool strong, out Label value)
            {
                var label = new Label
                {
                    Text = caption,
                    Location = new Point(0, y),
                    AutoSize = true,
                    Font = strong ? new Font(UiTheme.FamilySemibold, 12F) : UiTheme.Body,
                    ForeColor = strong ? UiTheme.TextPrimary : UiTheme.TextSecondary,
                    UseMnemonic = false
                };

                value = new Label
                {
                    Text = "0.00",
                    AutoSize = false,
                    Size = new Size(140, strong ? 26 : 20),
                    Location = new Point(footer.Width - 140, y),
                    TextAlign = ContentAlignment.MiddleRight,
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Font = strong ? new Font(UiTheme.FamilySemibold, 15F) : UiTheme.Body,
                    ForeColor = strong ? UiTheme.Primary : UiTheme.TextPrimary,
                    UseMnemonic = false
                };

                footer.Controls.Add(label);
                footer.Controls.Add(value);
                return label;
            }

            Row("Subtotal", 14, false, out _subtotalValue);

            var discountLabel = Row("Discount", 40, false, out _discountValue);
            _ = discountLabel;

            _discount = new TextBox
            {
                Location = new Point(72, 37),
                Width = 90,
                Font = UiTheme.Body,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Right,
                Text = "0"
            };
            _discount.TextChanged += (_, _) => Recalculate();
            UiKit.AttachNumericFilter(_discount);
            footer.Controls.Add(_discount);

            Row("Total", 70, true, out _totalValue);

            // ---- payment ----
            _takePayment = new CheckBox
            {
                Text = "Record payment now",
                Location = new Point(0, 112),
                AutoSize = true,
                Font = UiTheme.Body,
                ForeColor = UiTheme.TextPrimary,
                Checked = Session.Can(Modules.Payments)
            };
            _takePayment.CheckedChanged += (_, _) => _method.Enabled = _takePayment.Checked;

            _method = UiKit.Select(150);
            _method.Location = new Point(170, 110);
            _method.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _method.DisplayMember = "Value";
            _method.ValueMember = "Key";
            _method.DataSource = new List<KeyValuePair<string, string>>
            {
                new("Cash", "Cash"), new("Card", "Card"), new("GCash", "GCash"),
                new("Transfer", "Transfer"), new("Check", "Check")
            };

            // Amount tendered and change - cash only, and only meaningful when a payment is
            // being taken now. Left blank, the sale is assumed paid exactly to the total.
            var tenderedLabel = new Label
            {
                Text = "Amount paid",
                Location = new Point(0, 142),
                AutoSize = true,
                Font = UiTheme.Body,
                ForeColor = UiTheme.TextSecondary,
                UseMnemonic = false
            };

            _tendered = new TextBox
            {
                Location = new Point(90, 139),
                Width = 100,
                Font = UiTheme.Body,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Right,
                PlaceholderText = "0.00"
            };
            _tendered.TextChanged += (_, _) => Recalculate();
            UiKit.AttachNumericFilter(_tendered);

            var changeLabel = new Label
            {
                Text = "Change",
                Location = new Point(210, 142),
                AutoSize = true,
                Font = UiTheme.Body,
                ForeColor = UiTheme.TextSecondary,
                UseMnemonic = false
            };

            _changeValue = new Label
            {
                Text = "0.00",
                Location = new Point(265, 142),
                Size = new Size(95, 20),
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font(UiTheme.FamilySemibold, 10F),
                ForeColor = UiTheme.TextPrimary,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                UseMnemonic = false
            };

            // Only offered when the operator holds Payments; otherwise the sale is recorded
            // and settled later by somebody who does.
            if (Session.Can(Modules.Payments))
            {
                footer.Controls.Add(_takePayment);
                footer.Controls.Add(_method);
                footer.Controls.Add(tenderedLabel);
                footer.Controls.Add(_tendered);
                footer.Controls.Add(changeLabel);
                footer.Controls.Add(_changeValue);
            }

            _confirm = UiKit.Action("Confirm sale", ButtonTone.Success,
                async (_, _) => await ConfirmAsync(), 360, 48);
            _confirm.Location = new Point(0, 176);
            _confirm.Font = new Font(UiTheme.FamilySemibold, 11F);
            _confirm.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            footer.Controls.Add(_confirm);

            footer.Resize += (_, _) => _confirm.Width = footer.Width;

            return footer;
        }

        // ------------------------------------------------------------------ data

        public override Task LoadAsync() => GuardAsync(async () =>
        {
            _stock = Unwrap(await Session.Inventory.GetAllAsync()) ?? new();

            BindCatalogue();
            Recalculate();

            var sellable = _stock.Count(s => s.IsActive && s.QuantityOnHand > 0);
            SetStatus($"{sellable:N0} sellable product(s)");
        }, "Loading the till…");

        private void BindCatalogue()
        {
            var term = _productSearch.Text.Trim();

            var view = _stock
                .Where(s => s.IsActive && s.QuantityOnHand > 0)
                .Where(s => string.IsNullOrWhiteSpace(term) ||
                            s.ProductName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                            s.ProductCode.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();

            _catalogue.DataSource = new BindingList<InventoryDto>(view);
        }

        // ------------------------------------------------------------------ cart

        private void AddSelected()
        {
            if (_catalogue.CurrentRow?.DataBoundItem is not InventoryDto item)
            {
                ShowError("Choose a product from the catalogue first.");
                return;
            }

            if (!item.IsActive)
            {
                ShowError($"{item.ProductName} is not available to sell.");
                return;
            }

            if (item.QuantityOnHand <= 0)
            {
                ShowError($"Insufficient stock for {item.ProductName}. There is none on hand.");
                return;
            }

            var line = _cart.FirstOrDefault(l => l.ProductId == item.ProductId);

            if (line is null)
            {
                _cart.Add(new CartLine
                {
                    ProductId = item.ProductId,
                    ProductCode = item.ProductCode,
                    ProductName = item.ProductName,
                    UnitPrice = item.UnitPrice,
                    Quantity = 1,
                    OnHand = item.QuantityOnHand
                });
            }
            else
            {
                if (line.Quantity + 1 > line.OnHand)
                {
                    ShowError($"Insufficient stock for {line.ProductName}. " +
                              $"Only {line.OnHand:N0} on hand and {line.Quantity:N0} already in the cart.");
                    return;
                }

                line.Quantity++;
                _cart.ResetBindings();
            }

            ShowError(null);
            Recalculate();
        }

        private void Bump(int delta)
        {
            if (_cartGrid.CurrentRow?.DataBoundItem is not CartLine line)
            {
                ShowError("Choose a line in the cart first.");
                return;
            }

            var next = line.Quantity + delta;

            // Dropping to zero means the operator wants the line gone.
            if (next <= 0)
            {
                _cart.Remove(line);
                Recalculate();
                return;
            }

            if (next > line.OnHand)
            {
                ShowError($"Insufficient stock for {line.ProductName}. Only {line.OnHand:N0} on hand.");
                return;
            }

            line.Quantity = next;
            _cart.ResetBindings();
            ShowError(null);
            Recalculate();
        }

        private void RemoveLine()
        {
            if (_cartGrid.CurrentRow?.DataBoundItem is not CartLine line)
            {
                ShowError("Choose a line in the cart first.");
                return;
            }

            _cart.Remove(line);
            Recalculate();
        }

        private void ClearCart()
        {
            if (_cart.Count == 0) return;

            if (UiKit.Confirm("Clear the whole cart?\r\n\r\nNothing has been saved, so no sale is lost.",
                    "Clear cart") != DialogResult.Yes) return;

            _cart.Clear();
            _discount.Text = "0";
            ShowError(null);
            Recalculate();
        }

        private decimal Discount()
        {
            decimal.TryParse(_discount.Text, out var discount);
            return discount < 0 ? 0m : discount;
        }

        private void Recalculate()
        {
            var subtotal = _cart.Sum(l => l.LineTotal);
            var discount = Math.Min(Discount(), subtotal);
            var total = Math.Max(0m, subtotal - discount);

            _subtotalValue.Text = UiKit.Money(subtotal);
            _discountValue.Text = UiKit.Money(discount);
            _totalValue.Text = UiKit.Money(total);

            if (_changeValue is not null)
            {
                var change = decimal.TryParse(_tendered.Text, out var tendered) ? tendered - total : 0m;
                _changeValue.Text = UiKit.Money(change > 0 ? change : 0m);
            }

            _confirm.Text = _cart.Count == 0
                ? "Confirm sale"
                : $"Confirm sale  ·  {UiKit.Money(total)}";

            _confirm.Enabled = _cart.Count > 0 && !IsBusy;

            _cartEmpty.Visible = _cart.Count == 0;
            _cartGrid.Visible = _cart.Count > 0;
        }

        protected override void SetToolbarEnabled(bool enabled)
        {
            base.SetToolbarEnabled(enabled);

            // The POS keeps its controls outside the toolbar, so they are disabled by hand
            // while the sale is committing. This is what stops a second Confirm.
            if (_confirm is null) return;

            _confirm.Enabled = enabled && _cart.Count > 0;
            _addToCart.Enabled = enabled;
            _discount.Enabled = enabled;
            _tendered.Enabled = enabled;
        }

        // ------------------------------------------------------------------ commit

        private async Task ConfirmAsync()
        {
            if (IsBusy) return;

            if (_cart.Count == 0)
            {
                ShowError("Add at least one product before confirming the sale.");
                return;
            }

            if (_cart.Any(l => l.Quantity <= 0))
            {
                ShowError("Every line must have a quantity of at least one.");
                return;
            }

            var subtotal = _cart.Sum(l => l.LineTotal);
            var discount = Discount();

            if (discount > subtotal)
            {
                ShowError($"The discount cannot be more than the subtotal of {UiKit.Money(subtotal)}.");
                return;
            }

            var total = subtotal - discount;

            decimal? amountTendered = TakingPayment() && decimal.TryParse(_tendered.Text, out var t)
                ? t
                : null;

            if (amountTendered is decimal tenderedAmount && tenderedAmount < total)
            {
                ShowError($"Amount paid of {UiKit.Money(tenderedAmount)} is less than the total due of {UiKit.Money(total)}.");
                return;
            }

            var summary =
                $"Complete this sale?\r\n\r\n" +
                $"Items:     {_cart.Count} line(s), {_cart.Sum(l => l.Quantity):N0} unit(s)\r\n" +
                $"Subtotal:  {UiKit.Money(subtotal)}\r\n" +
                $"Discount:  {UiKit.Money(discount)}\r\n" +
                $"Total:     {UiKit.Money(total)}\r\n\r\n" +
                (TakingPayment()
                    ? $"A {_method.Text} payment of {UiKit.Money(total)} will be recorded against it.\r\n\r\n"
                    : "No payment will be recorded, so the sale will show as unpaid.\r\n\r\n") +
                "Stock is deducted when the sale is saved.";

            // The one confirmation in the application that still took money behind a native
            // Yes/No box. It names what it is about to do, like every other one now does, and
            // the safe button is what Enter and Escape reach.
            if (ConfirmDialog.Show(this, "Confirm sale", summary,
                    "Complete sale", "Cancel", ButtonTone.Success) != DialogResult.Yes)
            {
                return;
            }

            await GuardAsync(async () =>
            {
                // Every sale here is a walk-in, and the cashier is never picked from a list -
                // the server already attributes it to whoever is signed in (falling back to
                // their own linked employee record when one exists), from the token rather than
                // from anything this screen could send.
                var dto = new CreateSaleDto
                {
                    MemberId = null,
                    WalkInName = null,
                    Discount = discount,

                    // The server writes the sale, moves the stock and records the payment in
                    // one transaction. Posting a separate payment afterwards, as this screen
                    // used to, could leave a completed sale looking unpaid if the second call
                    // failed.
                    SettleNow = TakingPayment(),
                    PaymentMethod = _method.SelectedValue?.ToString() ?? "Cash",
                    AmountTendered = amountTendered,

                    Items = _cart.Select(l => new CreateSaleItemDto
                    {
                        ProductId = l.ProductId,
                        Quantity = l.Quantity
                    }).ToList()
                };

                var result = await Session.Sales.CreateAsync(dto);

                if (!result.IsSuccess)
                {
                    // Insufficient stock, an inactive product or an unknown member all surface
                    // here with the server's own wording.
                    ShowError(result.ErrorMessage);
                    return;
                }

                var sale = result.Value!;

                _cart.Clear();
                _discount.Text = "0";
                _tendered.Text = "";

                await LoadAsync();
                Notify($"Sale #{sale.SaleId} completed successfully.");

                // The receipt is built from what the server stored, not from the cart, so it
                // shows the prices and the cashier the database actually holds.
                await ShowSaleReceiptAsync(sale.SaleId);
            }, "Completing the sale…");
        }

        private bool TakingPayment() =>
            Session.Can(Modules.Payments) && _takePayment.Checked;

        /// <summary>
        /// Opens the receipt for a sale. Re-reads the sale from the server so the document
        /// reflects what was committed, including the payment written alongside it.
        /// </summary>
        private async Task ShowSaleReceiptAsync(int saleId)
        {
            var detail = await Session.Sales.GetDetailAsync(saleId);

            if (!detail.IsSuccess || detail.Value is null)
            {
                ShowError(detail.ErrorMessage ?? "The receipt could not be loaded.");
                return;
            }

            ReceiptDialog.Show(this, ReceiptBuilder.ForSale(
                Session, detail.Value.Sale, detail.Value.Lines, detail.Value.Payments));
        }
    }

    // ------------------------------------------------------------------------------ Sales

    internal sealed class SalesPage : CrudPageBase<SaleViewDto>
    {
        public SalesPage(FitCoreSession session)
            : base(session, "Sales History", "Every counter transaction and what is still owed on it.",
                   "sale", "Sale ID, member or cashier")
        {
            AddAction("Cancel sale", ButtonTone.Secondary, () => CancelAsync(), 110);
            AddAction("View lines", ButtonTone.Secondary, () => ShowLinesAsync(), 104);
            AddAction("View receipt", ButtonTone.Secondary, () => ShowReceiptAsync(), 116);
        }

        protected override bool SupportsAdd => false;
        protected override bool SupportsEdit => false;

        protected override string DeleteConsequence =>
            "Deleting a sale removes it from every revenue figure and returns its stock. " +
            "Cancel it instead if you want the transaction kept on record. This cannot be undone.";

        protected override string EmptyHeadline => "No sales recorded yet";
        protected override string EmptyDetail =>
            "Sales rung up at the till appear here with what has been paid and what is still owed.";

        protected override async Task<List<SaleViewDto>?> FetchAsync() =>
            Unwrap(await Session.Sales.GetAllAsync());

        protected override void DefineColumns()
        {
            Column(nameof(SaleViewDto.SaleId), "ID", 40, rightAlign: true);
            DateColumn(nameof(SaleViewDto.SaleDate), "Date", 90);
            Column(nameof(SaleViewDto.MemberName), "Member", 130);
            Column(nameof(SaleViewDto.ProcessedBy), "Cashier", 105);
            Column(nameof(SaleViewDto.TotalQuantity), "Units", 45, rightAlign: true);
            MoneyColumn(nameof(SaleViewDto.Subtotal), "Subtotal", 75);
            MoneyColumn(nameof(SaleViewDto.Discount), "Discount", 70);
            MoneyColumn(nameof(SaleViewDto.TotalAmount), "Total", 80);
            MoneyColumn(nameof(SaleViewDto.AmountPaid), "Paid", 70);
            MoneyColumn(nameof(SaleViewDto.Balance), "Balance", 70);
            StatusColumn(nameof(SaleViewDto.PaymentStatus), "Payment", 90);
            StatusColumn(nameof(SaleViewDto.Status), "Status", 80);
        }

        protected override bool Matches(SaleViewDto s, string term) =>
            s.SaleId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (s.MemberName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (s.CashierName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(SaleViewDto s) =>
            $"sale #{s.SaleId} for {s.MemberName} ({UiKit.Money(s.TotalAmount)})";

        protected override async Task<string?> OnDeleteAsync(SaleViewDto s)
        {
            var result = await Session.Sales.DeleteAsync(s.SaleId);
            return result.IsSuccess ? null : result.ErrorMessage;
        }

        private async Task CancelAsync()
        {
            if (IsBusy) return;

            var s = Selected;
            if (s is null) { ShowError("Select a sale to cancel."); return; }

            if (string.Equals(s.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                ShowError($"Sale #{s.SaleId} has already been cancelled.");
                return;
            }

            var fields = new List<FieldSpec>
            {
                new("reason", "Reason")
                {
                    Required = true,
                    MaxLength = 300,
                    Hint = "Recorded on the sale, e.g. “customer changed their mind”."
                }
            };

            var cancelled = EditDialog.Run(this, $"Cancel sale #{s.SaleId}",
                $"{UiKit.Money(s.TotalAmount)} for {s.MemberName}. Cancelling returns every item " +
                "to stock inside one transaction; the sale stays on record as cancelled.",
                fields, async f =>
                {
                    var result = await Session.Sales.CancelAsync(s.SaleId,
                        new CancelSaleDto { Reason = f.First(x => x.Key == "reason").Text });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Cancel sale");

            if (!cancelled) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify($"Sale #{s.SaleId} cancelled successfully and its stock returned.");
            }, "Refreshing…");
        }

        private async Task ShowReceiptAsync()
        {
            if (IsBusy) return;

            var s = Selected;
            if (s is null) { ShowError("Select a sale to view its receipt."); return; }

            await GuardAsync(async () =>
            {
                var detail = Unwrap(await Session.Sales.GetDetailAsync(s.SaleId));
                if (detail is null) return;

                ReceiptDialog.Show(this, ReceiptBuilder.ForSale(
                    Session, detail.Sale, detail.Lines, detail.Payments));
            }, "Loading the receipt…");
        }

        private async Task ShowLinesAsync()
        {
            if (IsBusy) return;

            var s = Selected;
            if (s is null) { ShowError("Select a sale to view its lines."); return; }

            await GuardAsync(async () =>
            {
                var lines = Unwrap(await Session.Sales.GetItemsAsync(s.SaleId));
                if (lines is null) return;

                using var dialog = new ListDialog($"Sale #{s.SaleId} — {s.MemberName}", lines, grid =>
                {
                    grid.Columns.Add(new DataGridViewTextBoxColumn
                    { DataPropertyName = nameof(SaleLineDto.ProductCode), HeaderText = "SKU", FillWeight = 60 });
                    grid.Columns.Add(new DataGridViewTextBoxColumn
                    { DataPropertyName = nameof(SaleLineDto.ProductName), HeaderText = "Product", FillWeight = 170 });
                    grid.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        DataPropertyName = nameof(SaleLineDto.Quantity), HeaderText = "Qty", FillWeight = 50,
                        DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
                    });
                    grid.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        DataPropertyName = nameof(SaleLineDto.UnitPrice), HeaderText = "Unit price", FillWeight = 70,
                        DefaultCellStyle = { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight }
                    });
                    grid.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        DataPropertyName = nameof(SaleLineDto.Subtotal), HeaderText = "Line total", FillWeight = 70,
                        DefaultCellStyle = { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight }
                    });
                },
                $"   Sale total {UiKit.Money(s.TotalAmount)}      Paid {UiKit.Money(s.AmountPaid)}      " +
                $"Balance {UiKit.Money(s.Balance)}");

                dialog.ShowDialog(this);
            }, "Loading sale lines…");
        }
    }
}
