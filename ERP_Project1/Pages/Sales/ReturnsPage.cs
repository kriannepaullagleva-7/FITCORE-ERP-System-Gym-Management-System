using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// Returns and refunds: goods coming back over the counter.
    ///
    /// A return is its own transaction rather than an edit to the sale it came from. The
    /// original stays exactly as it was rung up - which is what a receipt, a report and an
    /// audit all depend on - and the return records separately that some of it came back, what
    /// was refunded, and whether the goods went back on the shelf.
    /// </summary>
    internal sealed class ReturnsPage : CrudPageBase<SaleReturnDto>
    {
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;

        private readonly Label _count;
        private readonly Label _countHint;
        private readonly Label _refunded;
        private readonly Label _refundedHint;
        private readonly Label _restocked;
        private readonly Label _restockedHint;
        private readonly Label _written;
        private readonly Label _writtenHint;

        private List<string> _reasons = new();

        public ReturnsPage(FitCoreSession session)
            : base(session, "Returns & Refunds",
                   "Goods coming back, the money returned, and whether the stock went back on the shelf.",
                   "return", "Return number, member or reason")
        {
            var today = DateTime.Today;

            _from = UiKit.DatePicker(new DateTime(today.Year, today.Month, 1).AddMonths(-2));
            _to = UiKit.DatePicker(today);

            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            FilterBar.Controls.Add(_from);
            FilterBar.Controls.Add(UiKit.FilterLabel("To"));
            FilterBar.Controls.Add(_to);

            AddAction("Lines", ButtonTone.Secondary, ShowLinesAsync, 80);
            AddAction("Cancel", ButtonTone.Warning, CancelReturnAsync, 86);

            StatsRow.Controls.Add(UiKit.StatCard("Returns", out _count, out _countHint, UiTheme.Primary));
            StatsRow.Controls.Add(UiKit.StatCard("Refunded", out _refunded, out _refundedHint, UiTheme.Warning));
            StatsRow.Controls.Add(UiKit.StatCard("Back on the shelf", out _restocked, out _restockedHint, UiTheme.Success));
            StatsRow.Controls.Add(UiKit.StatCard("Written off", out _written, out _writtenHint, UiTheme.Danger));
        }

        protected override bool SupportsEdit => false;
        protected override bool SupportsDelete => false;

        protected override string CreatedMessage => "Return recorded.";

        protected override string EmptyHeadline => "No returns in this period";
        protected override string EmptyDetail =>
            "When a customer brings something back, record it here. The original sale is left " +
            "exactly as it was - the return is a transaction of its own.";

        protected override async Task<List<SaleReturnDto>?> FetchAsync()
        {
            if (_reasons.Count == 0)
            {
                _reasons = Unwrap(await Session.Returns.GetReasonsAsync()) ?? new List<string>();
            }

            var returns = Unwrap(await Session.Returns.GetAllAsync(_from.Value.Date, _to.Value.Date));

            if (returns is not null)
            {
                var live = returns.Where(r => r.Status == "Completed").ToList();

                _count.Text = live.Count.ToString("N0");
                _countHint.Text = $"{live.Sum(r => r.ItemCount):N0} line(s)";

                _refunded.Text = live.Sum(r => r.RefundAmount).ToString("N2");
                _refundedHint.Text = "given back to customers";

                var restocked = live.Where(r => r.RestockedToInventory).ToList();
                _restocked.Text = restocked.Sum(r => r.Subtotal).ToString("N2");
                _restockedHint.Text = $"{UiKit.Plural(restocked.Count, "return")} restocked";

                var lost = live.Where(r => !r.RestockedToInventory).ToList();
                _written.Text = lost.Sum(r => r.Subtotal).ToString("N2");
                _written.ForeColor = lost.Count > 0 ? UiTheme.Danger : UiTheme.TextPrimary;
                _writtenHint.Text = lost.Count == 0 ? "nothing damaged" : "damaged or expired";
            }

            return returns;
        }

        protected override void DefineColumns()
        {
            Column(nameof(SaleReturnDto.ReturnNo), "Return", 80);
            DateColumn(nameof(SaleReturnDto.ReturnDate), "Date", 75);
            Column(nameof(SaleReturnDto.SaleId), "Sale", 50, rightAlign: true);
            Column(nameof(SaleReturnDto.MemberName), "Member", 150);
            Column(nameof(SaleReturnDto.Reason), "Reason", 100);
            Column(nameof(SaleReturnDto.ItemCount), "Lines", 45, rightAlign: true);
            MoneyColumn(nameof(SaleReturnDto.Subtotal), "Goods", 80);
            MoneyColumn(nameof(SaleReturnDto.RefundAmount), "Refunded", 85);
            StatusColumn(nameof(SaleReturnDto.Status), "Status", 70);
            Column(nameof(SaleReturnDto.ProcessedBy), "By", 90);
        }

        protected override bool Matches(SaleReturnDto r, string term) =>
            r.ReturnNo.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            r.MemberName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            r.Reason.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            r.SaleId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Records a return against a sale.
        ///
        /// The operator names the sale first and the server answers with what is still
        /// returnable on it, so they are offered what can come back rather than discovering
        /// the limit when the server refuses them.
        /// </summary>
        protected override async Task<bool> OnAddAsync()
        {
            var saleId = 0;

            var found = EditDialog.Run(this, "Record a return",
                "Start with the sale the goods came from.",
                new List<FieldSpec>
                {
                    new("sale", "Sale number", FieldKind.Integer)
                        { Required = true, Minimum = 1m, Hint = "From the customer's receipt." }
                },
                f =>
                {
                    saleId = f.First(x => x.Key == "sale").Int;
                    return Task.FromResult<string?>(null);
                }, "Find sale");

            if (!found || saleId <= 0) return false;

            List<ReturnableLineDto>? lines = null;

            await GuardAsync(async () =>
            {
                lines = Unwrap(await Session.Returns.GetReturnableAsync(saleId));
            }, "Looking up the sale…");

            if (lines is null) return false;

            var returnable = lines.Where(l => l.QuantityReturnable > 0).ToList();

            if (returnable.Count == 0)
            {
                ShowError(lines.Count == 0
                    ? $"Sale #{saleId} has no lines on it, or does not exist."
                    : $"Everything on sale #{saleId} has already been returned.");

                return false;
            }

            var fields = new List<FieldSpec>
            {
                new("line", "Item", FieldKind.Combo)
                {
                    Required = true,
                    Options = returnable
                        .Select(l => new KeyValuePair<string, string>(
                            l.SaleItemId.ToString(),
                            $"{l.ProductName} — {l.QuantityReturnable} of {l.QuantitySold} left"))
                        .ToList()
                },
                new("quantity", "Quantity coming back", FieldKind.Integer)
                {
                    Required = true, Minimum = 1m, Value = 1,
                    Hint = "Cannot be more than is left on the line."
                },
                new("reason", "Reason", FieldKind.Combo)
                {
                    Required = true,
                    Value = _reasons.FirstOrDefault() ?? "Other",
                    Options = _reasons.Select(r => new KeyValuePair<string, string>(r, r)).ToList()
                },
                new("restock", "Put the goods back on the shelf", FieldKind.Check)
                {
                    Value = true,
                    Hint = "Ignored for a damaged or expired return - that stock cannot be sold."
                },
                new("refund", "Refund amount", FieldKind.Money)
                {
                    Minimum = 0m,
                    Hint = "Leave at zero to refund the full value of what came back."
                },
                new("method", "Refunded by", FieldKind.Combo)
                {
                    Value = "Cash",
                    Options = new List<KeyValuePair<string, string>>
                    {
                        new("Cash", "Cash"), new("Card", "Card"), new("Transfer", "Transfer"),
                        new("GCash", "GCash")
                    }
                },
                new("notes", "Notes", FieldKind.Multiline) { MaxLength = 300 }
            };

            return EditDialog.Run(this, $"Return against sale #{saleId}",
                "The sale itself is not changed. Stock and the ledger are corrected by this " +
                "return, so both transactions stay on record.",
                fields, async f =>
                {
                    var lineId = int.Parse(f.First(x => x.Key == "line").ComboValue!);
                    var quantity = f.First(x => x.Key == "quantity").Int;

                    var line = returnable.First(l => l.SaleItemId == lineId);

                    if (quantity > line.QuantityReturnable)
                    {
                        return $"Only {line.QuantityReturnable} of {line.ProductName} can still " +
                               "be returned on this sale.";
                    }

                    var refund = f.First(x => x.Key == "refund").Decimal;

                    var result = await Session.Returns.CreateAsync(new CreateSaleReturnDto
                    {
                        SaleId = saleId,
                        Reason = f.First(x => x.Key == "reason").Text,
                        RestockToInventory = f.First(x => x.Key == "restock").Flag,
                        RefundAmount = refund > 0m ? refund : null,
                        RefundMethod = f.First(x => x.Key == "method").Text,
                        Notes = f.First(x => x.Key == "notes").Text,
                        Lines = new List<ReturnLineDto>
                        {
                            new() { SaleItemId = lineId, Quantity = quantity }
                        }
                    });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Record return");
        }

        private async Task ShowLinesAsync()
        {
            var saleReturn = Selected;

            if (saleReturn is null)
            {
                ShowError("Select a return to see its lines.");
                return;
            }

            await GuardAsync(async () =>
            {
                var detail = Unwrap(await Session.Returns.GetByIdAsync(saleReturn.SaleReturnId));
                if (detail is null) return;

                ListDialog.Show(this,
                    $"{detail.ReturnNo} · against sale #{detail.SaleId}",
                    $"{detail.Reason} · refunded {detail.RefundAmount:N2} by {detail.RefundMethod} · " +
                    (detail.RestockedToInventory ? "restocked" : "written off"),
                    detail.Items.Select(i => new
                    {
                        Product = $"{i.ProductCode} {i.ProductName}",
                        i.Quantity,
                        UnitPrice = i.UnitPrice.ToString("N2"),
                        LineTotal = i.LineTotal.ToString("N2")
                    }).ToList());
            }, "Loading…");
        }

        private async Task CancelReturnAsync()
        {
            var saleReturn = Selected;

            if (saleReturn is null)
            {
                ShowError("Select the return to cancel.");
                return;
            }

            if (saleReturn.Status != "Completed")
            {
                ShowError($"{saleReturn.ReturnNo} has already been cancelled.");
                return;
            }

            var cancelled = EditDialog.Run(this, $"Cancel {saleReturn.ReturnNo}",
                "Any stock this return put back on the shelf is taken off again, and its " +
                "ledger posting is reversed. Both entries stay visible.",
                new List<FieldSpec>
                {
                    new("reason", "Why is this being cancelled?", FieldKind.Multiline)
                        { Required = true, MaxLength = 300 }
                },
                async f =>
                {
                    var result = await Session.Returns.CancelAsync(
                        saleReturn.SaleReturnId, f.First(x => x.Key == "reason").Text);

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Cancel return");

            if (!cancelled) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify($"{saleReturn.ReturnNo} cancelled.");
            }, "Refreshing…");
        }
    }
}
