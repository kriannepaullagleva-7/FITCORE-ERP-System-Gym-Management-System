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
        private List<PaymentViewDto> _all = new();

        public PaymentsPage(FitCoreSession session)
            : base(session, "Payment Transactions",
                   "Money received, what it settled, and what is still outstanding.",
                   "payment", "Member, reference or payment ID")
        {
            AddAction("Void", ButtonTone.Secondary, () => VoidAsync(), 84);
            AddAction("Set status", ButtonTone.Secondary, () => SetStatusAsync(), 104);

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

        private List<FieldSpec> PaymentFields(PaymentViewDto? existing)
        {
            var fields = new List<FieldSpec>();

            if (existing is null)
            {
                fields.Add(new FieldSpec("member", "Member", FieldKind.Combo)
                {
                    Required = true,
                    Options = _members
                        .Select(m => new KeyValuePair<string, string>(
                            m.MemberId.ToString(), $"{m.FirstName} {m.LastName} (#{m.MemberId})"))
                        .ToList()
                });
            }

            fields.Add(new FieldSpec("amount", "Amount", FieldKind.Money)
            {
                Required = true,
                Value = existing?.Amount ?? 0m,
                Minimum = 0.01m,
                Maximum = 10_000_000m,
                Hint = "Must be greater than zero."
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

        protected override Task<bool> OnAddAsync()
        {
            if (_members.Count == 0)
            {
                ShowError("There are no members yet. A payment is always recorded against a " +
                          "member, so add one from the Membership module first.");
                return Task.FromResult(false);
            }

            var saved = EditDialog.Run(this, "Record payment",
                "Recorded against the member. It settles their oldest outstanding balance first.",
                PaymentFields(null), async f =>
                {
                    var result = await Session.Payments.RecordAsync(new RecordPaymentDto
                    {
                        MemberId = int.Parse(f.First(x => x.Key == "member").ComboValue ?? "0"),
                        Amount = f.First(x => x.Key == "amount").Decimal,
                        Method = f.First(x => x.Key == "method").ComboValue ?? "Cash",
                        PaymentDate = f.First(x => x.Key == "date").Date,
                        Status = f.First(x => x.Key == "status").ComboValue ?? "Completed",
                        ReferenceNo = f.First(x => x.Key == "ref").Text,
                        Notes = f.First(x => x.Key == "notes").Text
                    });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Record payment");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(PaymentViewDto p)
        {
            var saved = EditDialog.Run(this, $"Edit payment #{p.PaymentId}",
                $"{UiKit.Money(p.Amount)} from {p.MemberName}. The member it belongs to cannot " +
                "be changed — delete and re-record it if that is wrong.",
                PaymentFields(p), async f =>
                {
                    var result = await Session.Payments.UpdateAsync(p.PaymentId, new UpdatePaymentDto
                    {
                        Amount = f.First(x => x.Key == "amount").Decimal,
                        Method = f.First(x => x.Key == "method").ComboValue ?? "Cash",
                        PaymentDate = f.First(x => x.Key == "date").Date,
                        Status = f.First(x => x.Key == "status").ComboValue ?? "Completed",
                        ReferenceNo = f.First(x => x.Key == "ref").Text,
                        Notes = f.First(x => x.Key == "notes").Text
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

        private async Task SetStatusAsync()
        {
            if (IsBusy) return;

            var p = Selected;
            if (p is null) { ShowError("Select a payment to change its status."); return; }

            var fields = new List<FieldSpec>
            {
                new("status", "Status", FieldKind.Combo)
                {
                    Required = true,
                    Value = p.Status,
                    Options = new()
                    {
                        new("Completed", "Completed"), new("Pending", "Pending"),
                        new("Refunded", "Refunded"), new("Failed", "Failed")
                    },
                    Hint = "Only a Completed payment counts towards collections."
                }
            };

            var changed = EditDialog.Run(this, $"Status — payment #{p.PaymentId}",
                $"{UiKit.Money(p.Amount)} from {p.MemberName}.", fields, async f =>
                {
                    var result = await Session.Payments.SetStatusAsync(p.PaymentId,
                        new UpdatePaymentStatusDto
                        { Status = f.First(x => x.Key == "status").ComboValue ?? "Completed" });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Apply");

            if (!changed) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify("Payment status updated successfully.");
            }, "Refreshing…");
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

    // ---------------------------------------------------------------------------- Reports

    /// <summary>
    /// Date-filtered reports. Every figure is aggregated server-side in the tenant database;
    /// this screen only chooses a range and renders what comes back.
    /// </summary>
    internal sealed class ReportsPage : ModulePageBase
    {
        private DateTimePicker _from = null!;
        private DateTimePicker _to = null!;
        private ComboBox _which = null!;
        private ComboBox _preset = null!;
        private DataGridView _grid = null!;
        private FlowLayoutPanel _headline = null!;
        private Label _caption = null!;

        protected override bool UsesGrid => false;

        public ReportsPage(FitCoreSession session)
            : base(session, "Reports", "Sales, payments, inventory and membership, filtered by date.")
        {
            BuildFilters();
            BuildBody();
        }

        private void BuildFilters()
        {
            FilterBar.Controls.Add(UiKit.FilterLabel("Report"));

            _which = UiKit.Select(170);
            _which.Items.AddRange(new object[] { "Sales", "Payments", "Inventory", "Membership" });
            _which.SelectedIndex = 0;
            _which.SelectedIndexChanged += async (_, _) => await LoadAsync();
            FilterBar.Controls.Add(_which);

            FilterBar.Controls.Add(UiKit.FilterLabel("Period"));

            _preset = UiKit.Select(150);
            _preset.DisplayMember = "Value";
            _preset.ValueMember = "Key";
            _preset.DataSource = new List<KeyValuePair<string, string>>
            {
                new("month", "This month"),
                new("last30", "Last 30 days"),
                new("quarter", "Last 3 months"),
                new("year", "This year"),
                new("custom", "Custom range")
            };
            _preset.SelectedIndexChanged += async (_, _) => await ApplyPresetAsync();
            FilterBar.Controls.Add(_preset);

            FilterBar.Controls.Add(UiKit.FilterLabel("From"));
            _from = UiKit.DatePicker(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
            FilterBar.Controls.Add(_from);

            FilterBar.Controls.Add(UiKit.FilterLabel("To"));
            _to = UiKit.DatePicker(DateTime.Today);
            FilterBar.Controls.Add(_to);

            FilterBar.Controls.Add(UiKit.Action("Apply", ButtonTone.Primary,
                async (_, _) => await LoadAsync(), 92));

            AddAction("Refresh", ButtonTone.Secondary, () => LoadAsync(), 90);
        }

        private async Task ApplyPresetAsync()
        {
            var today = DateTime.Today;

            switch (_preset.SelectedValue?.ToString())
            {
                case "month":
                    _from.Value = new DateTime(today.Year, today.Month, 1);
                    _to.Value = today;
                    break;
                case "last30":
                    _from.Value = today.AddDays(-30);
                    _to.Value = today;
                    break;
                case "quarter":
                    _from.Value = today.AddMonths(-3);
                    _to.Value = today;
                    break;
                case "year":
                    _from.Value = new DateTime(today.Year, 1, 1);
                    _to.Value = today;
                    break;
                default:
                    return;   // custom: leave the pickers to the operator
            }

            await LoadAsync();
        }

        private void BuildBody()
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Canvas,
                Padding = new Padding(1)
            };
            card.Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, card.Width, card.Height), UiTheme.Surface, UiTheme.Border);

            _headline = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 78,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.Surface,
                Padding = new Padding(14, 14, 14, 10)
            };

            _caption = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                BackColor = UiTheme.Surface,
                UseMnemonic = false
            };

            _grid = UiKit.Grid();
            _grid.AutoGenerateColumns = true;
            UiKit.HumaniseColumns(_grid);

            card.Controls.Add(_grid);
            card.Controls.Add(_caption);
            card.Controls.Add(_headline);

            SetBody(card);
        }

        /// <summary>One headline figure on the strip above the report table.</summary>
        private void Figure(string label, string value, Color? accent = null)
        {
            var box = new Panel
            {
                Width = 168,
                Height = 50,
                BackColor = UiTheme.Surface,
                Margin = new Padding(0, 0, 10, 0)
            };

            box.Controls.Add(new Label
            {
                Text = label.ToUpperInvariant(),
                Font = UiTheme.Overline,
                ForeColor = UiTheme.TextMuted,
                Location = new Point(0, 0),
                AutoSize = true,
                UseMnemonic = false
            });

            box.Controls.Add(new Label
            {
                Text = value,
                Font = new Font(UiTheme.FamilySemibold, 13F),
                ForeColor = accent ?? UiTheme.TextPrimary,
                Location = new Point(0, 18),
                AutoSize = true,
                UseMnemonic = false
            });

            _headline.Controls.Add(box);
        }

        public override Task LoadAsync() => GuardAsync(async () =>
        {
            var from = _from.Value.Date;
            var to = _to.Value.Date;

            if (to < from)
            {
                ShowError("The “to” date cannot be before the “from” date.");
                return;
            }

            _grid.DataSource = null;
            _headline.Controls.Clear();

            var range = $"{UiKit.Date(from)} to {UiKit.Date(to)}";

            switch (_which.SelectedItem?.ToString())
            {
                case "Sales":
                {
                    var report = Unwrap(await Session.Reports.GetSalesReportAsync(from, to));
                    if (report is null) return;

                    Figure("Transactions", report.TransactionCount.ToString("N0"));
                    Figure("Gross sales", UiKit.Money(report.GrossSales));
                    Figure("Discounts", UiKit.Money(report.Discounts));
                    Figure("Net sales", UiKit.Money(report.NetSales), UiTheme.Primary);
                    Figure("Units sold", report.UnitsSold.ToString("N0"));
                    Figure("Outstanding", UiKit.Money(report.Outstanding),
                        report.Outstanding > 0 ? UiTheme.Danger : UiTheme.Success);

                    _grid.DataSource = report.TopProducts;
                    _caption.Text = $"Top products by revenue — {range}";
                    SetStatus($"{report.TopProducts.Count:N0} product(s) sold in this period");
                    break;
                }

                case "Payments":
                {
                    var report = Unwrap(await Session.Reports.GetPaymentReportAsync(from, to));
                    if (report is null) return;

                    Figure("Payments", report.Count.ToString("N0"));
                    Figure("Collected", UiKit.Money(report.TotalCollected), UiTheme.Success);
                    Figure("Pending", UiKit.Money(report.Pending), UiTheme.Warning);
                    Figure("Refunded", UiKit.Money(report.Refunded));
                    Figure("Outstanding", UiKit.Money(report.TotalOutstanding),
                        report.TotalOutstanding > 0 ? UiTheme.Danger : UiTheme.Success);

                    _grid.DataSource = report.ByMethod;
                    _caption.Text = $"Collections by payment method — {range}";
                    SetStatus($"{report.PaidCount:N0} paid, {report.PartiallyPaidCount:N0} part paid, " +
                              $"{report.UnpaidCount:N0} unpaid");
                    break;
                }

                case "Inventory":
                {
                    var report = Unwrap(await Session.Reports.GetInventoryReportAsync(from, to));
                    if (report is null) return;

                    Figure("Products", report.Summary.TotalProducts.ToString("N0"));
                    Figure("Stock value", UiKit.Money(report.Summary.StockValue), UiTheme.Primary);
                    Figure("Retail value", UiKit.Money(report.Summary.RetailValue));
                    Figure("Low stock", report.Summary.LowStock.ToString("N0"),
                        report.Summary.LowStock > 0 ? UiTheme.Warning : UiTheme.Success);
                    Figure("Out of stock", report.Summary.OutOfStock.ToString("N0"),
                        report.Summary.OutOfStock > 0 ? UiTheme.Danger : UiTheme.Success);

                    _grid.DataSource = report.Movements;
                    _caption.Text = $"Stock movements — {range}";
                    SetStatus($"{report.Movements.Count:N0} movement(s) in this period");
                    break;
                }

                case "Membership":
                {
                    var report = Unwrap(await Session.Reports.GetMembershipReportAsync(from, to));
                    if (report is null) return;

                    Figure("Members", report.TotalMembers.ToString("N0"));
                    Figure("Active", report.ActiveMembers.ToString("N0"), UiTheme.Success);
                    Figure("New in range", report.NewMembersInRange.ToString("N0"));
                    Figure("Expiring soon", report.ExpiringSoon.ToString("N0"),
                        report.ExpiringSoon > 0 ? UiTheme.Warning : UiTheme.Success);
                    Figure("Revenue", UiKit.Money(report.MembershipRevenue), UiTheme.Primary);
                    Figure("Outstanding", UiKit.Money(report.MembershipOutstanding),
                        report.MembershipOutstanding > 0 ? UiTheme.Danger : UiTheme.Success);

                    _grid.DataSource = report.ByPlan;
                    _caption.Text = $"Membership by plan — {range}";
                    SetStatus($"{report.ActiveSubscriptions:N0} active subscription(s)");
                    break;
                }
            }
        }, "Running the report…");
    }
}
