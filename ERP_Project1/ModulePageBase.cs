using System.Drawing;
using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>
    /// A screen that can be asked to start its own "create" flow from somewhere else - the
    /// dashboard's quick actions, for instance.
    /// </summary>
    internal interface IQuickAddPage
    {
        string QuickAddLabel { get; }
        Task QuickAddAsync();
    }

    /// <summary>
    /// The frame every module screen sits in: title, action toolbar, optional statistic row,
    /// filter bar, body and a status line, plus the shared loading, empty, error and success
    /// states so all of them behave identically.
    ///
    /// Screens derive from this and fill in <see cref="LoadAsync"/>. None of them talks to a
    /// database - they all go through <see cref="Session"/>, which is an HTTP client.
    /// </summary>
    internal abstract class ModulePageBase : UserControl
    {
        protected readonly FitCoreSession Session;

        protected DataGridView Grid = null!;
        protected FlowLayoutPanel Toolbar = null!;
        protected FlowLayoutPanel FilterBar = null!;
        protected FlowLayoutPanel StatsRow = null!;
        protected Label StatusLine = null!;

        private readonly string _title;
        private readonly string _subtitle;


        private Panel _header = null!;
        private Panel _titles = null!;

        private Panel _bodyHost = null!;
        private Panel _gridHost = null!;
        private Panel? _emptyState;
        private UiKit.BusyOverlay _busy = null!;
        private System.Windows.Forms.Timer? _successTimer;

        private int _busyDepth;

        protected ModulePageBase(FitCoreSession session, string title, string subtitle)
        {
            Session = session;
            _title = title;
            _subtitle = subtitle;

            Dock = DockStyle.Fill;
            BackColor = UiTheme.Canvas;
            Padding = new Padding(UiTheme.SpaceL, UiTheme.SpaceM, UiTheme.SpaceL, UiTheme.SpaceM);
            BuildFrame();
        }

        /// <summary>Set false by a screen that supplies its own body instead of a grid.</summary>
        protected virtual bool UsesGrid => true;

        /// <summary>
        /// Hides the page's own title and description when it is hosted inside a module
        /// workspace, where the shell's topbar already carries them - two headings stacked on
        /// one screen reads as a mistake.
        ///
        /// Only the wording is hidden. The action toolbar shares the header row and stays,
        /// because Add, Edit and Delete are the point of the screen.
        /// </summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool HeaderVisible
        {
            get => _titles.Visible;
            set
            {
                _titles.Visible = value;

                // Without the two lines of text the row only needs to be as tall as a button,
                // and a screen with no actions at all - the till, the dashboard - does not
                // need the row to exist.
                _header.Height = value ? 60 : Toolbar.Controls.Count > 0 ? 44 : 0;
                _header.Margin = new Padding(0, 0, 0, value ? UiTheme.SpaceM : UiTheme.SpaceS);
            }
        }

        public string PageTitle => _title;
        public string PageSubtitle => _subtitle;

        /// <summary>True while a server call is in flight, so actions can refuse to re-enter.</summary>
        protected bool IsBusy => _busyDepth > 0;

        /// <summary>Raised when a screen wants the shell to open another module.</summary>
        public event Action<string, string?>? NavigationRequested;

        protected void RequestNavigation(string moduleKey, string? tabKey = null) =>
            NavigationRequested?.Invoke(moduleKey, tabKey);

        private int _extraActions;

        /// <summary>
        /// Adds a screen-specific action to the left of the shared Delete / Edit / Refresh /
        /// Add group, in the order declared. Keeping the primary action rightmost on every
        /// screen is what makes the toolbars feel like one application.
        /// </summary>
        protected Button AddAction(string text, ButtonTone tone, Func<Task> run, int width = 104)
        {
            var button = UiKit.Action(text, tone, async (_, _) => await run(), width);

            Toolbar.Controls.Add(button);
            Toolbar.Controls.SetChildIndex(button, _extraActions++);

            return button;
        }

        public abstract Task LoadAsync();

        /// <summary>Replaces the grid with custom content, for screens like the dashboard or POS.</summary>
        protected void SetBody(Control body)
        {
            _gridHost.Controls.Clear();
            body.Dock = DockStyle.Fill;
            _gridHost.Controls.Add(body);
        }

        // ------------------------------------------------------------------ frame

        private void BuildFrame()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = UiTheme.Canvas
            };


            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // header
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // stats
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // filters
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // body + banners
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // status

            root.Controls.Add(BuildHeader(), 0, 0);

            StatsRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 2)
            };
            root.Controls.Add(StatsRow, 0, 1);

            FilterBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, UiTheme.SpaceS)
            };
            root.Controls.Add(FilterBar, 0, 2);

            root.Controls.Add(BuildBody(), 0, 3);

            StatusLine = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 34,
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.TextMuted,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(2, 0, 0, 0),
                UseMnemonic = false
            };
            root.Controls.Add(StatusLine, 0, 4);

            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            _header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                BackColor = UiTheme.Canvas,
                Margin = new Padding(0, 0, 0, UiTheme.SpaceM)
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UiTheme.Canvas
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var titles = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Canvas };
            _titles = titles;
            titles.Controls.Add(new Label
            {
                Text = _title,
                Font = UiTheme.PageTitle,
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                Location = new Point(0, 0),
                UseMnemonic = false
            });
            titles.Controls.Add(new Label
            {
                Text = _subtitle,
                Font = UiTheme.Small,
                ForeColor = UiTheme.TextMuted,
                AutoSize = true,
                Location = new Point(1, 28),
                UseMnemonic = false
            });

            Toolbar = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                Margin = new Padding(0, 8, 0, 0)
            };

            grid.Controls.Add(titles, 0, 0);
            grid.Controls.Add(Toolbar, 1, 0);
            _header.Controls.Add(grid);

            return _header;
        }

        private Control BuildBody()
        {
            _bodyHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Canvas };

            _gridHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Surface,
                Padding = new Padding(1)
            };
            _gridHost.Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, _gridHost.Width, _gridHost.Height),
                UiTheme.Surface, UiTheme.Border);

            if (UsesGrid)
            {
                Grid = UiKit.Grid();
                Grid.Margin = new Padding(1);
                _gridHost.Controls.Add(Grid);
            }

            // The overlay is positioned by hand rather than docked, so it covers the body
            // without taking part in the docking that sizes the grid.
            _busy = new UiKit.BusyOverlay { Dock = DockStyle.None };

            _bodyHost.Controls.Add(_gridHost);
            _bodyHost.Controls.Add(_busy);

            _bodyHost.Resize += (_, _) => _busy.Bounds = _bodyHost.ClientRectangle;

            return _bodyHost;
        }

        // ------------------------------------------------------------------ states

        // Feedback lives on the status strip under the body. It is one always-present
        // control rather than a pair of banners that appear and disappear, which is both
        // simpler and reliable: a docked panel that starts life hidden never reclaims its
        // space in the layout, so a banner built that way is laid out correctly and then
        // painted straight over by the grid filling the rest of the body.
        private string _restingStatus = "";

        /// <summary>
        /// The raw message behind the status strip's current red text, kept so a caller that
        /// wants to show the same failure somewhere more prominent - the empty-state-shaped
        /// "unable to load" panel, say - does not have to parse it back out of the decorated
        /// "⚠ " status line.
        /// </summary>
        protected string? LastErrorMessage { get; private set; }

        /// <summary>The ordinary record-count line. Remembered, so feedback can revert to it.</summary>
        protected void SetStatus(string text)
        {
            _restingStatus = text;

            if (_successTimer is null)
            {
                StatusLine.ForeColor = UiTheme.TextMuted;
                StatusLine.Text = text;
            }
        }

        /// <summary>
        /// Reports a problem the operator has to act on. Always a sentence, never a status
        /// code - by the time anything reaches here it has been through
        /// <see cref="ApiErrorText"/>.
        /// </summary>
        protected void ShowError(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                LastErrorMessage = null;

                if (_successTimer is null)
                {
                    StatusLine.ForeColor = UiTheme.TextMuted;
                    StatusLine.Text = _restingStatus;
                }
                return;
            }

            LastErrorMessage = message;
            StopFeedbackTimer();

            // A multi-line validation summary is longer than a status strip should carry, so
            // it goes to a dialog where it can be read properly.
            if (message.Contains('\n'))
            {
                StatusLine.ForeColor = UiTheme.Danger;
                StatusLine.Text = "⚠   The details supplied were not accepted.";
                UiKit.Error(message, "Please check these details");
                return;
            }

            StatusLine.ForeColor = UiTheme.Danger;
            StatusLine.Text = "⚠   " + message;
        }

        /// <summary>
        /// Confirms a completed operation, then fades back to the record count. Used for
        /// things the operator can see the result of; a modal is reserved for what they
        /// cannot, such as a completed sale.
        /// </summary>
        protected void Notify(string message)
        {
            StopFeedbackTimer();

            StatusLine.ForeColor = UiTheme.Success;
            StatusLine.Text = "✓   " + message;

            _successTimer = new System.Windows.Forms.Timer { Interval = 8000 };
            _successTimer.Tick += (_, _) =>
            {
                StopFeedbackTimer();

                if (IsDisposed) return;
                StatusLine.ForeColor = UiTheme.TextMuted;
                StatusLine.Text = _restingStatus;
            };
            _successTimer.Start();
        }

        private void StopFeedbackTimer()
        {
            _successTimer?.Stop();
            _successTimer?.Dispose();
            _successTimer = null;
        }

        /// <summary>Replaces the grid with an invitation when there is genuinely nothing to show.</summary>
        protected void ShowEmptyState(string headline, string detail,
                                      string? actionText = null, EventHandler? action = null,
                                      Color? headlineColor = null)
        {
            HideEmptyState();

            _emptyState = UiKit.EmptyState(headline, detail, actionText, action, headlineColor);
            _gridHost.Controls.Add(_emptyState);
            _emptyState.BringToFront();
        }

        /// <summary>
        /// The same panel <see cref="ShowEmptyState"/> shows for "nothing here yet", but for
        /// "the list could not be read" instead - headline in the danger colour, and the action
        /// is a retry rather than an invitation to create the first record. Reserved for the
        /// screen's own initial load failing; a failed Edit or Delete on a page that already
        /// has valid rows on screen keeps the ordinary status-strip banner instead, since
        /// blanking a grid that still holds good data would be a regression, not a clarification.
        /// </summary>
        protected void ShowErrorState(string headline, string detail, EventHandler onRetry) =>
            ShowEmptyState(headline, detail, "Retry", onRetry, UiTheme.Danger);

        protected void HideEmptyState()
        {
            if (_emptyState is null) return;

            _gridHost.Controls.Remove(_emptyState);
            _emptyState.Dispose();
            _emptyState = null;
        }

        // ------------------------------------------------------------------ server calls

        /// <summary>
        /// Runs server work behind a loading overlay, surfacing anything that goes wrong in
        /// the banner rather than throwing into the message loop and killing the application.
        ///
        /// A nested call - the reload an action runs after it succeeds - is executed inline:
        /// the outer call already owns the overlay and the banner, and restarting either
        /// would flicker. Guarding against a double-clicked button is the job of the
        /// <see cref="IsBusy"/> check at each user-facing entry point, backed by the disabled
        /// toolbar and the overlay swallowing clicks.
        /// </summary>
        protected async Task GuardAsync(Func<Task> work, string busyCaption = "Loading…")
        {
            if (_busyDepth > 0)
            {
                await work();
                return;
            }

            _busyDepth++;

            try
            {
                ShowError(null);
                _busy.Start(busyCaption);
                SetToolbarEnabled(false);

                await work();
            }
            catch (Exception ex)
            {
                // Anything that reaches here is a defect rather than a server answer, so the
                // operator gets a plain sentence and never a stack trace.
                ShowError(ApiErrorText.IsUsable(ex.Message)
                    ? ex.Message
                    : "Something went wrong in FitCore. Please try that again.");
                SetStatus("Could not complete.");
            }
            finally
            {
                _busyDepth--;

                if (_busyDepth == 0 && !IsDisposed)
                {
                    _busy.Stop();
                    SetToolbarEnabled(true);
                }
            }
        }

        /// <summary>
        /// Called while the page is busy so toolbars grey out. Overridden by screens that
        /// own buttons outside the toolbar.
        /// </summary>
        protected virtual void SetToolbarEnabled(bool enabled)
        {
            foreach (Control control in Toolbar.Controls)
            {
                control.Enabled = enabled;
            }
        }

        /// <summary>
        /// Unwraps an <see cref="ApiResult{T}"/>: returns the value, or shows the server's
        /// message and returns default. Keeps every call site to three lines.
        /// </summary>
        protected T? Unwrap<T>(ApiResult<T> result)
        {
            if (result.IsSuccess) return result.Value;
            ShowError(result.ErrorMessage);
            return default;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _successTimer?.Stop();
                _successTimer?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
