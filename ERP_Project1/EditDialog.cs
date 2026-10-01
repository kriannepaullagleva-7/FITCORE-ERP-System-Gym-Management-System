using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ERP_Project1
{
    internal enum FieldKind { Text, Multiline, Number, Money, Integer, Date, Combo, Check, Password, Email, Phone }

    /// <summary>One editable field in an <see cref="EditDialog"/>.</summary>
    internal sealed class FieldSpec
    {
        public FieldSpec(string key, string label, FieldKind kind = FieldKind.Text)
        {
            Key = key;
            Label = label;
            Kind = kind;
        }

        public string Key { get; }
        public string Label { get; }
        public FieldKind Kind { get; }
        public bool Required { get; set; }
        public string? Hint { get; set; }
        public object? Value { get; set; }

        /// <summary>Lowest accepted value for a numeric field. Defaults to zero - prices,
        /// quantities and salaries are never negative in this application.</summary>
        public decimal? Minimum { get; set; }
        public decimal? Maximum { get; set; }

        /// <summary>Longest accepted text, mirroring the column width the API validates.</summary>
        public int? MaxLength { get; set; }

        /// <summary>
        /// Shown but not editable. Used where the server would refuse the change anyway - a
        /// salary field for a Manager, say - so the dialog shows what the value is without
        /// inviting an edit the API is only going to reject.
        /// </summary>
        public bool ReadOnly { get; set; }

        /// <summary>A field-specific rule. Returns null when the value is acceptable.</summary>
        public Func<FieldSpec, string?>? Validate { get; set; }

        /// <summary>Value/display pairs for <see cref="FieldKind.Combo"/>.</summary>
        public List<KeyValuePair<string, string>>? Options { get; set; }

        /// <summary>Raised when a combo selection changes, so dependent fields can react.</summary>
        public Action<FieldSpec>? OnChanged { get; set; }

        internal Control? Control { get; set; }

        /// <summary>The small red line under the field, shown when this field fails validation.</summary>
        internal Label? ErrorLabel { get; set; }

        public string Text => Kind switch
        {
            FieldKind.Combo => (Control as ComboBox)?.SelectedValue?.ToString() ?? "",
            FieldKind.Check => ((Control as CheckBox)?.Checked ?? false).ToString(),
            FieldKind.Date => ((Control as DateTimePicker)?.Value ?? DateTime.Today).ToString("o"),
            _ => Control?.Text?.Trim() ?? ""
        };

        public bool Flag => (Control as CheckBox)?.Checked ?? false;

        public DateTime Date => (Control as DateTimePicker)?.Value ?? DateTime.Today;

        public decimal Decimal =>
            decimal.TryParse(Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var d) ? d : 0m;

        public int Int => int.TryParse(Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var i) ? i : 0;

        public string? ComboValue
        {
            get
            {
                var v = (Control as ComboBox)?.SelectedValue?.ToString();
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }
        }

        public void SetOptions(List<KeyValuePair<string, string>> options)
        {
            Options = options;
            if (Control is ComboBox combo) combo.DataSource = options;
        }
    }

    /// <summary>
    /// A modal create/edit form built from field descriptors.
    ///
    /// Two things happen before anything is sent: required fields and formats are checked
    /// here, so obviously invalid data never reaches the API or the database; and the save
    /// callback returns null on success or a message to display, so server-side validation -
    /// a duplicate code, an overlapping payroll period - is shown in the dialog with the
    /// operator's typing preserved rather than the form closing over the error.
    /// </summary>
    internal sealed class EditDialog : Form
    {
        private static readonly Regex EmailPattern =
            new(@"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$", RegexOptions.Compiled);

        /// <summary>WinForms' own default, used where a field declares no limit of its own.</summary>
        private const int DefaultMaxLength = 32767;

        private readonly IList<FieldSpec> _fields;
        private readonly Func<IList<FieldSpec>, Task<string?>> _save;
        private readonly Panel _errorPanel;
        private readonly Label _error;
        private readonly Button _saveButton;
        private readonly string _saveText;
        private readonly Dictionary<FieldSpec, string> _originalValues;
        private bool _saving;

        private EditDialog(string title, string subtitle, IList<FieldSpec> fields,
                           Func<IList<FieldSpec>, Task<string?>> save, string saveText)
        {
            _fields = fields;
            _save = save;
            _saveText = saveText;

            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = UiTheme.Surface;
            Font = UiTheme.Body;
            Width = 560;

            const int fieldWidth = 488;

            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UiTheme.Surface
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(22, 18, 22, 8),
                BackColor = UiTheme.Surface
            };

            layout.Controls.Add(new Label
            {
                Text = title,
                Font = new Font(UiTheme.FamilySemibold, 13F),
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 2),
                UseMnemonic = false
            });

            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                layout.Controls.Add(new Label
                {
                    Text = subtitle,
                    Font = UiTheme.Small,
                    ForeColor = UiTheme.TextMuted,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 0, 12),
                    MaximumSize = new Size(fieldWidth, 0),
                    UseMnemonic = false
                });
            }

            _errorPanel = new Panel
            {
                Width = fieldWidth,
                Height = 42,
                Visible = false,
                BackColor = UiTheme.Surface,
                Padding = new Padding(12, 0, 12, 0),
                Margin = new Padding(0, 0, 0, 12)
            };
            _errorPanel.Paint += (_, e) => UiTheme.PaintCard(
                e.Graphics, new Rectangle(0, 0, _errorPanel.Width, _errorPanel.Height),
                UiTheme.DangerSoft, UiKit.Blend(UiTheme.Danger, Color.White, 0.6f), 6);

            _error = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.Danger,
                Font = UiTheme.Body,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false
            };
            _errorPanel.Controls.Add(_error);
            layout.Controls.Add(_errorPanel);

            foreach (var field in fields)
            {
                layout.Controls.Add(BuildLabel(field));

                var control = BuildControl(field, fieldWidth);
                field.Control = control;

                if (field.ReadOnly)
                {
                    // A ComboBox and CheckBox have no read-only mode of their own, so Enabled
                    // is used for those; a TextBox gets ReadOnly, which - unlike Enabled -
                    // still paints as ordinary text rather than the greyed-out disabled look.
                    switch (control)
                    {
                        case TextBox textBox: textBox.ReadOnly = true; break;
                        default: control.Enabled = false; break;
                    }
                }

                if (field.Kind == FieldKind.Password)
                {
                    layout.Controls.Add(BuildPasswordRow((TextBox)control, fieldWidth));
                }
                else if (field.Kind == FieldKind.Money)
                {
                    layout.Controls.Add(BuildMoneyRow((TextBox)control));
                }
                else
                {
                    layout.Controls.Add(control);
                }

                if (field.OnChanged is not null)
                {
                    // Combo fields react to a selection; text-entry fields (Money included)
                    // react to every keystroke, which is what lets a change-due figure update
                    // as the operator types the amount tendered rather than after they leave
                    // the field.
                    if (control is ComboBox combo)
                    {
                        combo.SelectedIndexChanged += (_, _) => field.OnChanged(field);
                    }
                    else if (control is TextBox textBox)
                    {
                        textBox.TextChanged += (_, _) => field.OnChanged(field);
                    }
                }

                // Hidden until Save finds this field invalid - see ShowFieldProblems. Always
                // present (rather than created on demand) so it already occupies its row and
                // showing it does not reshuffle every field below it by more than its own height.
                var errorLabel = new Label
                {
                    Text = "",
                    Font = UiTheme.Small,
                    ForeColor = UiTheme.Danger,
                    AutoSize = true,
                    MaximumSize = new Size(fieldWidth, 0),
                    Margin = new Padding(2, 3, 0, 0),
                    Visible = false,
                    UseMnemonic = false
                };
                field.ErrorLabel = errorLabel;
                layout.Controls.Add(errorLabel);

                if (!string.IsNullOrWhiteSpace(field.Hint))
                {
                    layout.Controls.Add(new Label
                    {
                        Text = field.Hint,
                        Font = new Font(UiTheme.Family, 8F),
                        ForeColor = UiTheme.TextMuted,
                        AutoSize = true,
                        MaximumSize = new Size(fieldWidth, 0),
                        Margin = new Padding(2, 3, 0, 12),
                        UseMnemonic = false
                    });
                }

                // Whatever landed last for this field - the hint if there was one, the error
                // label otherwise - carries the gap before the next field's label.
                var lastForField = layout.Controls[layout.Controls.Count - 1];
                lastForField.Margin = new Padding(lastForField.Margin.Left, lastForField.Margin.Top,
                                                  lastForField.Margin.Right, 12);
            }

            scroll.Controls.Add(layout);

            // Snapshotted once every control has its starting value, so Cancel and the window's
            // own close button can tell "nothing changed" from "there is typing to lose" without
            // any field kind needing to say so itself - FieldSpec.Text already normalises every
            // kind (combo, check, date, plain text) to one comparable string.
            _originalValues = fields.ToDictionary(f => f, f => f.Text);

            // ---- footer, always visible even when the field list scrolls ----
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 64,
                BackColor = UiTheme.SurfaceAlt
            };
            footer.Paint += (_, e) =>
            {
                using var pen = new Pen(UiTheme.Border);
                e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };

            // Sized to its label: several flows use a sentence rather than "Save", and a
            // fixed width silently clipped them to "Create and take".
            var saveWidth = Math.Max(160,
                TextRenderer.MeasureText(saveText, UiTheme.BodyStrong).Width + 36);

            _saveButton = UiKit.Action(saveText, ButtonTone.Primary,
                async (_, _) => await SaveAsync(), saveWidth);
            var cancel = UiKit.Action("Cancel", ButtonTone.Secondary,
                (_, _) => { DialogResult = DialogResult.Cancel; Close(); }, 104);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 14, 18, 0),
                BackColor = Color.Transparent
            };
            buttons.Controls.Add(_saveButton);
            buttons.Controls.Add(cancel);
            footer.Controls.Add(buttons);

            Controls.Add(scroll);
            Controls.Add(footer);

            AcceptButton = _saveButton;
            CancelButton = cancel;
            FormClosing += HandleClosing;

            // Focuses the first field an operator can actually type into, rather than leaving
            // the initial focus to whatever WinForms picks - normally the title bar's own
            // system controls, which is nowhere useful to start typing.
            Shown += (_, _) =>
            {
                foreach (var f in fields)
                {
                    if (f.ReadOnly || f.Control is not { Enabled: true }) continue;
                    f.Control.Focus();
                    break;
                }
            };

            // Size to the content, but never taller than the screen.
            var wanted = layout.PreferredSize.Height + footer.Height + 48;
            var maximum = Screen.PrimaryScreen is { } screen
                ? (int)(screen.WorkingArea.Height * 0.86)
                : 820;
            Height = Math.Min(wanted, maximum);
        }

        // ------------------------------------------------------------------ closing

        private bool IsDirty() => _fields.Any(f => f.Text != _originalValues[f]);

        /// <summary>
        /// Catches every way this dialog can close - the Cancel button, Escape (routed through
        /// it as <see cref="CancelButton"/>), and the window's own X - in one place, so "was
        /// anything actually typed" only has to be answered once. A successful Save already set
        /// <see cref="DialogResult.OK"/> before calling <see cref="Close"/>, so that path is
        /// let through without asking; a save still in flight refuses to close at all, so an
        /// impatient click cannot abandon the operation the server is midway through.
        /// </summary>
        private void HandleClosing(object? sender, FormClosingEventArgs e)
        {
            if (_saving)
            {
                e.Cancel = true;
                return;
            }

            if (DialogResult == DialogResult.OK || !IsDirty()) return;

            // Owned by this dialog rather than raised through UiKit.Confirm, so the prompt
            // centres on the form it is guarding instead of on the screen.
            if (ConfirmDialog.Show(this, "Discard unsaved changes?",
                    "Any changes you made will be lost.", "Discard changes", "Stay",
                    ButtonTone.Danger) != DialogResult.Yes)
            {
                e.Cancel = true;
            }
        }

        // ------------------------------------------------------------------ building

        /// <summary>
        /// A required field's label is followed by a "*" - always was - but painted in the same
        /// danger red used everywhere else in the app to mean "this needs attention", rather than
        /// the label's own colour, so "required" reads at a glance rather than only by noticing
        /// the character. Two labels rather than one, because a <see cref="Label"/> paints its
        /// whole <see cref="Control.Text"/> in one <see cref="Control.ForeColor"/> - there is no
        /// way to colour one character of it differently without owner-drawing the entire thing.
        /// </summary>
        private static Control BuildLabel(FieldSpec f)
        {
            var caption = new Label
            {
                Text = f.Label,
                Font = UiTheme.Label,
                ForeColor = UiTheme.TextPrimary,
                AutoSize = true,
                Margin = new Padding(0),
                UseMnemonic = false
            };

            if (!f.Required) return caption;

            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 4)
            };

            row.Controls.Add(caption);
            row.Controls.Add(new Label
            {
                Text = " *",
                Font = UiTheme.Label,
                ForeColor = UiTheme.Danger,
                AutoSize = true,
                Margin = new Padding(0),
                UseMnemonic = false
            });

            return row;
        }

        private static Control BuildControl(FieldSpec f, int width)
        {
            switch (f.Kind)
            {
                case FieldKind.Multiline:
                    return new TextBox
                    {
                        Text = f.Value?.ToString() ?? "",
                        Width = width,
                        Height = 66,
                        Multiline = true,
                        ScrollBars = ScrollBars.Vertical,
                        Font = UiTheme.Body,
                        BorderStyle = BorderStyle.FixedSingle,
                        MaxLength = f.MaxLength ?? DefaultMaxLength,
                        Margin = new Padding(0)
                    };

                case FieldKind.Check:
                    return new CheckBox
                    {
                        Text = "Yes",
                        Checked = f.Value is bool b && b,
                        AutoSize = true,
                        Font = UiTheme.Body,
                        ForeColor = UiTheme.TextPrimary,
                        Margin = new Padding(0, 2, 0, 0)
                    };

                case FieldKind.Date:
                    return new DateTimePicker
                    {
                        Format = DateTimePickerFormat.Long,
                        Value = f.Value is DateTime d ? d : DateTime.Today,
                        Width = 260,
                        Font = UiTheme.Body,
                        Margin = new Padding(0)
                    };

                case FieldKind.Combo:
                    var combo = new ComboBox
                    {
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        Width = width,
                        Font = UiTheme.Body,
                        FlatStyle = FlatStyle.Flat,
                        BackColor = UiTheme.Surface,
                        Margin = new Padding(0),
                        DisplayMember = "Value",
                        ValueMember = "Key"
                    };
                    combo.DataSource = f.Options ?? new List<KeyValuePair<string, string>>();

                    var initial = f.Value?.ToString();
                    if (!string.IsNullOrEmpty(initial)) combo.SelectedValue = initial;
                    return combo;

                case FieldKind.Password:
                    return new TextBox
                    {
                        Text = f.Value?.ToString() ?? "",
                        Width = width - 84,
                        Font = UiTheme.Body,
                        BorderStyle = BorderStyle.FixedSingle,
                        UseSystemPasswordChar = true,
                        MaxLength = f.MaxLength ?? DefaultMaxLength,
                        Margin = new Padding(0)
                    };

                case FieldKind.Number:
                case FieldKind.Money:
                case FieldKind.Integer:
                    var amount = new TextBox
                    {
                        // Money seeds at two decimals, matching how the same figure is written
                        // in every grid, receipt and total in the app; a plain number keeps the
                        // shorter form, since a quantity of 5 is not "5.00" anywhere else either.
                        Text = f.Value switch
                        {
                            null => "",
                            decimal dec => dec.ToString(f.Kind == FieldKind.Money ? "0.00" : "0.##"),
                            _ => f.Value.ToString()
                        },
                        Width = 220,
                        Font = UiTheme.Body,
                        BorderStyle = BorderStyle.FixedSingle,
                        TextAlign = HorizontalAlignment.Right,
                        Margin = new Padding(0)
                    };

                    UiKit.AttachNumericFilter(amount,
                        allowDecimal: f.Kind != FieldKind.Integer,
                        allowNegative: (f.Minimum ?? 0m) < 0m);

                    return amount;

                case FieldKind.Phone:
                    var phone = new TextBox
                    {
                        Text = f.Value?.ToString() ?? "",
                        Width = width,
                        Font = UiTheme.Body,
                        BorderStyle = BorderStyle.FixedSingle,
                        MaxLength = f.MaxLength ?? DefaultMaxLength,
                        Margin = new Padding(0)
                    };
                    UiKit.AttachPhoneFilter(phone);
                    return phone;

                default:
                    return new TextBox
                    {
                        Text = f.Value?.ToString() ?? "",
                        Width = width,
                        Font = UiTheme.Body,
                        BorderStyle = BorderStyle.FixedSingle,
                        MaxLength = f.MaxLength ?? DefaultMaxLength,
                        Margin = new Padding(0)
                    };
            }
        }

        /// <summary>
        /// A money box with the peso sign beside it. Purely a caption: the box's own text and
        /// the value read back off it are exactly what they were without it, so nothing about
        /// what gets sent to the API changes.
        /// </summary>
        private static Panel BuildMoneyRow(TextBox box)
        {
            const int prefixWidth = 20;

            var row = new Panel
            {
                Width = box.Width + prefixWidth,
                Height = box.PreferredHeight,
                Margin = new Padding(0)
            };

            var prefix = new Label
            {
                Text = "₱",
                Font = UiTheme.BodyStrong,
                ForeColor = UiTheme.TextSecondary,
                AutoSize = false,
                Width = prefixWidth,
                Height = row.Height,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(0, 0),
                UseMnemonic = false
            };

            box.Location = new Point(prefixWidth, 0);

            row.Controls.Add(prefix);
            row.Controls.Add(box);
            return row;
        }

        /// <summary>A password box with a Show/Hide toggle beside it.</summary>
        private static Panel BuildPasswordRow(TextBox box, int width)
        {
            var row = new Panel { Width = width, Height = 30, Margin = new Padding(0) };

            var toggle = UiKit.Action("Show", ButtonTone.Secondary, null, 76, 24);
            toggle.Location = new Point(width - 78, 0);
            toggle.Font = UiTheme.Small;
            toggle.Click += (_, _) =>
            {
                box.UseSystemPasswordChar = !box.UseSystemPasswordChar;
                toggle.Text = box.UseSystemPasswordChar ? "Show" : "Hide";
            };

            box.Location = new Point(0, 0);
            row.Controls.Add(box);
            row.Controls.Add(toggle);
            return row;
        }

        // ------------------------------------------------------------------ validation

        /// <summary>
        /// Everything checkable without the server. The messages are the ones the operator
        /// needs, naming the field rather than restating a rule.
        ///
        /// Every field is checked, not just up to the first bad one, so an operator fixing a
        /// form is told about all of it at once rather than discovering the next problem each
        /// time they press Save. One message per field - the first rule that field breaks -
        /// since a field that is both blank and too short has only one thing to do about it.
        /// </summary>
        private List<(FieldSpec Field, string Message)> ValidateFields()
        {
            var problems = new List<(FieldSpec, string)>();

            foreach (var f in _fields)
            {
                var text = f.Text;

                if (f.Required && f.Kind is not FieldKind.Check && string.IsNullOrWhiteSpace(text))
                {
                    problems.Add((f, f.Kind switch
                    {
                        FieldKind.Combo => $"Please choose a {f.Label.ToLowerInvariant()}.",
                        FieldKind.Money or FieldKind.Number or FieldKind.Integer =>
                            $"Please enter {Article(f.Label)}.",
                        _ => $"{f.Label} is required."
                    }));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(text)) continue;

                if (f.MaxLength is { } max && text.Length > max)
                {
                    problems.Add((f, $"{f.Label} cannot be longer than {max} characters."));
                    continue;
                }

                switch (f.Kind)
                {
                    case FieldKind.Email:
                        if (!EmailPattern.IsMatch(text))
                        {
                            problems.Add((f, "Please enter a valid email address."));
                            continue;
                        }
                        break;

                    case FieldKind.Integer:
                        if (!int.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var whole))
                        {
                            problems.Add((f, $"Please enter a whole number for {f.Label.ToLowerInvariant()}."));
                            continue;
                        }
                        if (NumberProblem(f, whole) is { } wholeProblem)
                        {
                            problems.Add((f, wholeProblem));
                            continue;
                        }
                        break;

                    case FieldKind.Number:
                    case FieldKind.Money:
                        if (!decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var number))
                        {
                            problems.Add((f, f.Kind == FieldKind.Money
                                ? "Please enter a valid amount."
                                : $"Please enter a valid number for {f.Label.ToLowerInvariant()}."));
                            continue;
                        }
                        if (NumberProblem(f, number) is { } numberProblem)
                        {
                            problems.Add((f, numberProblem));
                            continue;
                        }
                        break;

                    case FieldKind.Date:
                        if (f.Date.Year < 1900 || f.Date.Year > 2200)
                        {
                            problems.Add((f, "Please enter a valid date."));
                            continue;
                        }
                        break;
                }

                if (f.Validate is not null && f.Validate(f) is { } custom)
                {
                    problems.Add((f, custom));
                }
            }

            return problems;
        }

        /// <summary>
        /// Puts each problem under the field it belongs to, so the operator reads "this one, and
        /// why" beside the box rather than one sentence at the top about a field they then have
        /// to find. The banner is kept for the count alone, and only when there is more than one
        /// - repeating a single message twice on the same screen says nothing the inline line
        /// did not already say.
        /// </summary>
        private void ShowFieldProblems(List<(FieldSpec Field, string Message)> problems)
        {
            ClearFieldProblems();

            foreach (var (field, message) in problems)
            {
                if (field.ErrorLabel is null) continue;

                field.ErrorLabel.Text = "⚠  " + message;
                field.ErrorLabel.Visible = true;
            }

            ShowError(problems.Count > 1 ? $"{problems.Count} fields need attention." : null);

            Focus(problems[0].Field);
        }

        private void ClearFieldProblems()
        {
            foreach (var f in _fields)
            {
                if (f.ErrorLabel is null) continue;

                f.ErrorLabel.Visible = false;
                f.ErrorLabel.Text = "";
            }
        }

        private static string? NumberProblem(FieldSpec f, decimal value)
        {
            // Nothing this application stores is meaningfully negative, so zero is the floor
            // unless a field says otherwise.
            var floor = f.Minimum ?? 0m;

            if (value < floor)
            {
                if (floor == 0m)
                {
                    return f.Kind == FieldKind.Money
                        ? $"{f.Label} cannot be negative."
                        : $"{f.Label} cannot be negative.";
                }

                return $"{f.Label} must be at least {floor:0.##}.";
            }

            if (f.Maximum is { } ceiling && value > ceiling)
            {
                return $"{f.Label} cannot be more than {ceiling:0.##}.";
            }

            return null;
        }

        private static string Article(string label)
        {
            var lower = label.ToLowerInvariant();
            return "aeiou".Contains(lower[0]) ? $"an {lower}" : $"a {lower}";
        }

        private void Focus(FieldSpec f) => f.Control?.Focus();

        // ------------------------------------------------------------------ saving

        private async Task SaveAsync()
        {
            if (_saving) return;

            var problems = ValidateFields();

            if (problems.Count > 0)
            {
                ShowFieldProblems(problems);
                return;
            }

            try
            {
                _saving = true;
                _saveButton.Enabled = false;
                _saveButton.Text = "Working…";
                Cursor = Cursors.WaitCursor;
                ShowError(null);
                ClearFieldProblems();

                var serverProblem = await _save(_fields);

                if (serverProblem is null)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }

                ShowError(serverProblem);
            }
            catch (Exception ex)
            {
                ShowError(Api.ApiErrorText.IsUsable(ex.Message)
                    ? ex.Message
                    : "Something went wrong saving this record. Please try again.");
            }
            finally
            {
                _saving = false;

                if (!IsDisposed)
                {
                    _saveButton.Enabled = true;
                    _saveButton.Text = _saveText;
                    Cursor = Cursors.Default;
                }
            }
        }

        private void ShowError(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                _errorPanel.Visible = false;
                return;
            }

            var lines = message.Split('\n').Length;
            _errorPanel.Height = Math.Min(150, 42 + (lines - 1) * 18);

            _error.Text = message;
            _errorPanel.Visible = true;
        }

        /// <summary>Returns true when the record was saved.</summary>
        public static bool Run(IWin32Window owner, string title, string subtitle,
                               IList<FieldSpec> fields,
                               Func<IList<FieldSpec>, Task<string?>> save,
                               string saveText = "Save")
        {
            using var dialog = new EditDialog(title, subtitle, fields, save, saveText);
            return dialog.ShowDialog(owner) == DialogResult.OK;
        }
    }
}
