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
            AddAction("Restore", ButtonTone.Secondary, () => RestoreAsync(), 90);
            AddAction("Archive", ButtonTone.Secondary, () => ArchiveAsync(), 90);
        }

        protected override string DeleteConsequence =>
            "A member who has ever paid, subscribed or bought something cannot be deleted, " +
            "because that would take their financial history with them. Archive them instead " +
            "to retire them while keeping the records.";

        protected override string EmptyHeadline => "No members yet";
        protected override string EmptyDetail =>
            "Add your first member to start selling subscriptions, recording payments and " +
            "ringing up sales against them.";

        protected override async Task<List<MemberDto>?> FetchAsync() =>
            Unwrap(await Session.Members.GetAllAsync());

        protected override void DefineColumns()
        {
            Column(nameof(MemberDto.MemberId), "ID", 40);
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
            new("phone", "Phone") { Value = m?.Phone, MaxLength = 20 },
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
            // anything, because that would take the gym's takings with them. Point the
            // operator at the alternative it offers instead.
            return result.IsSuccess
                ? null
                : (result.ErrorMessage ?? "This member could not be deleted.") +
                  "\r\nUse Archive instead to retire them while keeping their history.";
        }

        private async Task ArchiveAsync()
        {
            if (IsBusy) return;

            var m = Selected;
            if (m is null) { ShowError("Select a member to archive."); return; }

            if (UiKit.Confirm(
                    $"Archive {m.FirstName} {m.LastName}?\r\n\r\n" +
                    "They stop appearing as an active member, but every subscription, payment " +
                    "and sale of theirs is kept and still counts in reports. You can restore " +
                    "them at any time.", "Archive member") != DialogResult.Yes) return;

            await GuardAsync(async () =>
            {
                var result = await Session.Members.ArchiveAsync(m.MemberId);
                if (!result.IsSuccess) { ShowError(result.ErrorMessage); return; }

                await LoadAsync();
                Notify($"{m.FirstName} {m.LastName} archived successfully.");
            }, "Archiving…");
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
            Column(nameof(MembershipPlanDto.PlanId), "ID", 40);
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

        protected override Task<bool> OnAddAsync()
        {
            if (_members.Count == 0)
            {
                ShowError("Add a member before selling a subscription.");
                return Task.FromResult(false);
            }

            if (_plans.Count == 0)
            {
                ShowError("Create an active membership plan before selling a subscription.");
                return Task.FromResult(false);
            }

            var fields = new List<FieldSpec>
            {
                new("member", "Member", FieldKind.Combo)
                {
                    Required = true,
                    Options = _members
                        .Select(m => new KeyValuePair<string, string>(
                            m.MemberId.ToString(), $"{m.FirstName} {m.LastName} (#{m.MemberId})"))
                        .ToList()
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

            var saved = EditDialog.Run(this, "New subscription",
                "The end date is calculated by the server from the plan's duration.",
                fields, async f =>
                {
                    var result = await Session.Subscriptions.CreateAsync(new CreateSubscriptionDto
                    {
                        MemberId = int.Parse(f.First(x => x.Key == "member").ComboValue ?? "0"),
                        PlanId = int.Parse(f.First(x => x.Key == "plan").ComboValue ?? "0"),
                        StartDate = f.First(x => x.Key == "start").Date
                    });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Create subscription");

            return Task.FromResult(saved);
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

            if (UiKit.Confirm(
                    $"Renew the membership for {s.FullName}?\r\n\r\n" +
                    "The new period is added onto the current expiry date when that is still " +
                    "in the future, so no paid-for days are lost.", "Renew membership")
                != DialogResult.Yes) return;

            await GuardAsync(async () =>
            {
                var result = await Session.Subscriptions.RenewAsync(s.SubscriptionId.Value);
                if (!result.IsSuccess) { ShowError(result.ErrorMessage); return; }

                await LoadAsync();

                Notify(result.Value is null
                    ? "Membership renewed successfully."
                    : $"Membership renewed successfully — now expires {UiKit.Date(result.Value.EndDate)}.");
            }, "Renewing…");
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
