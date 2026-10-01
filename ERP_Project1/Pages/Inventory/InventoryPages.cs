using ERP_Project1.Api;

namespace ERP_Project1
{
    // --------------------------------------------------------------------------- Products

    internal sealed class ProductsPage : CrudPageBase<ProductDto>
    {
        private List<string> _categories = new();

        public ProductsPage(FitCoreSession session)
            : base(session, "Products", "What the gym sells, with cost and selling price.",
                   "product", "Name or SKU")
        { }

        protected override string DeleteConsequence =>
            "A product that has been sold cannot be deleted, because that would change past " +
            "sales. If you have simply stopped stocking it, edit it and clear “Available to " +
            "sell” instead.";

        protected override string EmptyHeadline => "No products yet";
        protected override string EmptyDetail =>
            "Add the drinks, supplements and gear you sell at the counter. Creating a product " +
            "also opens its stock record.";

        protected override async Task<List<ProductDto>?> FetchAsync()
        {
            _categories = Unwrap(await Session.Products.GetCategoriesAsync()) ?? new();
            return Unwrap(await Session.Products.GetAllAsync());
        }

        protected override void DefineColumns()
        {
            Column(nameof(ProductDto.ProductCode), "SKU", 70);
            Column(nameof(ProductDto.ProductName), "Product", 170);
            Column(nameof(ProductDto.Category), "Category", 90);
            MoneyColumn(nameof(ProductDto.CostPrice), "Cost", 70);
            MoneyColumn(nameof(ProductDto.UnitPrice), "Price", 70);
            MoneyColumn(nameof(ProductDto.Margin), "Margin", 70);
            FlagColumn(nameof(ProductDto.IsActive), "Status", 70);
        }

