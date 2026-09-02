using System.Diagnostics;

namespace PocketTray;

/// <summary>
/// Dialog for pinning a new folder or application, or editing an existing one.
/// </summary>
internal sealed class AddItemForm : Form
{
    private readonly RadioButton _folderRadio = new() { Text = "Folder", AutoSize = true };
    private readonly RadioButton _appRadio = new() { Text = "Application", AutoSize = true };
    private readonly TextBox _pathBox = new() { Width = 300 };
    private readonly Button _browseButton = new() { Text = "Browse...", AutoSize = true };
    private readonly Button _pickWindowButton = new() { Text = "Pick Running Window...", AutoSize = true };
    private readonly TextBox _labelBox = new() { Width = 300 };
    private readonly TextBox _windowTitleBox = new() { Width = 300 };
    private readonly Label _windowTitleLabel = new() { Text = "Window title contains (optional):", AutoSize = true };
    private readonly CheckBox _hideWhileRunningBox = new()
    {
        Text = "Hide this pin while the app is running (for apps that add their own tray icon)",
        AutoSize = true
    };
    private readonly TextBox _customIconBox = new() { Width = 220, ReadOnly = true };
    private readonly Button _customIconBrowseButton = new() { Text = "Browse...", AutoSize = true };
    private readonly Button _customIconClearButton = new() { Text = "Clear", AutoSize = true };
    private readonly Button _okButton = new() { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
    private readonly Button _cancelButton = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };

    /// <summary>Full path of a newly picked icon file, pending copy into the portable icons folder on OK. Null if unchanged.</summary>
    private string? _pendingIconSourcePath;

    /// <summary>The item's existing CustomIconPath (relative), carried over if the icon isn't changed or cleared.</summary>
    private string? _existingIconRelativePath;

    private bool _iconCleared;

    public TrayItemConfig? Result { get; private set; }

    public AddItemForm(TrayItemConfig? editing = null)
    {
        Text = editing is null ? "Add Item to PocketTray" : "Edit Item";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        var typePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        typePanel.Controls.Add(_folderRadio);
        typePanel.Controls.Add(_appRadio);
        layout.Controls.Add(new Label { Text = "Type:", AutoSize = true }, 0, 0);
        layout.Controls.Add(typePanel, 1, 0);
        layout.SetColumnSpan(typePanel, 2);

        layout.Controls.Add(new Label { Text = "Path:", AutoSize = true }, 0, 1);
        layout.Controls.Add(_pathBox, 1, 1);
        layout.Controls.Add(_browseButton, 2, 1);

        layout.Controls.Add(new Label(), 0, 2);
        layout.Controls.Add(_pickWindowButton, 1, 2);

        layout.Controls.Add(new Label { Text = "Label (optional):", AutoSize = true }, 0, 3);
        layout.Controls.Add(_labelBox, 1, 3);

        layout.Controls.Add(_windowTitleLabel, 0, 4);
        layout.Controls.Add(_windowTitleBox, 1, 4);

        layout.Controls.Add(_hideWhileRunningBox, 0, 5);
        layout.SetColumnSpan(_hideWhileRunningBox, 3);

        var iconPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        iconPanel.Controls.Add(_customIconBox);
        iconPanel.Controls.Add(_customIconBrowseButton);
        iconPanel.Controls.Add(_customIconClearButton);
        layout.Controls.Add(new Label { Text = "Custom Icon (optional):", AutoSize = true }, 0, 6);
        layout.Controls.Add(iconPanel, 1, 6);
        layout.SetColumnSpan(iconPanel, 2);

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom
        };
        buttonPanel.Controls.Add(_cancelButton);
        buttonPanel.Controls.Add(_okButton);

        var root = new TableLayoutPanel { AutoSize = true, RowCount = 2, ColumnCount = 1 };
        root.Controls.Add(layout, 0, 0);
        root.Controls.Add(buttonPanel, 0, 1);

        Controls.Add(root);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        _folderRadio.CheckedChanged += (_, _) => UpdateModeUi();
        _appRadio.CheckedChanged += (_, _) => UpdateModeUi();
        _browseButton.Click += OnBrowse;
        _pickWindowButton.Click += OnPickWindow;
        _customIconBrowseButton.Click += OnBrowseCustomIcon;
        _customIconClearButton.Click += OnClearCustomIcon;
        _okButton.Click += OnOk;

        if (editing is not null)
        {
            _folderRadio.Checked = editing.Type == TrayItemType.Folder;
            _appRadio.Checked = editing.Type == TrayItemType.App;
            _pathBox.Text = editing.Path;
            _labelBox.Text = editing.Label ?? string.Empty;
            _windowTitleBox.Text = editing.WindowTitleMatch ?? string.Empty;
            _hideWhileRunningBox.Checked = editing.HideWhileRunning;

            _existingIconRelativePath = editing.CustomIconPath;
            if (!string.IsNullOrWhiteSpace(editing.CustomIconPath))
            {
                _customIconBox.Text = Path.Combine(ConfigManager.ConfigDir, editing.CustomIconPath);
            }
        }
        else
        {
            _folderRadio.Checked = true;
        }

