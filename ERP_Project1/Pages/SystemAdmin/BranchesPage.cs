using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// The company's branch network, under System Administration.
    ///
    /// Drawn only for the Admin/Owner of a Medium tenant, because that is the only account the
    /// server lets through <c>systemadmin.branches</c>. Everything here is refused independently
    /// by the API, so hiding the tab is a courtesy rather than the boundary.
    /// </summary>
    internal sealed class BranchesPage : CrudPageBase<BranchDto>
    {
        private readonly Label _total;
        private readonly Label _totalHint;
        private readonly Label _open;
        private readonly Label _openHint;
        private readonly Label _staff;
        private readonly Label _staffHint;
        private readonly Label _members;
        private readonly Label _membersHint;

        public BranchesPage(FitCoreSession session)
            : base(session, "Branches",
                   "The sites this company runs. Every transaction belongs to one of them.",
                   "branch", "Code, name, address or phone")
        {
            AddAction("Make primary", ButtonTone.Secondary, MakePrimaryAsync, 118);
            AddAction("Transfer records", ButtonTone.Secondary, TransferAsync, 134);

            StatsRow.Controls.Add(UiKit.StatCard("Branches", out _total, out _totalHint, UiTheme.Primary));
            StatsRow.Controls.Add(UiKit.StatCard("Open", out _open, out _openHint, UiTheme.Success));
            StatsRow.Controls.Add(UiKit.StatCard("Staff", out _staff, out _staffHint, UiTheme.Info));
            StatsRow.Controls.Add(UiKit.StatCard("Members", out _members, out _membersHint, UiTheme.Warning));
        }

        protected override string DeleteConsequence =>
            "The branch is removed. This is refused as soon as anything has happened there - " +
            "close the branch instead, which keeps all of its history and its figures.";

        protected override string EmptyHeadline => "No branches yet";
        protected override string EmptyDetail =>
            "Add a branch for each site this company runs. The first one becomes the primary " +
            "branch, and every existing record is assigned to it.";

        protected override async Task<List<BranchDto>?> FetchAsync()
        {
            var branches = Unwrap(await Session.Branches.GetAllAsync());

            if (branches is not null)
            {
                _total.Text = branches.Count.ToString("N0");
                _totalHint.Text = "in this company";

                _open.Text = branches.Count(b => b.IsActive).ToString("N0");
                _openHint.Text = "trading now";

                _staff.Text = branches.Sum(b => b.EmployeeCount).ToString("N0");
                _staffHint.Text = "across all branches";

                _members.Text = branches.Sum(b => b.MemberCount).ToString("N0");
                _membersHint.Text = "assigned to a branch";
            }

            return branches;
        }

        protected override void DefineColumns()
        {
            Column(nameof(BranchDto.Code), "Code", 70);
            Column(nameof(BranchDto.Name), "Branch", 180);
            Column(nameof(BranchDto.Address), "Address", 180);
            Column(nameof(BranchDto.Phone), "Phone", 100);
            Column(nameof(BranchDto.EmployeeCount), "Staff", 60);
            Column(nameof(BranchDto.MemberCount), "Members", 70);
            StatusColumn(nameof(BranchDto.StatusText), "Status", 80);
        }

        protected override bool Matches(BranchDto b, string term) =>
            b.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            b.Code.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (b.Address ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (b.Phone ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(BranchDto b) => $"“{b.Name}” ({b.Code})";

        private static List<FieldSpec> Fields(BranchDto? b) => new()
        {
            new("code", "Branch code")
                { Required = true, Value = b?.Code, MaxLength = 20,
                  Hint = "Short and unique, e.g. BRA. Shown in the branch picker." },
            new("name", "Branch name") { Required = true, Value = b?.Name, MaxLength = 150 },
            new("address", "Address", FieldKind.Multiline) { Value = b?.Address, MaxLength = 250 },
            new("phone", "Phone", FieldKind.Phone) { Value = b?.Phone, MaxLength = 40 },
            new("email", "Email", FieldKind.Email) { Value = b?.Email, MaxLength = 150 }
        };

        protected override Task<bool> OnAddAsync()
        {
            var saved = EditDialog.Run(this, "Add branch",
                "Records written while this branch is selected will belong to it. The first " +
                "branch a company creates becomes its primary one.",
                Fields(null), async f =>
                {
                    var result = await Session.Branches.CreateAsync(new CreateBranchDto
                    {
                        Code = f.First(x => x.Key == "code").Text,
                        Name = f.First(x => x.Key == "name").Text,
                        Address = f.First(x => x.Key == "address").Text,
                        Phone = f.First(x => x.Key == "phone").Text,
                        Email = f.First(x => x.Key == "email").Text
                    });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Create branch");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(BranchDto b)
        {
            var fields = Fields(b);

            fields.Add(new FieldSpec("active", "Open", FieldKind.Check)
            {
                Value = b.IsActive,
                Hint = "A closed branch keeps its history and still appears in reports, but " +
                       "takes no new records."
            });

            var saved = EditDialog.Run(this, $"Edit {b.Name}", "", fields, async f =>
            {
                var result = await Session.Branches.UpdateAsync(b.BranchId, new UpdateBranchDto
                {
                    Code = f.First(x => x.Key == "code").Text,
                    Name = f.First(x => x.Key == "name").Text,
                    Address = f.First(x => x.Key == "address").Text,
                    Phone = f.First(x => x.Key == "phone").Text,
                    Email = f.First(x => x.Key == "email").Text,
                    IsActive = f.First(x => x.Key == "active").Flag
                });

                return result.IsSuccess ? null : result.ErrorMessage;
            }, "Save changes");

            return Task.FromResult(saved);
        }

        protected override async Task<string?> OnDeleteAsync(BranchDto b)
        {
            var result = await Session.Branches.DeleteAsync(b.BranchId);
            return result.IsSuccess ? null : result.ErrorMessage;
        }

        private async Task MakePrimaryAsync()
        {
            if (IsBusy) return;

            var branch = Selected;
            if (branch is null)
            {
                ShowError("Select a branch to make primary.");
                return;
            }

            if (branch.IsPrimary)
            {
                ShowError($"{branch.Name} is already the primary branch.");
                return;
            }

            if (!UiKit.ConfirmDelete(this,
                    $"Make “{branch.Name}” the primary branch?",
                    "A record written while no branch is selected will belong to it from now on. " +
                    "Nothing already recorded moves.",
                    "Make primary"))
            {
                return;
            }

            await GuardAsync(async () =>
            {
                var result = await Session.Branches.SetPrimaryAsync(branch.BranchId);

                if (!result.IsSuccess)
                {
                    ShowError(result.ErrorMessage);
                    return;
                }

                await LoadAsync();
                Notify($"{branch.Name} is now the primary branch.");
            }, "Updating…");
        }

        /// <summary>
        /// Moves people between branches. Transactions are deliberately not offered: a sale, a
        /// payment or a pay run says what happened at one site on one day, and moving it would
        /// rewrite both branches' revenue after the fact. The server refuses it too.
        /// </summary>
        private async Task TransferAsync()
        {
            if (IsBusy) return;

            var source = Selected;
            if (source is null)
            {
                ShowError("Select the branch to transfer records out of.");
                return;
            }

            var branches = Unwrap(await Session.Branches.GetAllAsync());

            var destinations = branches?
                .Where(b => b.IsActive && b.BranchId != source.BranchId)
                .ToList() ?? new List<BranchDto>();

            if (destinations.Count == 0)
            {
                ShowError("There is no other open branch to transfer to.");
                return;
            }

            var fields = new List<FieldSpec>
            {
                new("to", "Transfer to", FieldKind.Combo)
                {
                    Required = true,
                    Value = destinations[0].BranchId.ToString(),
                    Options = destinations
                        .Select(b => new KeyValuePair<string, string>(
                            b.BranchId.ToString(), $"{b.Code} - {b.Name}"))
                        .ToList()
                },
                new("scope", "What to move", FieldKind.Combo)
                {
                    Required = true,
                    Value = ((int)BranchTransferScope.AllStandingRecords).ToString(),
                    Options = new List<KeyValuePair<string, string>>
                    {
                        new(((int)BranchTransferScope.AllStandingRecords).ToString(),
                            "Members, employees and customers"),
                        new(((int)BranchTransferScope.Members).ToString(), "Members only"),
                        new(((int)BranchTransferScope.Employees).ToString(), "Employees only"),
                        new(((int)BranchTransferScope.Customers).ToString(), "Customers only")
                    },
                    Hint = "Sales, payments, pay runs and expenses stay where they happened - " +
                           "moving one would change both branches' recorded revenue."
                }
            };

            var saved = EditDialog.Run(this, $"Transfer from {source.Name}",
                $"Everything selected moves out of {source.Name} and into the branch you " +
                "choose. Their history travels with them.",
                fields, async f =>
                {
                    if (!int.TryParse(f.First(x => x.Key == "to").Text, out var toBranchId))
                    {
                        return "Choose a branch to transfer to.";
                    }

                    var scope = (BranchTransferScope)f.First(x => x.Key == "scope").Int;

                    var result = await Session.Branches.TransferAsync(new BranchTransferDto
                    {
                        FromBranchId = source.BranchId,
                        ToBranchId = toBranchId,
                        Scope = scope
                    });

                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Transfer");

            if (!saved) return;

            await GuardAsync(async () =>
            {
                await LoadAsync();
                Notify($"Records transferred out of {source.Name} successfully.");
            }, "Refreshing…");
        }
    }
}