        protected override bool Matches(ProductDto p, string term) =>
            p.ProductName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            p.ProductCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (p.Category ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(ProductDto p) =>
            $"“{p.ProductName}” ({p.ProductCode})";

        private List<FieldSpec> Fields(ProductDto? p)
        {
            var options = (_categories.Count > 0 ? _categories : new List<string> { "Other" })
                .Select(c => new KeyValuePair<string, string>(c, c))
                .ToList();

            // An existing product may sit in a category the catalogue no longer returns.
            if (p is not null && !string.IsNullOrWhiteSpace(p.Category) &&
                options.All(o => !string.Equals(o.Key, p.Category, StringComparison.OrdinalIgnoreCase)))
            {
                options.Insert(0, new KeyValuePair<string, string>(p.Category, p.Category));
            }

            return new List<FieldSpec>
            {
                new("code", "SKU / product code")
                {
                    Required = true, Value = p?.ProductCode, MaxLength = 50,
                    Hint = "Must be unique — the database enforces it too."
                },
                new("name", "Product name") { Required = true, Value = p?.ProductName, MaxLength = 150 },
                new("category", "Category", FieldKind.Combo)
                    { Value = p?.Category ?? "Other", Options = options },
                new("cost", "Cost price", FieldKind.Money)
                    { Value = p?.CostPrice ?? 0m, Minimum = 0m, Hint = "What you pay for it." },
                new("price", "Selling price", FieldKind.Money)
                    { Required = true, Value = p?.UnitPrice ?? 0m, Minimum = 0m }
            };
        }

        protected override Task<bool> OnAddAsync()
        {
            var fields = Fields(null);

            fields.Add(new FieldSpec("opening", "Opening stock", FieldKind.Number)
                { Value = 0m, Minimum = 0m, Hint = "Recorded as an opening stock movement." });

            fields.Add(new FieldSpec("reorder", "Reorder level", FieldKind.Number)
                { Value = 0m, Minimum = 0m, Hint = "Stock at or below this counts as low." });

            var saved = EditDialog.Run(this, "Add product",
                "Creating a product also opens its stock record.", fields, async f =>
                {
                    var result = await Session.Products.CreateAsync(new CreateProductDto
                    {
                        ProductCode = f.First(x => x.Key == "code").Text,
                        ProductName = f.First(x => x.Key == "name").Text,
                        Category = f.First(x => x.Key == "category").ComboValue ?? "Other",
                        CostPrice = f.First(x => x.Key == "cost").Decimal,
                        UnitPrice = f.First(x => x.Key == "price").Decimal,
                        OpeningStock = f.First(x => x.Key == "opening").Decimal,
                        ReorderLevel = f.First(x => x.Key == "reorder").Decimal
                    });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Create product");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(ProductDto p)
        {
            var fields = Fields(p);
            fields.Add(new FieldSpec("active", "Available to sell", FieldKind.Check) { Value = p.IsActive });

            var saved = EditDialog.Run(this, $"Edit {p.ProductName}",
                "Stock is changed from the Stock tab, never here.", fields, async f =>
                {
                    var result = await Session.Products.UpdateAsync(p.ProductId, new UpdateProductDto
                    {
                        ProductCode = f.First(x => x.Key == "code").Text,
                        ProductName = f.First(x => x.Key == "name").Text,
                        Category = f.First(x => x.Key == "category").ComboValue ?? "Other",
                        CostPrice = f.First(x => x.Key == "cost").Decimal,
                        UnitPrice = f.First(x => x.Key == "price").Decimal,
                        IsActive = f.First(x => x.Key == "active").Flag
                    });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Save changes");

            return Task.FromResult(saved);
        }

        protected override async Task<string?> OnDeleteAsync(ProductDto p)
        {
            var result = await Session.Products.DeleteAsync(p.ProductId);
            return result.IsSuccess ? null : result.ErrorMessage;
        }
    }

    // -------------------------------------------------------------------------- Inventory

    /// <summary>
    /// Stock on hand. Quantity is never edited directly: every change goes through stock-in,
    /// stock-out or an adjustment on the server, each of which writes a ledger row recording
    /// the balance before and after.
    /// </summary>
    internal sealed class InventoryPage : CrudPageBase<InventoryDto>
    {
        private static readonly string[] StockOutReasons = { "SALE", "DAMAGED", "USED", "REMOVED", "ADJUSTMENT" };

        private readonly Label _summary;
        private readonly ComboBox _statusFilter;
        private List<SupplierDto> _suppliers = new();

        public InventoryPage(FitCoreSession session)
            : base(session, "Stock", "Stock levels, reorder points and the movement ledger.",
                   "product", "Product or SKU")
        {
            AddAction("Ledger", ButtonTone.Secondary, () => ShowLedgerAsync(), 90);
            AddAction("Reorder level", ButtonTone.Secondary, () => SetReorderAsync(), 118);
            AddAction("Adjust", ButtonTone.Secondary, () => MoveAsync("adjust"), 86);
            AddAction("Stock out", ButtonTone.Warning, () => MoveAsync("out"), 100);
            AddAction("Stock in", ButtonTone.Success, () => MoveAsync("in"), 98);

            FilterBar.Controls.Add(UiKit.FilterLabel("Status"));

            _statusFilter = UiKit.Select(150);
            _statusFilter.DisplayMember = "Value";
            _statusFilter.ValueMember = "Key";
            _statusFilter.DataSource = new List<KeyValuePair<string, string>>
            {
                new("", "All stock"),
                new("low", "Low stock"),
                new("out", "Out of stock"),
                new("in", "In stock")
            };
            _statusFilter.SelectedIndexChanged += (_, _) => ApplyStatusFilter();
            FilterBar.Controls.Add(_statusFilter);

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

        protected override string EmptyHeadline => "No stock records yet";
        protected override string EmptyDetail =>
            "Every product gets a stock record when it is created. Add a product on the " +
            "Products tab and it will appear here.";

        protected override async Task<List<InventoryDto>?> FetchAsync()
        {
            _suppliers = Unwrap(await Session.Suppliers.GetAllAsync()) ?? new();
            return Unwrap(await Session.Inventory.GetAllAsync());
        }

        protected override void DefineColumns()
        {
            Column(nameof(InventoryDto.ProductCode), "SKU", 70);
            Column(nameof(InventoryDto.ProductName), "Product", 170);
            Column(nameof(InventoryDto.Category), "Category", 90);
            Column(nameof(InventoryDto.QuantityOnHand), "On hand", 70, "N2", rightAlign: true);
            Column(nameof(InventoryDto.ReorderLevel), "Reorder at", 70, "N2", rightAlign: true);
            StatusColumn(nameof(InventoryDto.StockStatus), "Status", 90);
            MoneyColumn(nameof(InventoryDto.StockValue), "Stock value", 85);
        }

        protected override bool Matches(InventoryDto i, string term) =>
            i.ProductName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            i.ProductCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (i.Category ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The status filter narrows the list before the search does, so "low stock" plus a
        /// search term means both, which is what an operator expects.
        /// </summary>
        protected override void AfterLoad()
        {
            _ = RefreshSummaryAsync();
        }

        private async Task RefreshSummaryAsync()
        {
            var summary = Unwrap(await Session.Inventory.GetSummaryAsync());
            if (summary is null || IsDisposed) return;

            _summary.Text =
                $"{summary.TotalProducts:N0} product(s)      {summary.InStock:N0} in stock      " +
                $"{summary.LowStock:N0} low      {summary.OutOfStock:N0} out      " +
                $"value {UiKit.Money(summary.StockValue)}";
        }

        private string Filter => _statusFilter.SelectedValue?.ToString() ?? "";

        private bool PassesStatus(InventoryDto i) => Filter switch
        {
            "low" => i.QuantityOnHand > 0 && i.QuantityOnHand <= i.ReorderLevel,
            "out" => i.QuantityOnHand <= 0,
            "in" => i.QuantityOnHand > i.ReorderLevel,
            _ => true
        };

        protected override async Task<string?> OnDeleteAsync(InventoryDto i) =>
            await Task.FromResult("Stock records are removed with their product.");

        // The base class filters on the search term; the status filter is layered on by
        // narrowing Items before it runs.
        private List<InventoryDto> _all = new();

        public override async Task LoadAsync()
        {
            await base.LoadAsync();
            _all = Items.ToList();
            ApplyStatusFilter();
        }

        private void ApplyStatusFilter()
        {
            Items = _all.Where(PassesStatus).ToList();
            ApplyFilter();
        }

        private async Task SetReorderAsync()
        {
            if (IsBusy) return;

            var row = Selected;
            if (row is null) { ShowError("Select a product to set its reorder level."); return; }

            var fields = new List<FieldSpec>
            {
                new("level", "Reorder level", FieldKind.Number)
                {
                    Required = true,
                    Value = row.ReorderLevel,
                    Minimum = 0m,
                    Hint = $"{row.ProductName} currently has {UiKit.Money(row.QuantityOnHand)} on hand. " +
                           "Stock at or below the reorder level is flagged as low."
                }
            };

            var saved = EditDialog.Run(this, $"Reorder level — {row.ProductName}", "", fields, async f =>
            {
                var result = await Session.Inventory.SetReorderLevelAsync(row.ProductId,
                    new ReorderLevelDto { ReorderLevel = f.First(x => x.Key == "level").Decimal });
                return result.IsSuccess ? null : result.ErrorMessage;
            }, "Save");

            if (!saved) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify("Reorder level updated successfully.");
            }, "Refreshing…");
        }

        private async Task MoveAsync(string kind)
        {
            if (IsBusy) return;

            var row = Selected;
            if (row is null)
            {
                ShowError(kind switch
                {
                    "in" => "Select a product to receive stock into.",
                    "out" => "Select a product to take stock out of.",
                    _ => "Select a product to adjust."
                });
                return;
            }

            var isAdjust = kind == "adjust";
            var isIn = kind == "in";

            if (isIn && _suppliers.Count(s => s.IsActive) == 0)
            {
                ShowError("There are no active suppliers. Add one on the Suppliers tab before receiving stock.");
                return;
            }

            var fields = new List<FieldSpec>
            {
                new("current", "Current stock on hand")
                {
                    Value = UiKit.Money(row.QuantityOnHand),
                    Hint = $"{row.ProductName} ({row.ProductCode})"
                },

                new("qty", isAdjust ? "Counted quantity" : "Quantity", FieldKind.Number)
                {
                    Required = true,
                    Minimum = 0m,
                    Hint = isAdjust
                        ? "Enter what you actually counted. The difference is recorded."
                        : isIn
                            ? "How many units are arriving."
                            : "How many units are leaving."
                }
            };

            if (isIn)
            {
                fields.Add(new FieldSpec("supplier", "Supplier", FieldKind.Combo)
                {
                    Required = true,
                    Options = _suppliers.Where(s => s.IsActive)
                        .Select(s => new KeyValuePair<string, string>(
                            s.SupplierId.ToString(), $"{s.SupplierName} ({s.SupplierCode})"))
                        .ToList(),
                    Hint = "Who supplied the product. Required for every stock-in."
                });

                fields.Add(new FieldSpec("ref", "Reference") { MaxLength = 100, Hint = "e.g. PO-1042" });
            }

            if (kind == "out")
            {
                fields.Add(new FieldSpec("reason", "Reason", FieldKind.Combo)
                {
                    Required = true,
                    Options = StockOutReasons.Select(r => new KeyValuePair<string, string>(r, r)).ToList()
                });
            }

            fields.Add(new FieldSpec("notes", isAdjust ? "Reason" : "Notes", FieldKind.Multiline) { MaxLength = 300 });

            // The current-stock field is a read-only reminder, not an input.
            fields[0].Validate = _ => null;

            var title = kind switch
            {
                "in" => $"Stock in — {row.ProductName}",
                "out" => $"Stock out — {row.ProductName}",
                _ => $"Stock count — {row.ProductName}"
            };

            var moved = EditDialog.Run(this, title,
                "The server writes a ledger row recording the balance before and after.",
                fields, async f =>
                {
                    var qty = f.First(x => x.Key == "qty").Decimal;

                    if (!isAdjust && qty <= 0) return "Please enter a quantity greater than zero.";

                    // Stock is never reduced below zero. The server enforces this too; checking
                    // here saves a round trip and gives a clearer message.
                    if (kind == "out" && qty > row.QuantityOnHand)
                    {
                        return $"Insufficient stock for {row.ProductName}. " +
                               $"There are only {UiKit.Money(row.QuantityOnHand)} units on hand.";
                    }

                    var notes = f.First(x => x.Key == "notes").Text;

                    ApiResult<InventoryDto> result = kind switch
                    {
                        "in" => await Session.Inventory.StockInAsync(row.ProductId, new StockInRequestDto
                        {
                            Quantity = qty,
                            SupplierId = int.Parse(f.First(x => x.Key == "supplier").ComboValue ?? "0"),
                            Reference = f.First(x => x.Key == "ref").Text,
                            Notes = notes
                        }),
                        "out" => await Session.Inventory.StockOutAsync(row.ProductId, new StockOutRequestDto
                        {
                            Quantity = qty,
                            Reason = f.First(x => x.Key == "reason").ComboValue ?? "",
                            Notes = notes
                        }),
                        _ => await Session.Inventory.AdjustAsync(row.ProductId,
                            new StockAdjustmentDto { NewQuantity = qty, Notes = notes })
                    };

                    return result.IsSuccess ? null : result.ErrorMessage;
                },
                kind switch { "in" => "Receive stock", "out" => "Remove stock", _ => "Apply count" });

            if (!moved) return;

            await GuardAsync(async () =>
            {
                var before = row.QuantityOnHand;
                await LoadAsync();

                var after = Items.FirstOrDefault(i => i.ProductId == row.ProductId)?.QuantityOnHand ?? before;

                Notify(kind switch
                {
                    "in" => $"Stock-in completed successfully — {row.ProductName} is now " +
                            $"{UiKit.Money(after)} on hand.",
                    "out" => $"Stock-out completed successfully — {row.ProductName} is now " +
                             $"{UiKit.Money(after)} on hand.",
                    _ => $"Stock count applied successfully — {row.ProductName} is now " +
                         $"{UiKit.Money(after)} on hand."
                });
            }, "Refreshing stock…");
        }

        private async Task ShowLedgerAsync()
        {
            if (IsBusy) return;

            var row = Selected;

            await GuardAsync(async () =>
            {
                var movements = Unwrap(await Session.Inventory.GetMovementsAsync(row?.ProductId, 300));
                if (movements is null) return;

                if (movements.Count == 0)
                {
                    ShowError(row is null
                        ? "No stock movements have been recorded yet."
                        : $"No stock movements have been recorded for {row.ProductName} yet.");
                    return;
                }

                using var dialog = new ListDialog(
                    row is null ? "Stock movement ledger" : $"Ledger — {row.ProductName}",
                    movements,
                    grid =>
                    {
                        grid.Columns.Add(new DataGridViewTextBoxColumn
                        {
                            DataPropertyName = nameof(StockMovementDto.MovementDate), HeaderText = "When",
                            FillWeight = 90, DefaultCellStyle = { Format = "d MMM yyyy HH:mm" }
                        });
                        grid.Columns.Add(new DataGridViewTextBoxColumn
                        { DataPropertyName = nameof(StockMovementDto.ProductCode), HeaderText = "SKU", FillWeight = 58 });
                        grid.Columns.Add(new DataGridViewTextBoxColumn
                        {
                            Name = nameof(StockMovementDto.MovementType),
                            DataPropertyName = nameof(StockMovementDto.MovementType),
                            HeaderText = "Type", FillWeight = 62
                        });
                        grid.Columns.Add(new DataGridViewTextBoxColumn
                        {
                            DataPropertyName = nameof(StockMovementDto.Quantity), HeaderText = "Qty", FillWeight = 48,
                            DefaultCellStyle = { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight }
                        });
                        grid.Columns.Add(new DataGridViewTextBoxColumn
                        {
                            DataPropertyName = nameof(StockMovementDto.BalanceBefore), HeaderText = "Before",
                            FillWeight = 52,
                            DefaultCellStyle = { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight }
                        });
                        grid.Columns.Add(new DataGridViewTextBoxColumn
                        {
                            DataPropertyName = nameof(StockMovementDto.BalanceAfter), HeaderText = "After",
                            FillWeight = 52,
                            DefaultCellStyle = { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight }
                        });
                        grid.Columns.Add(new DataGridViewTextBoxColumn
                        { DataPropertyName = nameof(StockMovementDto.Reference), HeaderText = "Reference", FillWeight = 88 });
                        grid.Columns.Add(new DataGridViewTextBoxColumn
                        { DataPropertyName = nameof(StockMovementDto.Notes), HeaderText = "Notes", FillWeight = 120 });
                    },
                    $"   {movements.Count:N0} movement(s), most recent first");

                dialog.ShowDialog(this);
            }, "Loading the ledger…");
        }
    }
}