        UpdateModeUi();
        DarkThemeHelper.Apply(this);
    }

    private void UpdateModeUi()
    {
        var isApp = _appRadio.Checked;
        _pickWindowButton.Visible = isApp;
        _windowTitleLabel.Visible = isApp;
        _windowTitleBox.Visible = isApp;
        _hideWhileRunningBox.Visible = isApp;
        _browseButton.Text = isApp ? "Browse for .exe..." : "Browse for Folder...";
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        if (_folderRadio.Checked)
        {
            using var dialog = new FolderBrowserDialog { Description = "Select a folder to pin" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _pathBox.Text = dialog.SelectedPath;
                if (string.IsNullOrWhiteSpace(_labelBox.Text))
                {
                    _labelBox.Text = Path.GetFileName(dialog.SelectedPath.TrimEnd('\\', '/'));
                }
            }
        }
        else
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
                Title = "Select an application"
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _pathBox.Text = dialog.FileName;
                if (string.IsNullOrWhiteSpace(_labelBox.Text))
                {
                    _labelBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
                }
            }
        }
    }

    private void OnPickWindow(object? sender, EventArgs e)
    {
        using var picker = new WindowPickerForm();
        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedExePath is null)
        {
            return;
        }

        _pathBox.Text = picker.SelectedExePath;
        if (string.IsNullOrWhiteSpace(_labelBox.Text))
        {
            _labelBox.Text = Path.GetFileNameWithoutExtension(picker.SelectedExePath);
        }
        if (!string.IsNullOrWhiteSpace(picker.SelectedWindowTitle))
        {
            _windowTitleBox.Text = picker.SelectedWindowTitle;
        }
    }

    private void OnBrowseCustomIcon(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Icon/image files (*.ico;*.png;*.gif;*.jpg;*.jpeg;*.bmp)|*.ico;*.png;*.gif;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
            Title = "Select a custom icon"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pendingIconSourcePath = dialog.FileName;
            _iconCleared = false;
            _customIconBox.Text = dialog.FileName;
        }
    }

    private void OnClearCustomIcon(object? sender, EventArgs e)
    {
        _pendingIconSourcePath = null;
        _existingIconRelativePath = null;
        _iconCleared = true;
        _customIconBox.Text = string.Empty;
    }

    private void OnOk(object? sender, EventArgs e)
    {
        var path = _pathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            MessageBox.Show(this, "Please choose a path first.", "PocketTray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        // A newly picked file gets copied into the portable icons folder now;
        // an unchanged existing icon keeps its already-stored relative path;
        // a cleared one leaves both null, falling back to the real shell icon.
        var customIconPath = _iconCleared
            ? null
            : _pendingIconSourcePath is not null
                ? IconHelper.SaveCustomIcon(_pendingIconSourcePath)
                : _existingIconRelativePath;

        Result = new TrayItemConfig
        {
            Type = _folderRadio.Checked ? TrayItemType.Folder : TrayItemType.App,
            Path = path,
            Label = string.IsNullOrWhiteSpace(_labelBox.Text) ? null : _labelBox.Text.Trim(),
            WindowTitleMatch = string.IsNullOrWhiteSpace(_windowTitleBox.Text) ? null : _windowTitleBox.Text.Trim(),
            HideWhileRunning = _appRadio.Checked && _hideWhileRunningBox.Checked,
            CustomIconPath = customIconPath
        };
    }
}

/// <summary>Small dialog listing running processes with visible windows, for "pin a running app".</summary>
internal sealed class WindowPickerForm : Form
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill };
    private readonly List<(string ExePath, string Title)> _entries = new();

    public string? SelectedExePath { get; private set; }
    public string? SelectedWindowTitle { get; private set; }

    public WindowPickerForm()
    {
        Text = "Select a Running Window";
        Width = 480;
        Height = 400;
        StartPosition = FormStartPosition.CenterParent;

        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        okButton.Click += (_, _) =>
        {
            if (_list.SelectedIndex >= 0)
            {
                var (exePath, title) = _entries[_list.SelectedIndex];
                SelectedExePath = exePath;
                SelectedWindowTitle = title;
            }
        };

        // Bottom-docked control must be added before the Fill-docked one, or it
        // won't get any space reserved for it.
        Controls.Add(okButton);
        Controls.Add(_list);
        AcceptButton = okButton;

        PopulateWindows();
        DarkThemeHelper.Apply(this);
    }

    private void PopulateWindows()
    {
        _entries.Clear();
        _list.Items.Clear();

        // One EnumWindows pass for the whole system, then match by process id —
        // see NativeMethods.GetAllVisibleWindowTitlesByProcess for why this
        // replaces a per-process EnumWindows call.
        var windowTitlesByProcessId = NativeMethods.GetAllVisibleWindowTitlesByProcess();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!windowTitlesByProcessId.TryGetValue(process.Id, out var titles))
                {
                    continue;
                }

                string? exePath;
                try
                {
                    exePath = process.MainModule?.FileName;
                }
                catch
                {
                    continue; // access denied (system/elevated processes) — skip
                }

                if (exePath is null)
                {
                    continue;
                }

                foreach (var title in titles)
                {
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        continue;
                    }

                    _entries.Add((exePath, title));
                    _list.Items.Add($"{title}  —  {Path.GetFileName(exePath)}");
                }
            }
        }
    }
}
