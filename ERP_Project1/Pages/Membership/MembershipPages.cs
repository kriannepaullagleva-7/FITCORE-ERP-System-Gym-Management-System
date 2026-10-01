using ERP_Project1.Api;

namespace ERP_Project1
{
    // ---------------------------------------------------------------------------- Members

    internal sealed class MembersPage : CrudPageBase<MemberDto>
    {
        public MembersPage(FitCoreSession session)
            : base(session, "Members", "Everyone who trains here, and their current standing.",
                   "member", "Name, phone or email")
        {
            AddAction("Suspend", ButtonTone.Warning, SuspendAsync, 90);
            AddAction("Restore", ButtonTone.Secondary, () => RestoreAsync(), 90);
        }

        protected override string DeleteConsequence =>
            "A member who has ever paid, subscribed or bought something cannot be deleted, " +
            "because that would take their financial history with them.";

        protected override string EmptyHeadline => "No members yet";
        protected override string EmptyDetail =>
            "Add your first member to start selling subscriptions, recording payments and " +
            "ringing up sales against them.";

        protected override async Task<List<MemberDto>?> FetchAsync() =>
            Unwrap(await Session.Members.GetAllAsync());

        protected override void DefineColumns()
        {
            Column(nameof(MemberDto.MemberId), "ID", 40, rightAlign: true);
            Column(nameof(MemberDto.FirstName), "First name", 110);
            Column(nameof(MemberDto.LastName), "Last name", 110);
            Column(nameof(MemberDto.Phone), "Phone", 90);
            Column(nameof(MemberDto.Email), "Email", 150);
            DateColumn(nameof(MemberDto.JoinDate), "Joined", 90);
            StatusColumn(nameof(MemberDto.Status), "Status", 80);
        }

