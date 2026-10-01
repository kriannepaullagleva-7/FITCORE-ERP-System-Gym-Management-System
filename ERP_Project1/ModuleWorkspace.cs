using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>One submodule inside a module: the tab label and the screen behind it.</summary>
    internal sealed record WorkspaceTab(string Key, string Label, Func<ModulePageBase> Create);

    /// <summary>
    /// A module and its submodules on one screen.
    ///
    /// The sidebar lists modules - Membership, Sales, Inventory - and each of those opens one
    /// of these: the shell draws the module heading, and the submodules sit on a tab strip
    /// directly beneath it. That keeps the sidebar to the six things a gym actually does, rather than
    /// the fourteen screens it takes to do them.
    ///
    /// Pages are built the first time their tab is opened and kept afterwards, so switching
    /// back is instant and does not re-query the server for data that is seconds old.
    /// </summary>
    internal sealed class ModuleWorkspace : UserControl
    {
        /// <summary>How long a tab's data stays fresh before reopening it refetches.</summary>
        private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);

        private readonly WorkspaceTab[] _tabs;
        private readonly Dictionary<string, ModulePageBase> _pages = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _loadedAt = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Button> _tabButtons = new(StringComparer.OrdinalIgnoreCase);
        private readonly Panel _content;
        private readonly Panel _tabStrip;

        private string? _activeKey;

        public ModuleWorkspace(WorkspaceTab[] tabs)
        {
            _tabs = tabs;

            Dock = DockStyle.Fill;
            BackColor = UiTheme.Canvas;
            Padding = new Padding(UiTheme.SpaceL, UiTheme.SpaceM, UiTheme.SpaceL, 0);

            _content = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Canvas };
            _tabStrip = BuildTabStrip();

            // The module heading and its one-line description are drawn once, by the shell's
            // topbar. Repeating them here put the same two lines on screen twice.
            Controls.Add(_content);
            Controls.Add(_tabStrip);

            // A module with a single screen does not need a tab strip; the topbar heading
            // already says what the operator is looking at.
            _tabStrip.Visible = tabs.Length > 1;
        }

        /// <summary>Raised when a screen inside asks the shell to open another module.</summary>
        public event Action<string, string?>? NavigationRequested;

        /// <summary>
        /// Raised when any tab in this workspace wrote something. The shell listens so the
        /// other modules' workspaces drop their caches too - a sale moves stock, the ledger and
        /// every dashboard that counts it, none of which live in the Sales workspace.
        /// </summary>
        public event Action? DataChanged;

        /// <summary>Drops every cached load stamp, so each tab re-reads when next opened.</summary>
        public void InvalidateCaches() => _loadedAt.Clear();

        public ModulePageBase? ActivePage =>
            _activeKey is not null && _pages.TryGetValue(_activeKey, out var page) ? page : null;

        // ------------------------------------------------------------------ chrome


        private Panel BuildTabStrip()
        {
            var strip = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = UiTheme.Canvas
            };

            // A hairline under the whole strip, with the active tab's own underline drawn on
            // top of it, is what makes these read as tabs rather than as buttons.
            strip.Paint += (_, e) =>
            {
                using var pen = new Pen(UiTheme.Border);
                e.Graphics.DrawLine(pen, 0, strip.Height - 1, strip.Width, strip.Height - 1);
            };

            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.Canvas,
                Padding = new Padding(0, 6, 0, 0)
            };

            foreach (var tab in _tabs)
            {
                var width = Math.Max(104, TextRenderer.MeasureText(tab.Label, UiTheme.BodyStrong).Width + 34);

                var button = new Button
                {
                    Text = tab.Label,
                    Tag = tab.Key,
                    AutoSize = false,
                    Width = width,
                    Height = 36,
                    FlatStyle = FlatStyle.Flat,
                    Font = UiTheme.Body,
                    ForeColor = UiTheme.TextSecondary,
                    BackColor = UiTheme.Canvas,
                    Cursor = Cursors.Hand,
                    Margin = new Padding(0, 0, 4, 0),
                    TabStop = true,

                    // A tab is a caption, not a menu command: without this "Subscriptions &
                    // Tiers" loses its ampersand to the accelerator prefix and renders as
                    // "Subscriptions  Tiers" with a stray underline under the T.
                    UseMnemonic = false
                };
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = UiTheme.PrimarySoft;
                button.FlatAppearance.MouseDownBackColor = UiTheme.PrimarySoft;

                var key = tab.Key;
                button.Paint += (_, e) =>
                {
                    if (!string.Equals(_activeKey, key, StringComparison.OrdinalIgnoreCase)) return;

                    using var brush = new SolidBrush(UiTheme.Primary);
                    e.Graphics.FillRectangle(brush, 0, button.Height - 3, button.Width, 3);
                };

                button.Click += async (_, _) => await ActivateAsync(key);

                _tabButtons[key] = button;
                row.Controls.Add(button);
            }

            strip.Controls.Add(row);
            return strip;
        }

        // ------------------------------------------------------------------ activation

        /// <summary>Opens the module at its first tab, or at <paramref name="tabKey"/> when given.</summary>
        public Task OpenAsync(string? tabKey = null)
        {
            var key = tabKey is not null && _tabs.Any(t =>
                          string.Equals(t.Key, tabKey, StringComparison.OrdinalIgnoreCase))
                ? tabKey
                : _tabs[0].Key;

            return ActivateAsync(key);
        }

        private async Task ActivateAsync(string key)
        {
            var tab = _tabs.FirstOrDefault(t =>
                string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

            if (tab is null) return;

            var alreadyOpen = string.Equals(_activeKey, tab.Key, StringComparison.OrdinalIgnoreCase);
            _activeKey = tab.Key;

            foreach (var button in _tabButtons.Values)
            {
                var isActive = string.Equals((string)button.Tag!, tab.Key, StringComparison.OrdinalIgnoreCase);
                button.ForeColor = isActive ? UiTheme.Primary : UiTheme.TextSecondary;
                button.Font = isActive ? UiTheme.BodyStrong : UiTheme.Body;
                button.Invalidate();
            }

            var isNew = !_pages.TryGetValue(tab.Key, out var page);

            if (isNew)
            {
                page = tab.Create();

                // The shell topbar already carries the module heading, so the page hides its own.
                page.HeaderVisible = false;
                page.Padding = new Padding(0, UiTheme.SpaceM, 0, UiTheme.SpaceS);
                page.NavigationRequested += (m, t) => NavigationRequested?.Invoke(m, t);

                // A write on one tab makes every sibling's cached rows a lie: a membership sold
                // on Subscriptions belongs in History, a sale rung up at the till belongs in
                // Sales History. Dropping their load stamps is enough - the next activation sees
                // no stamp, treats them as stale and re-reads. The tab that did the writing has
                // already refreshed itself, so it is deliberately left alone.
                var writingTab = tab.Key;
                page.DataChanged += () =>
                {
                    foreach (var key in _loadedAt.Keys.ToList())
                    {
                        if (!string.Equals(key, writingTab, StringComparison.OrdinalIgnoreCase))
                        {
                            _loadedAt.Remove(key);
                        }
                    }

                    DataChanged?.Invoke();
                };

                _pages[tab.Key] = page;
            }

            _content.SuspendLayout();
            foreach (Control child in _content.Controls) child.Visible = false;

            if (!_content.Controls.Contains(page!)) _content.Controls.Add(page!);

            page!.Visible = true;
            page.BringToFront();
            _content.ResumeLayout();

            if (alreadyOpen && !isNew) return;

            var stale = !_loadedAt.TryGetValue(tab.Key, out var when) ||
                        DateTime.UtcNow - when > StaleAfter;

            if (isNew || stale)
            {
                _loadedAt[tab.Key] = DateTime.UtcNow;
                await page.LoadAsync();
            }
        }

        /// <summary>Forces the visible tab to re-read from the server.</summary>
        public async Task RefreshActiveAsync()
        {
            if (ActivePage is not { } page || _activeKey is null) return;

            _loadedAt[_activeKey] = DateTime.UtcNow;
            await page.LoadAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var page in _pages.Values) page.Dispose();
                _pages.Clear();
            }

            base.Dispose(disposing);
        }
    }
}
