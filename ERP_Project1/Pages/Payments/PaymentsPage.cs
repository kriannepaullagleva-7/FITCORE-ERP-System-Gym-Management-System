using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    // --------------------------------------------------------------------------- Payments

    internal sealed class PaymentsPage : CrudPageBase<PaymentViewDto>
    {
        private static readonly List<KeyValuePair<string, string>> Methods = new()
        {
            new("Cash", "Cash"), new("Card", "Card"), new("Transfer", "Transfer"),
            new("Check", "Check"), new("GCash", "GCash")
        };

        private readonly Label _summary;
        private readonly ComboBox _statusFilter;
        private List<MemberDto> _members = new();
        private List<SaleViewDto> _sales = new();
        private List<PaymentViewDto> _all = new();

        /// <summary>What a new payment in this central transaction log is being recorded for.</summary>
        private enum PaymentFor { Membership, WalkIn, Sale, Other }

        public PaymentsPage(FitCoreSession session)
            : base(session, "Payment Transactions",
                   "Money received, what it settled, and what is still outstanding.",
                   "payment", "Member, reference or payment ID")
        {
            AddAction("View receipt", ButtonTone.Secondary, () => ShowReceiptAsync(), 116);
            AddAction("Void", ButtonTone.Secondary, () => VoidAsync(), 84);

            FilterBar.Controls.Add(UiKit.FilterLabel("Status"));

            _statusFilter = UiKit.Select(150);
            _statusFilter.DisplayMember = "Value";
            _statusFilter.ValueMember = "Key";
            _statusFilter.DataSource = new List<KeyValuePair<string, string>>
            {
                new("", "All payments"),
                new("Completed", "Completed"),
                new("Pending", "Pending"),
                new("Refunded", "Refunded"),
                new("Failed", "Failed")
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

        protected override bool SupportsEdit => true;

        protected override string CreatedMessage => "Payment recorded successfully.";

        protected override string DeleteConsequence =>
            "Deleting a payment removes it from every collection figure and reopens whatever " +
            "it settled. Void it instead if it was taken in error but should stay on record. " +
            "This cannot be undone.";

        protected override string EmptyHeadline => "No payments recorded yet";
        protected override string EmptyDetail =>
            "Record what members pay for memberships and purchases. Outstanding balances are " +
            "calculated from what has been received.";

        protected override async Task<List<PaymentViewDto>?> FetchAsync()
        {
            _members = Unwrap(await Session.Members.GetAllAsync()) ?? new();
            _sales = Unwrap(await Session.Sales.GetAllAsync()) ?? new();
            return Unwrap(await Session.Payments.GetAllAsync());
        }

        public override async Task LoadAsync()
        {
            await base.LoadAsync();
            _all = Items.ToList();
            ApplyStatusFilter();
        }

        private void ApplyStatusFilter()
        {
            var wanted = _statusFilter.SelectedValue?.ToString() ?? "";

            Items = string.IsNullOrWhiteSpace(wanted)
                ? _all.ToList()
                : _all.Where(p => string.Equals(p.Status, wanted, StringComparison.OrdinalIgnoreCase)).ToList();

            ApplyFilter();
        }

        protected override void DefineColumns()
        {
            Column(nameof(PaymentViewDto.PaymentId), "ID", 40, rightAlign: true);
            DateColumn(nameof(PaymentViewDto.PaymentDate), "Date", 85);
            StatusColumn(nameof(PaymentViewDto.Category), "Category", 85);
            Column(nameof(PaymentViewDto.MemberDisplay), "Member", 120);
            Column(nameof(PaymentViewDto.AppliesTo), "Applies to", 115);
            MoneyColumn(nameof(PaymentViewDto.Amount), "Amount", 80);
            Column(nameof(PaymentViewDto.Method), "Method", 70);
            Column(nameof(PaymentViewDto.ReferenceNo), "Reference", 95);
            Column(nameof(PaymentViewDto.ProcessedBy), "Cashier", 100);
            StatusColumn(nameof(PaymentViewDto.Status), "Status", 80);
        }

        protected override bool Matches(PaymentViewDto p, string term) =>
            p.PaymentId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (p.MemberName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (p.ReferenceNo ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(PaymentViewDto p) =>
            $"payment #{p.PaymentId} of {UiKit.Money(p.Amount)} from {p.MemberName}";

        protected override void AfterLoad()
        {
            _ = RefreshSummaryAsync();
        }

        private async Task RefreshSummaryAsync()
        {
            var summary = Unwrap(await Session.Payments.GetSummaryAsync());
            if (summary is null || IsDisposed) return;

            _summary.Text =
                $"collected {UiKit.Money(summary.TotalCollected)}      " +
                $"this month {UiKit.Money(summary.CollectedThisMonth)}      " +
                $"outstanding {UiKit.Money(summary.TotalOutstanding)}      " +
                $"{summary.PendingCount:N0} pending";
        }

        /// <summary>
        /// The shared tail of every payment dialog: amount, method, date, the cash tendered and
        /// its change, reference and notes. <paramref name="onAmountOrTenderChanged"/> is wired
        /// to both the amount and the tendered field so the change figure follows either one.
        /// </summary>
        private static List<FieldSpec> CommonPaymentFields(
            PaymentViewDto? existing, Action<FieldSpec> onAmountOrTenderChanged)
        {
            var fields = new List<FieldSpec>();

            fields.Add(new FieldSpec("amount", "Amount due", FieldKind.Money)
            {
                Required = true,
                Value = existing?.Amount ?? 0m,
                Minimum = 0.01m,
                Maximum = 10_000_000m,
                Hint = "Must be greater than zero.",
                OnChanged = onAmountOrTenderChanged
            });

            fields.Add(new FieldSpec("method", "Payment method", FieldKind.Combo)
            {
                Required = true,
                Value = existing?.Method ?? "Cash",
                Options = Methods
            });

            fields.Add(new FieldSpec("date", "Payment date", FieldKind.Date)
            {
                Required = true,
                Value = existing?.PaymentDate ?? DateTime.Today,
                Validate = f => f.Date.Date > DateTime.Today.AddDays(1)
                    ? "A payment cannot be dated in the future."
                    : null
            });

            fields.Add(new FieldSpec("status", "Status", FieldKind.Combo)
            {
                Required = true,
                Value = existing?.Status ?? "Completed",
                Options = new()
                {
                    new("Completed", "Completed"), new("Pending", "Pending"),
                    new("Refunded", "Refunded"), new("Failed", "Failed")
                }
            });

            fields.Add(new FieldSpec("tendered", "Amount paid / money received", FieldKind.Money)
            {
                Value = existing?.AmountTendered,
                Minimum = 0m,
                Maximum = 100_000_000m,
                Hint = "For cash only. Leave blank when the amount paid exactly matches the amount due.",
                OnChanged = onAmountOrTenderChanged
            });

            fields.Add(new FieldSpec("change", "Change", FieldKind.Money)
            {
                ReadOnly = true,
                Value = existing?.ChangeGiven ?? 0m,
                Hint = "Calculated automatically from the amount due and the amount paid."
            });

            fields.Add(new FieldSpec("ref", "Reference")
            {
                Value = existing?.ReferenceNo,
                MaxLength = 60,
                Hint = "Receipt or transaction number."
            });

            fields.Add(new FieldSpec("notes", "Notes", FieldKind.Multiline)
                { Value = existing?.Notes, MaxLength = 300 });

            return fields;
        }

        /// <summary>
        /// Recomputes the read-only Change field from whichever of Amount due / Amount paid the
        /// operator just changed. Never negative on screen - a shortfall is a business-rule
        /// refusal the server reports on save, not something shown as change.
        /// </summary>
        private static void RecalculateChange(IList<FieldSpec> fields)
        {
            var amount = fields.First(f => f.Key == "amount").Decimal;
            var tenderedText = fields.First(f => f.Key == "tendered").Text;
            var changeField = fields.First(f => f.Key == "change");

            if (string.IsNullOrWhiteSpace(tenderedText) || changeField.Control is not { } control)
            {
                return;
            }

            var tendered = decimal.TryParse(tenderedText, out var t) ? t : 0m;
            var change = tendered - amount;
            control.Text = (change > 0 ? change : 0m).ToString("N2");
        }

        private static List<KeyValuePair<string, string>> PaymentForOptions() => new()
        {
            new(nameof(PaymentFor.Membership), "Membership"),
            new(nameof(PaymentFor.WalkIn), "Walk-In"),
            new(nameof(PaymentFor.Sale), "Sale / product"),
            new(nameof(PaymentFor.Other), "Other gym item or service")
        };

        /// <summary>
        /// Asks what a new payment is for before building the rest of the dialog. Payments is
        /// the central log for every kind of money the gym takes, so it does not default to a
        /// member the way a subscription's own settle step reasonably does.
        /// </summary>
        private PaymentFor? ChoosePaymentFor()
        {
            PaymentFor? chosen = null;

            var picked = EditDialog.Run(this, "New payment", "What is this payment for?",
                new List<FieldSpec>
                {
                    new("for", "This payment is for", FieldKind.Combo)
                    {
                        Required = true,
                        Value = nameof(PaymentFor.Membership),
                        Options = PaymentForOptions()
                    }
                },
                f =>
                {
                    chosen = Enum.Parse<PaymentFor>(f.First(x => x.Key == "for").ComboValue!);
                    return Task.FromResult<string?>(null);
                }, "Continue");

            return picked ? chosen : null;
        }

        protected override async Task<bool> OnAddAsync()
        {
            var paymentFor = ChoosePaymentFor();
            if (paymentFor is null) return false;

            if (paymentFor == PaymentFor.Membership && _members.Count == 0)
            {
                ShowError("There are no members yet. Add one from the Membership module first, " +
                          "or record this as a Walk-In payment instead.");
                return false;
            }

            var settleable = _sales.Where(s => s.Balance > 0m).ToList();
            if (paymentFor == PaymentFor.Sale && settleable.Count == 0)
            {
                ShowError("There is no sale with an outstanding balance to settle.");
                return false;
            }

            var fields = new List<FieldSpec>();

            switch (paymentFor)
            {
                case PaymentFor.Membership:
                    fields.Add(new FieldSpec("member", "Member", FieldKind.Combo)
                    {
                        Required = true,
                        Options = _members
                            .Select(m => new KeyValuePair<string, string>(
                                m.MemberId.ToString(), $"{m.FirstName} {m.LastName} (#{m.MemberId})"))
                            .ToList()
                    });
                    break;

                case PaymentFor.WalkIn:
                    fields.Add(new FieldSpec("walkin", "Walk-in's name")
                    {
                        MaxLength = 150,
                        Hint = "Optional. Left blank, the receipt and the payments list read \"Walk-In\"."
                    });
                    break;

                case PaymentFor.Sale:
                    fields.Add(new FieldSpec("sale", "Sale", FieldKind.Combo)
                    {
                        Required = true,
                        Options = settleable
                            .Select(s => new KeyValuePair<string, string>(
                                s.SaleId.ToString(),
                                $"Sale #{s.SaleId} — {s.MemberName} — balance {UiKit.Money(s.Balance)}"))
                            .ToList()
                    });
                    break;

                case PaymentFor.Other:
                    // Nothing further identifies the payer - the reference and notes fields in
                    // the common tail are where the operator says what this was.
                    break;
            }

            fields.AddRange(CommonPaymentFields(null, _ => RecalculateChange(fields)));

            var saved = EditDialog.Run(this, "Record payment",
                "The cashier is recorded automatically from your signed-in account.",
                fields, async f =>
                {
                    var result = await Session.Payments.RecordAsync(new RecordPaymentDto
                    {
                        MemberId = paymentFor == PaymentFor.Membership
                            ? int.Parse(f.First(x => x.Key == "member").ComboValue ?? "0")
                            : null,
                        WalkInName = paymentFor == PaymentFor.WalkIn
                            ? f.First(x => x.Key == "walkin").Text
                            : null,
                        SaleId = paymentFor == PaymentFor.Sale
                            ? int.Parse(f.First(x => x.Key == "sale").ComboValue ?? "0")
                            : null,
                        Amount = f.First(x => x.Key == "amount").Decimal,
                        Method = f.First(x => x.Key == "method").ComboValue ?? "Cash",
                        PaymentDate = f.First(x => x.Key == "date").Date,
                        Status = f.First(x => x.Key == "status").ComboValue ?? "Completed",
                        ReferenceNo = f.First(x => x.Key == "ref").Text,
                        Notes = f.First(x => x.Key == "notes").Text,
                        AmountTendered = string.IsNullOrWhiteSpace(f.First(x => x.Key == "tendered").Text)
                            ? null
                            : f.First(x => x.Key == "tendered").Decimal
                    });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Record payment");

            return saved;
        }

        protected override Task<bool> OnEditAsync(PaymentViewDto p)
        {
            var fields = new List<FieldSpec>();
            fields.AddRange(CommonPaymentFields(p, _ => RecalculateChange(fields)));

            var saved = EditDialog.Run(this, $"Edit payment #{p.PaymentId}",
                $"{UiKit.Money(p.Amount)} from {p.MemberName}. Who it belongs to cannot be " +
                "changed — delete and re-record it if that is wrong.",
                fields, async f =>
                {
                    var result = await Session.Payments.UpdateAsync(p.PaymentId, new UpdatePaymentDto
                    {
                        Amount = f.First(x => x.Key == "amount").Decimal,
                        Method = f.First(x => x.Key == "method").ComboValue ?? "Cash",
                        PaymentDate = f.First(x => x.Key == "date").Date,
                        Status = f.First(x => x.Key == "status").ComboValue ?? "Completed",
                        ReferenceNo = f.First(x => x.Key == "ref").Text,
                        Notes = f.First(x => x.Key == "notes").Text,
                        AmountTendered = string.IsNullOrWhiteSpace(f.First(x => x.Key == "tendered").Text)
                            ? null
                            : f.First(x => x.Key == "tendered").Decimal
                    });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Save changes");

            return Task.FromResult(saved);
        }

        protected override async Task<string?> OnDeleteAsync(PaymentViewDto p)
        {
            var result = await Session.Payments.DeleteAsync(p.PaymentId);
            return result.IsSuccess ? null : result.ErrorMessage;
        }

        /// <summary>Opens the payment receipt for the selected row.</summary>
        private Task ShowReceiptAsync()
        {
            var p = Selected;
            if (p is null) { ShowError("Select a payment to view its receipt."); return Task.CompletedTask; }

            ReceiptDialog.Show(this, ReceiptBuilder.ForPayment(Session, p));
            return Task.CompletedTask;
        }

        private async Task VoidAsync()
        {
            if (IsBusy) return;

            var p = Selected;
            if (p is null) { ShowError("Select a payment to void."); return; }

            var fields = new List<FieldSpec>
            {
                new("reason", "Reason")
                {
                    Required = true, MaxLength = 300,
                    Hint = "Recorded against the payment, e.g. “duplicate entry”."
                }
            };

            var voided = EditDialog.Run(this, $"Void payment #{p.PaymentId}",
                $"{UiKit.Money(p.Amount)} from {p.MemberName}. Voiding keeps the payment on " +
                "record but removes it from collections, and reopens whatever it settled.",
                fields, async f =>
                {
                    var result = await Session.Payments.VoidAsync(p.PaymentId,
                        new VoidPaymentDto { Reason = f.First(x => x.Key == "reason").Text });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Void payment");

            if (!voided) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify($"Payment #{p.PaymentId} voided successfully.");
            }, "Refreshing…");
        }
    }
}