        protected override bool Matches(MemberDto m, string term) =>
            m.FirstName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            m.LastName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (m.Phone ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (m.Email ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(MemberDto m) => $"{m.FirstName} {m.LastName}";

        /// <summary>
        /// The lengths mirror the columns the API validates against, so an over-long value is
        /// caught in the dialog rather than coming back as a 400.
        /// </summary>
        private static List<FieldSpec> Fields(MemberDto? m) => new()
        {
            new("first", "First name") { Required = true, Value = m?.FirstName, MaxLength = 100 },
            new("last", "Last name") { Required = true, Value = m?.LastName, MaxLength = 100 },
            new("phone", "Phone", FieldKind.Phone) { Value = m?.Phone, MaxLength = 20 },
            new("email", "Email", FieldKind.Email)
                { Value = m?.Email, MaxLength = 100, Hint = "Optional, but must be valid if given." }
        };

        protected override Task<bool> OnAddAsync()
        {
            var saved = EditDialog.Run(this, "Add member", "A new member starts Active.",
                Fields(null), async f =>
                {
                    var result = await Session.Members.CreateAsync(new CreateMemberDto
                    {
                        FirstName = f.First(x => x.Key == "first").Text,
                        LastName = f.First(x => x.Key == "last").Text,
                        Phone = f.First(x => x.Key == "phone").Text,
                        Email = f.First(x => x.Key == "email").Text
                    });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Create member");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(MemberDto m)
        {
            var fields = Fields(m);
            fields.Add(new FieldSpec("status", "Status", FieldKind.Combo)
            {
                Required = true,
                Value = m.Status,
                Options = new()
                {
                    new("Active", "Active"),
                    new("Inactive", "Inactive"),
                    new("Suspended", "Suspended")
                }
            });

            var saved = EditDialog.Run(this, $"Edit {m.FirstName} {m.LastName}",
                $"Member #{m.MemberId}, joined {UiKit.Date(m.JoinDate)}.", fields, async f =>
                {
                    var result = await Session.Members.UpdateAsync(m.MemberId, new UpdateMemberDto
                    {
                        FirstName = f.First(x => x.Key == "first").Text,
                        LastName = f.First(x => x.Key == "last").Text,
                        Phone = f.First(x => x.Key == "phone").Text,
                        Email = f.First(x => x.Key == "email").Text,
                        Status = f.First(x => x.Key == "status").ComboValue ?? "Active"
                    });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Save changes");

            return Task.FromResult(saved);
        }

        protected override async Task<string?> OnDeleteAsync(MemberDto m)
        {
            var result = await Session.Members.DeleteAsync(m.MemberId);

            // The server refuses to delete a member who has paid, subscribed or bought
            // anything, because that would take the gym's takings with them.
            return result.IsSuccess
                ? null
                : result.ErrorMessage ?? "This member could not be deleted.";
        }

        private async Task RestoreAsync()
        {
            if (IsBusy) return;

            var m = Selected;
            if (m is null) { ShowError("Select a member to restore."); return; }

            await GuardAsync(async () =>
            {
                var result = await Session.Members.RestoreAsync(m.MemberId);
                if (!result.IsSuccess) { ShowError(result.ErrorMessage); return; }

                await LoadAsync();
                Notify($"{m.FirstName} {m.LastName} restored successfully.");
            }, "Restoring…");
        }

        /// <summary>
        /// Pauses a membership rather than ending it.
        ///
        /// Distinct from archiving on purpose. An archived member has left; a suspended one is
        /// injured, or away, and is coming back. Keeping them apart is what lets a gym tell
        /// "we lost forty members this year" from "forty members are injured" - two very
        /// different pieces of news that a single Inactive status would merge.
        /// </summary>
        private async Task SuspendAsync()
        {
            if (IsBusy) return;

            var m = Selected;
            if (m is null) { ShowError("Select a member to suspend."); return; }

            if (string.Equals(m.Status, "Suspended", StringComparison.OrdinalIgnoreCase))
            {
                if (UiKit.Confirm(
                        $"{m.FirstName} {m.LastName} is suspended. Put them back on the active roll?",
                        "End suspension") != DialogResult.Yes) return;

                await GuardAsync(async () =>
                {
                    var result = await Session.Notes.ReactivateAsync(m.MemberId);
                    if (!result.IsSuccess) { ShowError(result.ErrorMessage); return; }

                    await LoadAsync();
                    Notify($"{m.FirstName} {m.LastName} is active again.");
                }, "Reactivating…");

                return;
            }

            var suspended = EditDialog.Run(this,
                $"Suspend {m.FirstName} {m.LastName}",
                "They drop off the active roll but keep their membership, their history and " +
                "their place.",
                new List<FieldSpec>
                {
                    new("reason", "Why?", FieldKind.Multiline)
                    {
                        Required = true, MaxLength = 300,
                        Hint = "The front desk will see this, so make it something they can repeat."
                    },
                    new("until", "Expected back", FieldKind.Date)
                    {
                        Value = DateTime.Today.AddMonths(1),
                        Hint = "Leave as today for an open-ended suspension."
                    }
                },
                async f =>
                {
                    var until = f.First(x => x.Key == "until").Date;

                    var result = await Session.Notes.SuspendAsync(m.MemberId, new SuspendMemberDto
                    {
                        Reason = f.First(x => x.Key == "reason").Text,
                        Until = until.Date <= DateTime.Today ? null : until
                    });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Suspend membership");

            if (!suspended) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify($"{m.FirstName} {m.LastName} suspended.");
            }, "Refreshing…");
        }

    }

    // ----------------------------------------------------------------- Membership plans

    internal sealed class PlansPage : CrudPageBase<MembershipPlanDto>
    {
        public PlansPage(FitCoreSession session)
            : base(session, "Membership Plans",
                   "What members can subscribe to, with duration and price.",
                   "plan", "Plan name or description")
        { }

        protected override string DeleteConsequence =>
            "A plan that has been subscribed to cannot be deleted. If it is simply no longer " +
            "sold, edit it and clear Active instead.";

        protected override string EmptyHeadline => "No membership plans yet";
        protected override string EmptyDetail =>
            "A plan sets the price and the duration a subscription runs for. Create one before " +
            "selling memberships.";

        protected override async Task<List<MembershipPlanDto>?> FetchAsync() =>
            Unwrap(await Session.Plans.GetAllAsync());

        protected override void DefineColumns()
        {
            Column(nameof(MembershipPlanDto.PlanId), "ID", 40, rightAlign: true);
            Column(nameof(MembershipPlanDto.PlanName), "Plan", 160);
            Column(nameof(MembershipPlanDto.DurationMonths), "Months", 60, rightAlign: true);
            MoneyColumn(nameof(MembershipPlanDto.Price), "Price", 80);
            Column(nameof(MembershipPlanDto.Description), "Description", 200);
            FlagColumn(nameof(MembershipPlanDto.IsActive), "Status", 70);
        }

        protected override bool Matches(MembershipPlanDto p, string term) =>
            p.PlanName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (p.Description ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(MembershipPlanDto p) => $"the plan “{p.PlanName}”";

        private static List<FieldSpec> Fields(MembershipPlanDto? p) => new()
        {
            new("name", "Plan name") { Required = true, Value = p?.PlanName, MaxLength = 100 },

            new("months", "Duration in months", FieldKind.Integer)
            {
                Required = true,
                Value = p?.DurationMonths ?? 1,
                Minimum = 1,
                Maximum = 120,
                Hint = "Between 1 and 120. The subscription end date is derived from this."
            },

            new("price", "Price", FieldKind.Money)
                { Required = true, Value = p?.Price ?? 0m, Minimum = 0m, Maximum = 10_000_000m },

            new("desc", "Description", FieldKind.Multiline) { Value = p?.Description, MaxLength = 500 }
        };

        protected override Task<bool> OnAddAsync()
        {
            var saved = EditDialog.Run(this, "Add membership plan",
                "The subscription end date is calculated from the duration.",
                Fields(null), async f =>
                {
                    var result = await Session.Plans.CreateAsync(new CreateMembershipPlanDto
                    {
                        PlanName = f.First(x => x.Key == "name").Text,
                        DurationMonths = f.First(x => x.Key == "months").Int,
                        Price = f.First(x => x.Key == "price").Decimal,
                        Description = f.First(x => x.Key == "desc").Text
                    });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Create plan");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(MembershipPlanDto p)
        {
            var fields = Fields(p);
            fields.Add(new FieldSpec("active", "Available to sell", FieldKind.Check) { Value = p.IsActive });

            var saved = EditDialog.Run(this, $"Edit {p.PlanName}",
                "Changing the price does not alter subscriptions already sold.", fields, async f =>
                {
                    var result = await Session.Plans.UpdateAsync(p.PlanId, new UpdateMembershipPlanDto
                    {
                        PlanName = f.First(x => x.Key == "name").Text,
                        DurationMonths = f.First(x => x.Key == "months").Int,
                        Price = f.First(x => x.Key == "price").Decimal,
                        Description = f.First(x => x.Key == "desc").Text,
                        IsActive = f.First(x => x.Key == "active").Flag
                    });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Save changes");

            return Task.FromResult(saved);
        }

        protected override async Task<string?> OnDeleteAsync(MembershipPlanDto p)
        {
            var result = await Session.Plans.DeleteAsync(p.PlanId);
            return result.IsSuccess ? null : result.ErrorMessage;
        }
    }

    // ------------------------------------------------------------------- Subscriptions

    /// <summary>
    /// Reads the membership overview rather than the raw subscription list: SubscriptionDto
    /// carries only ids, while the overview joins member, plan, expiry and balance in one
    /// server-side query.
    /// </summary>
    internal sealed class SubscriptionsPage : CrudPageBase<MembershipOverviewDto>
    {
        private List<MemberDto> _members = new();
        private List<MembershipPlanDto> _plans = new();

        public SubscriptionsPage(FitCoreSession session)
            : base(session, "Subscriptions", "Every membership sold, with its expiry and balance.",
                   "subscription", "Member or plan")
        {
            AddAction("View receipt", ButtonTone.Secondary, () => ShowReceiptAsync(), 116);

            // Renewal history lives in the audit trail rather than a second copy of the
            // subscription row - renewing extends the existing membership in place, so the
            // trail's before/after values are the only record of what it used to be. Shown
            // only to an administrator, the one audience the trail itself is restricted to.
            if (Session.CurrentUser?.IsAdminOrAbove == true)
            {
                AddAction("History", ButtonTone.Secondary, () => ShowHistoryAsync(), 84);
            }

            AddAction("Expire overdue", ButtonTone.Secondary, () => ExpireOverdueAsync(), 124);
            AddAction("Cancel", ButtonTone.Secondary, () => CancelAsync(), 84);
            AddAction("Renew", ButtonTone.Secondary, () => RenewAsync(), 84);
        }

        protected override bool SupportsEdit => false;

        protected override string CreatedMessage => "Subscription created successfully.";
        protected override string DeletedMessage => "Subscription deleted successfully.";

        protected override string DeleteConsequence =>
            "Any payments already recorded against this membership stay on the member's " +
            "ledger. This cannot be undone.";

        protected override string EmptyHeadline => "No subscriptions yet";
        protected override string EmptyDetail =>
            "Sell a membership by choosing a member and an active plan. The end date is " +
            "calculated from the plan's duration.";

        protected override async Task<List<MembershipOverviewDto>?> FetchAsync()
        {
            _members = Unwrap(await Session.Members.GetAllAsync()) ?? new();
            _plans = Unwrap(await Session.Plans.GetActiveAsync()) ?? new();
            return Unwrap(await Session.Reports.GetMembershipOverviewAsync());
        }

        protected override void DefineColumns()
        {
            Column(nameof(MembershipOverviewDto.FullName), "Member", 150);
            Column(nameof(MembershipOverviewDto.PlanName), "Plan", 130);
            DateColumn(nameof(MembershipOverviewDto.StartDate), "Start", 80);
            DateColumn(nameof(MembershipOverviewDto.ExpiryDate), "Expires", 80);
            Column(nameof(MembershipOverviewDto.DaysRemaining), "Days left", 60, rightAlign: true);
            StatusColumn(nameof(MembershipOverviewDto.MembershipStatus), "Membership", 100);
            MoneyColumn(nameof(MembershipOverviewDto.AmountPaid), "Paid", 70);
            MoneyColumn(nameof(MembershipOverviewDto.Balance), "Balance", 70);
            StatusColumn(nameof(MembershipOverviewDto.PaymentStatus), "Payment", 90);
        }

        protected override bool Matches(MembershipOverviewDto s, string term) =>
            (s.FullName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (s.PlanName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(MembershipOverviewDto s) =>
            $"subscription #{s.SubscriptionId} for {s.FullName}";

        /// <summary>The sentinel value that means "no member - this is a walk-in" in the Member combo.</summary>
        private const string WalkInSentinel = "";

        private List<KeyValuePair<string, string>> MemberOptionsWithWalkIn() =>
            new[] { new KeyValuePair<string, string>(WalkInSentinel, "— Walk-In (no member account) —") }
                .Concat(_members.Select(m => new KeyValuePair<string, string>(
                    m.MemberId.ToString(), $"{m.FirstName} {m.LastName} (#{m.MemberId})")))
                .ToList();

        protected override Task<bool> OnAddAsync()
        {
            if (_plans.Count == 0)
            {
                ShowError("Create an active membership plan before selling a subscription.");
                return Task.FromResult(false);
            }

            var fields = new List<FieldSpec>
            {
                new("member", "Member", FieldKind.Combo)
                {
                    Options = MemberOptionsWithWalkIn(),
                    Hint = "Leave on Walk-In to sell a membership to somebody with no member account yet."
                },
                new("walkinname", "Walk-in's name")
                {
                    MaxLength = 150,
                    Hint = "Used only when Member above is left on Walk-In. Left blank, this reads \"Walk-In\"."
                },
                new("walkinphone", "Walk-in's phone")
                {
                    MaxLength = 20,
                    Hint = "Optional, and used only for a walk-in."
                },
                new("plan", "Plan", FieldKind.Combo)
                {
                    Required = true,
                    Options = _plans
                        .Select(p => new KeyValuePair<string, string>(
                            p.PlanId.ToString(),
                            $"{p.PlanName} — {UiKit.Money(p.Price)} ({p.DurationMonths} month(s))"))
                        .ToList()
                },
                new("start", "Start date", FieldKind.Date) { Required = true, Value = DateTime.Today }
            };

            // The payment is part of the same dialog rather than a second screen: a
            // membership that is sold but not paid for is the thing this flow exists to
            // prevent, and splitting it in two is exactly how that happens.
            var dueField = new FieldSpec("due", "Amount due", FieldKind.Money)
            {
                ReadOnly = true,
                Value = _plans[0].Price,
                Hint = "Set by the plan chosen above."
            };

            fields.Add(dueField);

            fields.Add(new FieldSpec("method", "Payment method", FieldKind.Combo)
            {
                Required = true,
                Value = "Cash",
                Options = PaymentMethods()
            });

            var paidField = new FieldSpec("paid", "Amount paid / money received", FieldKind.Money)
            {
                Required = true,
                Minimum = 0m,
                Hint = "The change below is calculated automatically."
            };

            var changeField = new FieldSpec("change", "Change", FieldKind.Money)
            {
                ReadOnly = true,
                Value = 0m
            };

            void RecalculateChange(FieldSpec _)
            {
                var due = decimal.TryParse(dueField.Control?.Text, out var d) ? d : 0m;
                var paidText = paidField.Text;
                if (string.IsNullOrWhiteSpace(paidText) || changeField.Control is not { } control) return;

                var paid = decimal.TryParse(paidText, out var p) ? p : 0m;
                var change = paid - due;
                control.Text = (change > 0 ? change : 0m).ToString("N2");
            }

            paidField.OnChanged = RecalculateChange;
            fields.Add(paidField);
            fields.Add(changeField);

            // Amount due follows the chosen plan, and the paid amount defaults to it so a full
            // cash payment needs no typing - the operator only has to act when it differs.
            fields.First(x => x.Key == "plan").OnChanged = f =>
            {
                var planId = int.Parse(f.ComboValue ?? "0");
                var chosen = _plans.FirstOrDefault(p => p.PlanId == planId);
                if (chosen is null || dueField.Control is not { } dueControl) return;

                dueControl.Text = chosen.Price.ToString("N2");
                if (paidField.Control is { } paidControl) paidControl.Text = chosen.Price.ToString("N2");
            };

            fields.Add(new FieldSpec("ref", "Payment reference")
                { MaxLength = 60, Hint = "Receipt or transaction number, if you have one." });

            SubscriptionDto? created = null;
            PaymentViewDto? receiptPayment = null;
            MembershipPlanDto? chosenPlan = null;
            var partyName = "";

            var saved = EditDialog.Run(this, "New subscription",
                "The membership is created and paid for in one step. The end date is " +
                "calculated by the server from the plan's duration.",
                fields, async f =>
                {
                    var memberValue = f.First(x => x.Key == "member").ComboValue;
                    var planId = int.Parse(f.First(x => x.Key == "plan").ComboValue ?? "0");

                    chosenPlan = _plans.FirstOrDefault(p => p.PlanId == planId);
                    if (chosenPlan is null) return "That plan is no longer available.";

                    int? memberId = null;
                    string? walkInName = null;

                    if (memberValue is not null)
                    {
                        memberId = int.Parse(memberValue);
                        var member = _members.FirstOrDefault(m => m.MemberId == memberId);
                        partyName = member is null ? "" : $"{member.FirstName} {member.LastName}".Trim();
                    }
                    else
                    {
                        walkInName = f.First(x => x.Key == "walkinname").Text;
                        partyName = string.IsNullOrWhiteSpace(walkInName) ? "Walk-In" : walkInName;
                    }

                    var result = await Session.Subscriptions.CreateAsync(new CreateSubscriptionDto
                    {
                        MemberId = memberId,
                        WalkInName = walkInName,
                        WalkInPhone = memberValue is null ? f.First(x => x.Key == "walkinphone").Text : null,
                        PlanId = planId,
                        StartDate = f.First(x => x.Key == "start").Date
                    });

                    if (!result.IsSuccess) return result.ErrorMessage;
                    created = result.Value;

                    var paidText = f.First(x => x.Key == "paid").Text;
                    var amountPaid = string.IsNullOrWhiteSpace(paidText) ? chosenPlan.Price : f.First(x => x.Key == "paid").Decimal;

                    return await SettleAsync(
                        created, memberId, walkInName, chosenPlan,
                        f.First(x => x.Key == "method").ComboValue ?? "Cash",
                        f.First(x => x.Key == "ref").Text,
                        amountPaid,
                        payment => receiptPayment = payment);
                }, "Create and take payment");

            if (saved && created is not null && chosenPlan is not null)
            {
                ReceiptDialog.Show(this, ReceiptBuilder.ForSubscription(
                    Session, partyName, chosenPlan, created, receiptPayment, "New membership"));
            }

            return Task.FromResult(saved);
        }

        /// <summary>Reprints the membership receipt for the selected row.</summary>
        private Task ShowReceiptAsync()
        {
            var s = Selected;
            if (s?.SubscriptionId is null)
            {
                ShowError("Select a membership to view its receipt.");
                return Task.CompletedTask;
            }

            ReceiptDialog.Show(this, ReceiptBuilder.ForMembership(Session, s));
            return Task.CompletedTask;
        }

        /// <summary>Every audit event recorded against this subscription - renewals, cancellations, edits.</summary>
        private async Task ShowHistoryAsync()
        {
            if (IsBusy) return;

            var s = Selected;
            if (s?.SubscriptionId is null) { ShowError("Select a membership to view its history."); return; }

            await GuardAsync(async () =>
            {
                var events = Unwrap(await Session.Audit.SearchAsync(
                    entityName: "Subscription", entityId: s.SubscriptionId.Value.ToString(), take: 100));
                if (events is null) return;

                if (events.Count == 0)
                {
                    ShowError($"No history recorded for {s.FullName}'s membership yet.");
                    return;
                }

                using var dialog = new ListDialog($"Membership history — {s.FullName}", events, grid =>
                {
                    grid.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        DataPropertyName = nameof(AuditEventDto.OccurredAt), HeaderText = "When",
                        FillWeight = 90, DefaultCellStyle = { Format = "d MMM yyyy HH:mm" }
                    });
                    grid.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        Name = nameof(AuditEventDto.Action),
                        DataPropertyName = nameof(AuditEventDto.Action), HeaderText = "Action", FillWeight = 70
                    });
                    grid.Columns.Add(new DataGridViewTextBoxColumn
                    { DataPropertyName = nameof(AuditEventDto.Username), HeaderText = "By", FillWeight = 70 });
                    grid.Columns.Add(new DataGridViewTextBoxColumn
                    { DataPropertyName = nameof(AuditEventDto.Summary), HeaderText = "Summary", FillWeight = 220 });
                },
                $"   {events.Count:N0} event(s), most recent first");

                dialog.ShowDialog(this);
            }, "Loading history…");
        }

        private static List<KeyValuePair<string, string>> PaymentMethods() => new()
        {
            new("Cash", "Cash"), new("Card", "Card"), new("GCash", "GCash"),
            new("Transfer", "Transfer"), new("Check", "Check")
        };

        /// <summary>
        /// Takes the plan price against a subscription that has just been written.
        ///
        /// The subscription is already saved by the time this runs, so a payment failure is
        /// reported as exactly that - the membership exists and is unpaid - rather than being
        /// dressed up as a failed subscription. The operator can then settle it from Payments
        /// without re-selling the membership, which is what would create a duplicate.
        /// </summary>
        private async Task<string?> SettleAsync(
            SubscriptionDto? subscription,
            int? memberId,
            string? walkInName,
            MembershipPlanDto plan,
            string method,
            string reference,
            decimal amountPaid,
            Action<PaymentViewDto?> capture)
        {
            if (subscription is null) return null;

            if (!Session.Can(Modules.Payments))
            {
                return "The membership was created, but your account cannot record payments. " +
                       "Ask someone with Payments access to settle it.";
            }

            var payment = await Session.Payments.RecordAsync(new RecordPaymentDto
            {
                MemberId = memberId,
                WalkInName = walkInName,
                SubscriptionId = subscription.SubscriptionId,
                Amount = plan.Price,
                PaymentDate = DateTime.Today,
                Method = method,
                Status = "Completed",
                ReferenceNo = reference,
                Notes = $"Membership — {plan.PlanName}",
                AmountTendered = string.Equals(method, "Cash", StringComparison.OrdinalIgnoreCase)
                    ? amountPaid
                    : null
            });

            if (!payment.IsSuccess)
            {
                return "The membership was created, but the payment could not be recorded:\n" +
                       payment.ErrorMessage +
                       "\nSettle it from the Payments module — do not create the membership again.";
            }

            capture(payment.Value);
            return null;
        }

        protected override async Task<string?> OnDeleteAsync(MembershipOverviewDto s)
        {
            if (s.SubscriptionId is null) return "That member has no subscription to delete.";

            var result = await Session.Subscriptions.DeleteAsync(s.SubscriptionId.Value);
            return result.IsSuccess ? null : result.ErrorMessage;
        }

        private async Task RenewAsync()
        {
            if (IsBusy) return;

            var s = Selected;
            if (s?.SubscriptionId is null) { ShowError("Select a membership to renew."); return; }

            var plan = _plans.FirstOrDefault(p => p.PlanId == s.PlanId);

            if (plan is null)
            {
                ShowError($"The plan behind this membership ({s.PlanName}) is no longer active, " +
                          "so it cannot be renewed at a known price. Sell a new membership instead.");
                return;
            }

            // Renewal takes money, so it goes through the same collect-then-save path as a new
            // membership rather than silently extending the term for free.
            var fields = new List<FieldSpec>
            {
                new("method", "Payment method", FieldKind.Combo)
                {
                    Required = true,
                    Value = "Cash",
                    Options = PaymentMethods(),
                    Hint = $"{plan.PlanName} — {UiKit.Money(plan.Price)} for {plan.DurationMonths} month(s)."
                }
            };

            var paidField = new FieldSpec("paid", "Amount paid / money received", FieldKind.Money)
            {
                Required = true,
                Value = plan.Price,
                Minimum = 0m,
                Hint = "The change below is calculated automatically."
            };

            var changeField = new FieldSpec("change", "Change", FieldKind.Money) { ReadOnly = true, Value = 0m };

            paidField.OnChanged = _ =>
            {
                if (changeField.Control is not { } control) return;
                var paid = paidField.Decimal;
                var change = paid - plan.Price;
                control.Text = (change > 0 ? change : 0m).ToString("N2");
            };

            fields.Add(paidField);
            fields.Add(changeField);
            fields.Add(new FieldSpec("ref", "Payment reference") { MaxLength = 60 });

            SubscriptionDto? renewed = null;
            PaymentViewDto? receiptPayment = null;

            var done = EditDialog.Run(this, $"Renew membership — {s.FullName}",
                "The new period is added onto the current expiry date when that is still in " +
                "the future, so no paid-for days are lost.",
                fields, async f =>
                {
                    var result = await Session.Subscriptions.RenewAsync(s.SubscriptionId.Value);
                    if (!result.IsSuccess) return result.ErrorMessage;

                    renewed = result.Value;

                    var method = f.First(x => x.Key == "method").ComboValue ?? "Cash";

                    return await SettleAsync(
                        renewed, s.MemberId, s.MemberId is null ? s.FirstName : null, plan,
                        method, f.First(x => x.Key == "ref").Text, f.First(x => x.Key == "paid").Decimal,
                        payment => receiptPayment = payment);
                }, "Renew and take payment");

            if (!done) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();

                Notify(renewed is null
                    ? "Membership renewed successfully."
                    : $"Membership renewed successfully — now expires {UiKit.Date(renewed.EndDate)}.");
            }, "Refreshing…");

            if (renewed is not null)
            {
                ReceiptDialog.Show(this, ReceiptBuilder.ForSubscription(
                    Session, s.FullName ?? "", plan, renewed, receiptPayment, "Renewal"));
            }
        }

        private async Task CancelAsync()
        {
            if (IsBusy) return;

            var s = Selected;
            if (s?.SubscriptionId is null) { ShowError("Select a membership to cancel."); return; }

            if (UiKit.Confirm(
                    $"Cancel the membership for {s.FullName}?\r\n\r\n" +
                    "The subscription stops counting as active immediately. Payments already " +
                    "taken against it are kept.", "Cancel membership") != DialogResult.Yes) return;

            await GuardAsync(async () =>
            {
                var result = await Session.Subscriptions.CancelAsync(s.SubscriptionId.Value);
                if (!result.IsSuccess) { ShowError(result.ErrorMessage); return; }

                await LoadAsync();
                Notify("Membership cancelled successfully.");
            }, "Cancelling…");
        }

        private async Task ExpireOverdueAsync()
        {
            if (IsBusy) return;

            if (UiKit.Confirm(
                    "Expire every subscription whose end date has passed?\r\n\r\n" +
                    "This marks overdue memberships as Expired so the active count is correct. " +
                    "It does not delete anything.", "Expire overdue memberships")
                != DialogResult.Yes) return;

            await GuardAsync(async () =>
            {
                var result = await Session.Subscriptions.ExpireOverdueAsync();
                if (!result.IsSuccess) { ShowError(result.ErrorMessage); return; }

                await LoadAsync();
                Notify("Overdue memberships expired successfully.");
            }, "Updating memberships…");
        }
    }
}
