using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ERP_Project1
{
    internal enum FieldKind { Text, Multiline, Number, Money, Integer, Date, Combo, Check, Password, Email }

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

        /// <summary>A field-specific rule. Returns null when the value is acceptable.</summary>
        public Func<FieldSpec, string?>? Validate { get; set; }

        /// <summary>Value/display pairs for <see cref="FieldKind.Combo"/>.</summary>
        public List<KeyValuePair<string, string>>? Options { get; set; }

        /// <summary>Raised when a combo selection changes, so dependent fields can react.</summary>
        public Action<FieldSpec>? OnChanged { get; set; }

        internal Control? Control { get; set; }

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

        private readonly IList<FieldSpec> _fields;
        private readonly Func<IList<FieldSpec>, Task<string?>> _save;
        private readonly Panel _errorPanel;
        private readonly Label _error;
        private readonly Button _saveButton;
        private readonly string _saveText;
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

                if (field.Kind == FieldKind.Password)
                {
                    layout.Controls.Add(BuildPasswordRow((TextBox)control, fieldWidth));
                }
                else
                {
                    layout.Controls.Add(control);
                }

                if (field.Kind == FieldKind.Combo && field.OnChanged is not null &&
                    control is ComboBox combo)
                {
                    combo.SelectedIndexChanged += (_, _) => field.OnChanged(field);
                }

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
                else
                {
                    var last = layout.Controls[layout.Controls.Count - 1];
                    last.Margin = new Padding(last.Margin.Left, last.Margin.Top,
                                              last.Margin.Right, 12);
                }
            }

            scroll.Controls.Add(layout);

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

            _saveButton = UiKit.Action(saveText, ButtonTone.Primary, async (_, _) => await SaveAsync(), 160);
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

            // Size to the content, but never taller than the screen.
            var wanted = layout.PreferredSize.Height + footer.Height + 48;
            var maximum = Screen.PrimaryScreen is { } screen
                ? (int)(screen.WorkingArea.Height * 0.86)
                : 820;
            Height = Math.Min(wanted, maximum);
        }

        // ------------------------------------------------------------------ building

        private static Label BuildLabel(FieldSpec f) => new()
        {
            Text = f.Required ? f.Label + " *" : f.Label,
            Font = UiTheme.Label,
            ForeColor = UiTheme.TextPrimary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
            UseMnemonic = false
        };

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
                        Margin = new Padding(0)
                    };

                case FieldKind.Number:
                case FieldKind.Money:
                case FieldKind.Integer:
                    return new TextBox
                    {
                        Text = f.Value switch
                        {
                            null => "",
                            decimal dec => dec.ToString("0.##"),
                            _ => f.Value.ToString()
                        },
                        Width = 220,
                        Font = UiTheme.Body,
                        BorderStyle = BorderStyle.FixedSingle,
                        TextAlign = HorizontalAlignment.Right,
                        Margin = new Padding(0)
                    };

                default:
                    return new TextBox
                    {
                        Text = f.Value?.ToString() ?? "",
                        Width = width,
                        Font = UiTheme.Body,
                        BorderStyle = BorderStyle.FixedSingle,
                        Margin = new Padding(0)
                    };
            }
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
        /// </summary>
        private string? ValidateFields()
        {
            foreach (var f in _fields)
            {
                var text = f.Text;

                if (f.Required && f.Kind is not FieldKind.Check && string.IsNullOrWhiteSpace(text))
                {
                    Focus(f);
                    return f.Kind switch
                    {
                        FieldKind.Combo => $"Please choose a {f.Label.ToLowerInvariant()}.",
                        FieldKind.Money or FieldKind.Number or FieldKind.Integer =>
                            $"Please enter {Article(f.Label)}.",
                        _ => $"{f.Label} is required."
                    };
                }

                if (string.IsNullOrWhiteSpace(text)) continue;

                if (f.MaxLength is { } max && text.Length > max)
                {
                    Focus(f);
                    return $"{f.Label} cannot be longer than {max} characters.";
                }

                switch (f.Kind)
                {
                    case FieldKind.Email:
                        if (!EmailPattern.IsMatch(text))
                        {
                            Focus(f);
                            return "Please enter a valid email address.";
                        }
                        break;

                    case FieldKind.Integer:
                        if (!int.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var whole))
                        {
                            Focus(f);
                            return $"Please enter a whole number for {f.Label.ToLowerInvariant()}.";
                        }
                        if (NumberProblem(f, whole) is { } wholeProblem)
                        {
                            Focus(f);
                            return wholeProblem;
                        }
                        break;

                    case FieldKind.Number:
                    case FieldKind.Money:
                        if (!decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var number))
                        {
                            Focus(f);
                            return f.Kind == FieldKind.Money
                                ? "Please enter a valid amount."
                                : $"Please enter a valid number for {f.Label.ToLowerInvariant()}.";
                        }
                        if (NumberProblem(f, number) is { } numberProblem)
                        {
                            Focus(f);
                            return numberProblem;
                        }
                        break;

                    case FieldKind.Date:
                        if (f.Date.Year < 1900 || f.Date.Year > 2200)
                        {
                            Focus(f);
                            return "Please enter a valid date.";
                        }
                        break;
                }

                if (f.Validate is not null && f.Validate(f) is { } custom)
                {
                    Focus(f);
                    return custom;
                }
            }

            return null;
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

            if (ValidateFields() is { } problem)
            {
                ShowError(problem);
                return;
            }

            try
            {
                _saving = true;
                _saveButton.Enabled = false;
                _saveButton.Text = "Working…";
                Cursor = Cursors.WaitCursor;
                ShowError(null);

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
