using System.ComponentModel;
using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// A list-and-maintain screen: search, grid, and Add / Edit / Delete over one record type.
    ///
    /// Derived screens describe their columns and supply the four server calls; everything
    /// else - filtering, selection, refresh, empty state, confirmations, success messages,
    /// error surfacing, enabling buttons - is handled here so each module stays short and
    /// they all behave identically.
    /// </summary>
    internal abstract class CrudPageBase<T> : ModulePageBase, IQuickAddPage where T : class
    {
        protected List<T> Items = new();
        protected TextBox Search = null!;

        private readonly Button _add;
        private readonly Button _edit;
        private readonly Button _delete;
        private readonly Button _refresh;
        private readonly string _noun;

        private bool _columnsReady;

        protected CrudPageBase(FitCoreSession session, string title, string subtitle,
                               string noun, string searchPlaceholder)
            : base(session, title, subtitle)
        {
            _noun = noun;

            _delete = UiKit.Action("Delete", ButtonTone.Secondary, async (_, _) => await DeleteClicked(), 86);
            _edit = UiKit.Action("Edit", ButtonTone.Secondary, async (_, _) => await EditClicked(), 76);
            _refresh = UiKit.Action("Refresh", ButtonTone.Secondary, async (_, _) => await LoadAsync(), 90);
            _add = UiKit.Action($"+  Add {noun}", ButtonTone.Primary, async (_, _) => await AddClicked(), 146);

            if (SupportsDelete) Toolbar.Controls.Add(_delete);
            if (SupportsEdit) Toolbar.Controls.Add(_edit);
            Toolbar.Controls.Add(_refresh);
            if (SupportsAdd) Toolbar.Controls.Add(_add);

            var searchField = UiKit.SearchField(out var box, searchPlaceholder, 300);
            Search = box;
            Search.TextChanged += (_, _) => ApplyFilter();
            FilterBar.Controls.Add(searchField);

            Grid.SelectionChanged += (_, _) => UpdateButtons();
            Grid.CellDoubleClick += async (_, e) =>
            {
                if (e.RowIndex >= 0 && SupportsEdit) await EditClicked();
            };
        }

        protected virtual bool SupportsAdd => true;
        protected virtual bool SupportsEdit => true;
        protected virtual bool SupportsDelete => true;

        /// <summary>The record type as it appears in messages, e.g. "member".</summary>
        protected string Noun => _noun;

        protected static string Sentence(string noun) =>
            string.IsNullOrEmpty(noun) ? noun : char.ToUpperInvariant(noun[0]) + noun[1..];

        protected virtual string CreatedMessage => $"{Sentence(_noun)} created successfully.";
        protected virtual string UpdatedMessage => $"{Sentence(_noun)} updated successfully.";
        protected virtual string DeletedMessage => $"{Sentence(_noun)} deleted successfully.";

        /// <summary>What the operator loses by deleting. Shown in the confirmation.</summary>
        protected virtual string DeleteConsequence =>
            "This cannot be undone.";

        protected virtual string EmptyHeadline => $"No {_noun}s yet";
        protected virtual string EmptyDetail =>
            $"Once you add a {_noun} it will appear here.";

        string IQuickAddPage.QuickAddLabel => $"Add {_noun}";
        Task IQuickAddPage.QuickAddAsync() => AddClicked();

        protected abstract Task<List<T>?> FetchAsync();
        protected abstract void DefineColumns();
        protected abstract bool Matches(T item, string term);

        /// <summary>Returns true when a record was actually saved, so a message can be shown.</summary>
        protected virtual Task<bool> OnAddAsync() => Task.FromResult(false);
        protected virtual Task<bool> OnEditAsync(T item) => Task.FromResult(false);

        protected virtual Task<string?> OnDeleteAsync(T item) => Task.FromResult<string?>("Not supported.");
        protected virtual string DescribeForDelete(T item) => $"this {_noun}";

        /// <summary>Extra work after the list loads - statistic cards, for example.</summary>
        protected virtual void AfterLoad() { }

        protected T? Selected => Grid.CurrentRow?.DataBoundItem as T;

        public override async Task LoadAsync()
        {
            await GuardAsync(async () =>
            {
                var data = await FetchAsync();
                if (data is null) return;

                Items = data;

                if (!_columnsReady)
                {
                    Grid.AutoGenerateColumns = false;
                    DefineColumns();
                    _columnsReady = true;
                }

                ApplyFilter();
                AfterLoad();
            }, $"Loading {_noun}s…");
        }

        /// <summary>Re-reads the list without the full loading treatment, after a write.</summary>
        private async Task ReloadQuietlyAsync()
        {
            var data = await FetchAsync();
            if (data is null) return;

            Items = data;
            ApplyFilter();
            AfterLoad();
        }

        protected void ApplyFilter()
        {
            var term = Search.Text.Trim();

            var view = string.IsNullOrWhiteSpace(term)
                ? Items
                : Items.Where(i => Matches(i, term)).ToList();

            // Keeping the selected row across a refresh means an operator who edits a record
            // is still standing on it afterwards.
            var previous = Selected;

            Grid.DataSource = new BindingList<T>(view);

            if (previous is not null)
            {
                for (var i = 0; i < Grid.Rows.Count; i++)
                {
                    if (!ReferenceEquals(Grid.Rows[i].DataBoundItem, previous)) continue;
                    Grid.CurrentCell = Grid.Rows[i].Cells[0];
                    break;
                }
            }

            if (Items.Count == 0)
            {
                ShowEmptyState(EmptyHeadline, EmptyDetail,
                    SupportsAdd ? $"Add {_noun}" : null,
                    SupportsAdd ? async (_, _) => await AddClicked() : null);

                SetStatus($"No {_noun}s");
            }
            else if (view.Count == 0)
            {
                ShowEmptyState("No matches",
                    $"No {_noun} matches “{term}”. Try a shorter search, or clear it to see all " +
                    $"{Items.Count:N0}.",
                    "Clear search", (_, _) => Search.Clear());

                SetStatus($"0 of {Items.Count:N0} {_noun}(s)");
            }
            else
            {
                HideEmptyState();

                SetStatus(view.Count == Items.Count
                    ? $"{UiKit.Plural(Items.Count, _noun)}"
                    : $"Showing {view.Count:N0} of {Items.Count:N0} {_noun}(s)");
            }

            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var has = Selected is not null;
            if (SupportsEdit) _edit.Enabled = has && !IsBusy;
            if (SupportsDelete) _delete.Enabled = has && !IsBusy;
        }

        protected override void SetToolbarEnabled(bool enabled)
        {
            base.SetToolbarEnabled(enabled);

            // Edit and Delete additionally need something selected, so they are not simply
            // re-enabled along with the rest.
            if (enabled) UpdateButtons();
        }

        private async Task AddClicked()
        {
            if (IsBusy) return;

            var saved = await OnAddAsync();
            if (!saved) return;

            await GuardAsync(async () =>
            {
                await ReloadQuietlyAsync();
                Notify(CreatedMessage);
            }, "Refreshing…");
        }

        private async Task EditClicked()
        {
            if (IsBusy) return;

            var item = Selected;
            if (item is null)
            {
                ShowError($"Select a {_noun} to edit.");
                return;
            }

            var saved = await OnEditAsync(item);
            if (!saved) return;

            await GuardAsync(async () =>
            {
                await ReloadQuietlyAsync();
                Notify(UpdatedMessage);
            }, "Refreshing…");
        }

        private async Task DeleteClicked()
        {
            if (IsBusy) return;

            var item = Selected;
            if (item is null)
            {
                ShowError($"Select a {_noun} to delete.");
                return;
            }

            if (!UiKit.ConfirmDelete(this,
                    $"Are you sure you want to delete {DescribeForDelete(item)}?",
                    DeleteConsequence))
            {
                return;
            }

            await GuardAsync(async () =>
            {
                var problem = await OnDeleteAsync(item);

                if (problem is not null)
                {
                    // The server refuses deletes that would destroy history - a member with
                    // payments, an employee with a pay run - and says why. Surface that
                    // verbatim rather than a generic failure.
                    ShowError(problem);
                    return;
                }

                await ReloadQuietlyAsync();
                Notify(DeletedMessage);
            }, "Deleting…");
        }

        // ---- column helpers -------------------------------------------------------

        protected void Column(string property, string header, int fill = 100,
                              string? format = null, bool rightAlign = false)
        {
            var col = new DataGridViewTextBoxColumn
            {
                Name = property,
                DataPropertyName = property,
                HeaderText = header,
                FillWeight = fill,
                SortMode = DataGridViewColumnSortMode.Automatic
            };

            if (format is not null) col.DefaultCellStyle.Format = format;
            if (rightAlign) col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            Grid.Columns.Add(col);
        }

        protected void MoneyColumn(string property, string header, int fill = 80) =>
            Column(property, header, fill, "N2", rightAlign: true);

        protected void DateColumn(string property, string header, int fill = 80) =>
            Column(property, header, fill, "d MMM yyyy");

        /// <summary>A column whose values are drawn as coloured status pills.</summary>
        protected void StatusColumn(string property, string header, int fill = 80)
        {
            Column(property, header, fill);
            UiKit.PaintStatusColumns(Grid, property);
        }

        /// <summary>A yes/no column drawn as a pill rather than "True"/"False".</summary>
        protected void FlagColumn(string property, string header, int fill = 55)
        {
            var col = new DataGridViewTextBoxColumn
            {
                Name = property,
                DataPropertyName = property,
                HeaderText = header,
                FillWeight = fill
            };
            Grid.Columns.Add(col);

            Grid.CellFormatting += (_, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                if (Grid.Columns[e.ColumnIndex].Name != property) return;

                if (e.Value is bool flag)
                {
                    // FormattingApplied stops the grid trying to convert the replacement
                    // string back through the column's bool value type.
                    e.Value = flag ? "Active" : "Inactive";
                    e.FormattingApplied = true;
                }
            };

            UiKit.PaintStatusColumns(Grid, property);
        }
    }
}
