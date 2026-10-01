using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Purchasing: ordering stock, receiving it, and paying for it.
    ///
    /// This is the half of Inventory that makes it financially complete. Receiving is the
    /// event that matters - it puts stock on the shelf at what it actually cost, folds that
    /// cost into the weighted average every later sale is charged against, and raises what the
    /// supplier is owed. Nothing is expensed: goods bought for resale are an asset until they
    /// are sold.
    /// </summary>
    internal sealed class PurchasesPage : CrudPageBase<PurchaseDto>
    {
        private readonly ComboBox _status;
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;

        private readonly Label _awaiting;
        private readonly Label _awaitingHint;
        private readonly Label _received;
        private readonly Label _receivedHint;
        private readonly Label _payable;
        private readonly Label _payableHint;
        private readonly Label _ordered;
        private readonly Label _orderedHint;

        private List<SupplierDto> _suppliers = new();
        private List<ProductDto> _products = new();

        public PurchasesPage(FitCoreSession session)
            : base(session, "Purchases",
                   "Stock ordered from suppliers, what has arrived, and what is still owed for it.",
                   "purchase", "Order number, supplier or reference")
        {
            var today = DateTime.Today;

            _from = UiKit.DatePicker(new DateTime(today.Year, today.Month, 1).AddMonths(-3));
            _to = UiKit.DatePicker(today);

            _status = UiKit.Select(160);
            _status.Items.Add("All statuses");
            _status.Items.AddRange(PurchaseStatuses.All.Cast<object>().ToArray());
            _status.SelectedIndex = 0;
            _status.SelectedIndexChanged += async (_, _) => await LoadAsync();

            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            FilterBar.Controls.Add(_from);
            FilterBar.Controls.Add(UiKit.FilterLabel("To"));
            FilterBar.Controls.Add(_to);
            FilterBar.Controls.Add(UiKit.FilterLabel("Status"));
            FilterBar.Controls.Add(_status);

            AddAction("Lines", ButtonTone.Secondary, ShowLinesAsync, 80);
            AddAction("Place order", ButtonTone.Secondary, PlaceOrderAsync, 106);
            AddAction("Receive", ButtonTone.Success, ReceiveAsync, 92);
            AddAction("Pay", ButtonTone.Primary, PayAsync, 74);

            StatsRow.Controls.Add(UiKit.StatCard("Awaiting delivery", out _awaiting, out _awaitingHint, UiTheme.Warning));
            StatsRow.Controls.Add(UiKit.StatCard("Received", out _received, out _receivedHint, UiTheme.Success));
            StatsRow.Controls.Add(UiKit.StatCard("Owed to suppliers", out _payable, out _payableHint, UiTheme.Danger));
            StatsRow.Controls.Add(UiKit.StatCard("Ordered in total", out _ordered, out _orderedHint, UiTheme.Primary));
        }

        protected override string DeleteConsequence =>
            "The draft order is removed. Only a draft can be deleted - once an order has been " +
            "placed it is part of the purchase history and is cancelled instead.";

        protected override string EmptyHeadline => "No purchases yet";
        protected override string EmptyDetail =>
            "Raise an order for stock you are buying. Receiving it puts the goods on the shelf " +
            "at what they cost and records what the supplier is owed.";

        protected override async Task<List<PurchaseDto>?> FetchAsync()
        {
            if (_suppliers.Count == 0)
            {
                _suppliers = Unwrap(await Session.Suppliers.GetAllAsync()) ?? new List<SupplierDto>();
            }

            if (_products.Count == 0)
            {
                _products = Unwrap(await Session.Products.GetActiveAsync()) ?? new List<ProductDto>();
            }

            var summary = Unwrap(await Session.Purchases.GetSummaryAsync());

            if (summary is not null)
            {
                _awaiting.Text = summary.TotalOutstanding.ToString("N2");
                _awaitingHint.Text = $"{UiKit.Plural(summary.AwaitingDelivery, "order")} on the way";

                _received.Text = summary.TotalReceived.ToString("N2");
                _receivedHint.Text = "delivered in full";

                _payable.Text = summary.PayableBalance.ToString("N2");
                _payable.ForeColor = summary.PayableBalance > 0m ? UiTheme.Danger : UiTheme.TextPrimary;
                _payableHint.Text = "on goods already received";

                _ordered.Text = summary.TotalOrdered.ToString("N2");
                _orderedHint.Text = $"{UiKit.Plural(summary.Count, "order")} all time";
            }

            return Unwrap(await Session.Purchases.GetAllAsync(
                _from.Value.Date, _to.Value.Date,
                _status.SelectedIndex <= 0 ? null : (string)_status.SelectedItem!));
        }

        protected override void DefineColumns()
        {
            Column(nameof(PurchaseDto.PurchaseNo), "Order", 75);
            DateColumn(nameof(PurchaseDto.OrderDate), "Ordered", 75);
            Column(nameof(PurchaseDto.SupplierName), "Supplier", 150);
            Column(nameof(PurchaseDto.ItemCount), "Lines", 45, rightAlign: true);
            MoneyColumn(nameof(PurchaseDto.Total), "Total", 85);
            MoneyColumn(nameof(PurchaseDto.AmountPaid), "Paid", 80);
            MoneyColumn(nameof(PurchaseDto.Balance), "Balance", 85);
            StatusColumn(nameof(PurchaseDto.Status), "Status", 95);
            StatusColumn(nameof(PurchaseDto.PaymentStatus), "Payment", 85);
        }

        protected override bool Matches(PurchaseDto p, string term) =>
            p.PurchaseNo.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            p.SupplierName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            p.SupplierReference.Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(PurchaseDto p) =>
            $"draft order {p.PurchaseNo} for {p.SupplierName}";

        // ------------------------------------------------------------------ writing

        /// <summary>
        /// Raises an order with a single line.
        ///
        /// One line covers the common case - a case of protein, a box of shakers - and keeps
        /// the dialog to something usable. More lines are added by editing the draft, which is
        /// the rarer act and deserves the longer route.
        /// </summary>
        protected override Task<bool> OnAddAsync()
        {
            if (_suppliers.Count == 0 || _products.Count == 0)
            {
                ShowError(_suppliers.Count == 0
                    ? "Add a supplier before raising an order."
                    : "Add an active product before raising an order.");

                return Task.FromResult(false);
            }

            var saved = EditDialog.Run(this, "Raise a purchase order",
                "The order starts as a draft. Place it when it goes to the supplier, and " +
                "receive it when the goods arrive.",
                new List<FieldSpec>
                {
                    SupplierField(null),
                    new("orderDate", "Order date", FieldKind.Date)
                        { Required = true, Value = DateTime.Today },
                    new("expected", "Expected", FieldKind.Date) { Value = DateTime.Today.AddDays(7) },
                    new("reference", "Supplier reference")
                        { MaxLength = 60, Hint = "Their invoice or delivery note number." },

                    ProductField(),
                    new("quantity", "Quantity", FieldKind.Number)
                        { Required = true, Minimum = 0.01m, Value = 1m },
                    new("unitCost", "Unit cost", FieldKind.Money)
                    {
                        Minimum = 0m,
                        Hint = "What you are paying per unit. Left at zero, the catalogue cost " +
                               "is used - but a real cost here keeps stock valued correctly."
                    },

                    new("discount", "Discount", FieldKind.Money) { Minimum = 0m },
                    new("tax", "Tax", FieldKind.Money) { Minimum = 0m },
                    new("notes", "Notes", FieldKind.Multiline) { MaxLength = 300 }
                },
                async f =>
                {
                    var result = await Session.Purchases.CreateAsync(new CreatePurchaseDto
                    {
                        SupplierId = int.Parse(f.First(x => x.Key == "supplier").ComboValue!),
                        OrderDate = f.First(x => x.Key == "orderDate").Date,
                        ExpectedDate = f.First(x => x.Key == "expected").Date,
                        SupplierReference = f.First(x => x.Key == "reference").Text,
                        Discount = f.First(x => x.Key == "discount").Decimal,
                        Tax = f.First(x => x.Key == "tax").Decimal,
                        Notes = f.First(x => x.Key == "notes").Text,
                        Lines = new List<PurchaseLineDto>
                        {
                            new()
                            {
                                ProductId = int.Parse(f.First(x => x.Key == "product").ComboValue!),
                                Quantity = f.First(x => x.Key == "quantity").Decimal,
                                UnitCost = f.First(x => x.Key == "unitCost").Decimal
                            }
                        }
                    });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Raise order");

            return Task.FromResult(saved);
        }

        protected override async Task<bool> OnEditAsync(PurchaseDto purchase)
        {
            if (purchase.Status != PurchaseStatuses.Draft)
            {
                ShowError($"{purchase.PurchaseNo} is {purchase.Status.ToLowerInvariant()}. " +
                          "Only a draft can be edited - once it is placed, the supplier has it.");
                return false;
            }

            var detail = Unwrap(await Session.Purchases.GetByIdAsync(purchase.PurchaseId));
            if (detail is null) return false;

            var line = detail.Items.FirstOrDefault();

            var fields = new List<FieldSpec>
            {
                SupplierField(detail),
                new("orderDate", "Order date", FieldKind.Date)
                    { Required = true, Value = detail.OrderDate },
                new("expected", "Expected", FieldKind.Date)
                    { Value = detail.ExpectedDate ?? DateTime.Today },
                new("reference", "Supplier reference")
                    { Value = detail.SupplierReference, MaxLength = 60 },

                ProductField(line?.ProductId),
                new("quantity", "Quantity", FieldKind.Number)
                    { Required = true, Minimum = 0.01m, Value = line?.Quantity ?? 1m },
                new("unitCost", "Unit cost", FieldKind.Money)
                    { Minimum = 0m, Value = line?.UnitCost ?? 0m },

                new("discount", "Discount", FieldKind.Money)
                    { Minimum = 0m, Value = detail.Discount },
                new("tax", "Tax", FieldKind.Money) { Minimum = 0m, Value = detail.Tax },
                new("notes", "Notes", FieldKind.Multiline)
                    { Value = detail.Notes, MaxLength = 300 }
            };

            return EditDialog.Run(this, $"Edit {detail.PurchaseNo}",
                detail.Items.Count > 1
                    ? "This order has several lines. Saving here replaces them with the single " +
                      "line below."
                    : "",
                fields, async f =>
                {
                    var result = await Session.Purchases.UpdateAsync(
                        detail.PurchaseId, new UpdatePurchaseDto
                        {
                            SupplierId = int.Parse(f.First(x => x.Key == "supplier").ComboValue!),
                            OrderDate = f.First(x => x.Key == "orderDate").Date,
                            ExpectedDate = f.First(x => x.Key == "expected").Date,
                            SupplierReference = f.First(x => x.Key == "reference").Text,
                            Discount = f.First(x => x.Key == "discount").Decimal,
                            Tax = f.First(x => x.Key == "tax").Decimal,
                            Notes = f.First(x => x.Key == "notes").Text,
                            Lines = new List<PurchaseLineDto>
                            {
                                new()
                                {
                                    ProductId = int.Parse(f.First(x => x.Key == "product").ComboValue!),
                                    Quantity = f.First(x => x.Key == "quantity").Decimal,
                                    UnitCost = f.First(x => x.Key == "unitCost").Decimal
                                }
                            }
                        });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Save draft");
        }

        protected override async Task<string?> OnDeleteAsync(PurchaseDto purchase)
        {
            var result = await Session.Purchases.DeleteAsync(purchase.PurchaseId);
            return result.IsSuccess ? null : result.ErrorMessage;
        }

        private FieldSpec SupplierField(PurchaseDto? purchase) =>
            new("supplier", "Supplier", FieldKind.Combo)
            {
                Required = true,
                Value = purchase?.SupplierId.ToString(),
                Options = _suppliers
                    .Where(s => s.IsActive)
                    .Select(s => new KeyValuePair<string, string>(
                        s.SupplierId.ToString(), s.SupplierName))
                    .ToList()
            };

        private FieldSpec ProductField(int? productId = null) =>
            new("product", "Product", FieldKind.Combo)
            {
                Required = true,
                Value = productId?.ToString(),
                Options = _products
                    .Select(p => new KeyValuePair<string, string>(
                        p.ProductId.ToString(), $"{p.ProductCode} · {p.ProductName}"))
                    .ToList()
            };

        // ------------------------------------------------------------------ workflow

        private async Task PlaceOrderAsync()
        {
            var purchase = Selected;

            if (purchase is null)
            {
                ShowError("Select the draft order to place.");
                return;
            }

            await GuardAsync(async () =>
            {
                var result = await Session.Purchases.MarkOrderedAsync(purchase.PurchaseId);

                if (!result.IsSuccess)
                {
                    ShowError(result.ErrorMessage);
                    return;
                }

                await LoadAsync();
                Notify($"{purchase.PurchaseNo} placed with {purchase.SupplierName}.");
            }, "Placing order…");
        }

        private async Task ReceiveAsync()
        {
            var purchase = Selected;

            if (purchase is null)
            {
                ShowError("Select the order that has arrived.");
                return;
            }

            if (purchase.Status == PurchaseStatuses.Received)
            {
                ShowError($"{purchase.PurchaseNo} has already been received in full.");
                return;
            }

            if (UiKit.Confirm(
                    $"Receive everything still outstanding on {purchase.PurchaseNo}?\r\n\r\n" +
                    "The stock goes on the shelf at what it cost, each product's average cost " +
                    $"is updated, and {purchase.SupplierName} is recorded as owed for it.")
                != DialogResult.Yes)
            {
                return;
            }

            await GuardAsync(async () =>
            {
                var result = await Session.Purchases.ReceiveAsync(purchase.PurchaseId);

                if (!result.IsSuccess)
                {
                    ShowError(result.ErrorMessage);
                    return;
                }

                await LoadAsync();
                Notify($"{purchase.PurchaseNo} received. Stock and the ledger have been updated.");
            }, "Receiving…");
        }

        private async Task PayAsync()
        {
            var purchase = Selected;

            if (purchase is null)
            {
                ShowError("Select the order being paid for.");
                return;
            }

            if (purchase.Balance <= 0m)
            {
                ShowError($"{purchase.PurchaseNo} has nothing left to pay.");
                return;
            }

            var bankAccounts = Unwrap(
                await Session.Finance.GetBankAccountsAsync(includeInactive: false))
                ?? new List<BankAccountDto>();

            var accounts = bankAccounts
                .Select(a => new KeyValuePair<string, string>(
                    a.BankAccountId.ToString(), a.AccountName))
                .ToList();

            accounts.Insert(0, new KeyValuePair<string, string>("", "— not specified —"));

            var paid = EditDialog.Run(this, $"Pay {purchase.SupplierName}",
                $"{purchase.PurchaseNo} · {purchase.Balance:N2} outstanding.",
                new List<FieldSpec>
                {
                    new("amount", "Amount", FieldKind.Money)
                    {
                        Required = true,
                        Minimum = 0.01m,
                        Maximum = purchase.Balance,
                        Value = purchase.Balance,
                        Hint = "Cannot be more than the order still owes."
                    },
                    new("date", "Payment date", FieldKind.Date)
                        { Required = true, Value = DateTime.Today },
                    new("method", "Method", FieldKind.Combo)
                    {
                        Required = true,
                        Value = "Cash",
                        Options = new List<KeyValuePair<string, string>>
                        {
                            new("Cash", "Cash"), new("Card", "Card"), new("Transfer", "Transfer"),
                            new("Check", "Check"), new("GCash", "GCash")
                        }
                    },
                    new("bank", "From account", FieldKind.Combo) { Options = accounts },
                    new("reference", "Reference") { MaxLength = 60 },
                    new("notes", "Notes", FieldKind.Multiline) { MaxLength = 300 }
                },
                async f =>
                {
                    var result = await Session.Purchases.PaySupplierAsync(new PaySupplierDto
                    {
                        SupplierId = purchase.SupplierId,
                        PurchaseId = purchase.PurchaseId,
                        Amount = f.First(x => x.Key == "amount").Decimal,
                        PaymentDate = f.First(x => x.Key == "date").Date,
                        Method = f.First(x => x.Key == "method").Text,
                        ReferenceNo = f.First(x => x.Key == "reference").Text,
                        Notes = f.First(x => x.Key == "notes").Text,
                        BankAccountId = int.TryParse(
                            f.First(x => x.Key == "bank").ComboValue, out var id) ? id : null
                    });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Record payment");

            if (!paid) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify($"Payment to {purchase.SupplierName} recorded.");
            }, "Refreshing…");
        }

        private async Task ShowLinesAsync()
        {
            var purchase = Selected;

            if (purchase is null)
            {
                ShowError("Select an order to see its lines.");
                return;
            }

            await GuardAsync(async () =>
            {
                var detail = Unwrap(await Session.Purchases.GetByIdAsync(purchase.PurchaseId));
                if (detail is null) return;

                ListDialog.Show(this,
                    $"{detail.PurchaseNo} · {detail.SupplierName}",
                    $"Subtotal {detail.Subtotal:N2} · discount {detail.Discount:N2} · " +
                    $"tax {detail.Tax:N2} · total {detail.Total:N2}",
                    detail.Items.Select(i => new
                    {
                        Product = $"{i.ProductCode} {i.ProductName}",
                        Ordered = i.Quantity.ToString("N2"),
                        Received = i.QuantityReceived.ToString("N2"),
                        Outstanding = i.OutstandingQuantity.ToString("N2"),
                        UnitCost = i.UnitCost.ToString("N2"),
                        LineTotal = i.LineTotal.ToString("N2")
                    }).ToList());
            }, "Loading…");
        }
    }
}
