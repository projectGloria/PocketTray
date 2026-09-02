namespace PocketTray;

/// <summary>
/// Central window listing all pinned items, with add / edit / remove / reorder.
/// Talks back to TrayApp so changes are reflected in the live tray icons and
/// saved to disk immediately.
/// </summary>
internal sealed class ManageItemsForm : Form
{
    private readonly ListView _listView = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false
    };

    private readonly TrayApp _app;

    /// <summary>Non-null while the app icon is animated, driving this window's icon.</summary>
    private AnimatedIcon? _iconAnimation;

    /// <summary>Whether the current <see cref="Form.Icon"/> is ours to dispose.</summary>
    private bool _ownsIcon;

    public ManageItemsForm(TrayApp app)
    {
        _app = app;

        Text = "PocketTray - Manage Items";
        Width = 640;
        Height = 420;
        StartPosition = FormStartPosition.CenterScreen;
        ApplyAppIcon();

        _listView.Columns.Add("Name", 160);
        _listView.Columns.Add("Type", 80);
        _listView.Columns.Add("Path", 340);
        _listView.DoubleClick += (_, _) => EditSelected();

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(6)
        };

        var addButton = new Button { Text = "Add...", AutoSize = true };
        addButton.Click += (_, _) => AddNew();

        var editButton = new Button { Text = "Edit...", AutoSize = true };
        editButton.Click += (_, _) => EditSelected();

        var removeButton = new Button { Text = "Remove", AutoSize = true };
        removeButton.Click += (_, _) => RemoveSelected();

        var moveUpButton = new Button { Text = "Move Up", AutoSize = true };
        moveUpButton.Click += (_, _) => MoveSelected(-1);

        var moveDownButton = new Button { Text = "Move Down", AutoSize = true };
        moveDownButton.Click += (_, _) => MoveSelected(1);

        var appIconButton = new Button { Text = "App Icon...", AutoSize = true };
        appIconButton.Click += (_, _) => _app.ChangeAppIcon();

        var closeButton = new Button { Text = "Close", AutoSize = true };
        closeButton.Click += (_, _) => Close();

        buttonPanel.Controls.AddRange(new Control[]
        {
            addButton, editButton, removeButton, moveUpButton, moveDownButton, appIconButton, closeButton
        });

        // Add the Bottom-docked panel before the Fill-docked list view: Dock
        // layout reserves space for earlier-added controls first, so Fill must
        // come last or it will claim the whole client area before Bottom sees it.
        Controls.Add(buttonPanel);
        Controls.Add(_listView);

        _app.ItemsChanged += RefreshList;
        _app.AppIconChanged += ApplyAppIcon;
        FormClosed += (_, _) =>
        {
            _app.ItemsChanged -= RefreshList;
            _app.AppIconChanged -= ApplyAppIcon;
            _iconAnimation?.Dispose();
            _iconAnimation = null;
        };

        RefreshList();
        DarkThemeHelper.Apply(this);
    }

    /// <summary>
    /// Re-applies PocketTray's current icon as this window's icon — animating
    /// the title-bar icon too when the app icon is an animated GIF.
    /// </summary>
    private void ApplyAppIcon()
    {
        _iconAnimation?.Dispose();
        _iconAnimation = null;

        SetWindowIcon(_app.CloneAppIcon() ?? SystemIcons.Application, ownedByForm: true);

        // Frames belong to the animation, so the form must not dispose them on
        // the next swap — hence ownedByForm: false.
        _iconAnimation = AnimatedIcon.TryCreate(
            _app.AppIconSourcePath,
            frame => SetWindowIcon(frame, ownedByForm: false));

        _iconAnimation?.Start();
    }

    /// <summary>
    /// Swaps the window icon, disposing the outgoing one only if this form owned
    /// it. Shared SystemIcons instances and animation frames are never disposed.
    /// </summary>
    private void SetWindowIcon(Icon icon, bool ownedByForm)
    {
        var previous = Icon;
        var previousWasOwned = _ownsIcon;

        Icon = icon;
        _ownsIcon = ownedByForm;

        if (previousWasOwned && previous is not null && previous != SystemIcons.Application)
        {
            previous.Dispose();
        }
    }

    private void RefreshList()
    {
        _listView.BeginUpdate();
        _listView.Items.Clear();

        var normalColor = DarkThemeHelper.IsSystemDarkModeEnabled() ? Color.Gainsboro : SystemColors.WindowText;
        var missingColor = DarkThemeHelper.IsSystemDarkModeEnabled() ? Color.OrangeRed : Color.Firebrick;

        foreach (var item in _app.Config.Items)
        {
            var row = new ListViewItem(item.DisplayName)
            {
                Tag = item.Id,
                ForeColor = item.PathExists ? normalColor : missingColor
            };
            row.SubItems.Add(item.Type == TrayItemType.Folder ? "Folder" : "App");
            row.SubItems.Add(item.PathExists ? item.Path : $"{item.Path} (not found)");
            _listView.Items.Add(row);
        }

        _listView.EndUpdate();
    }

    private TrayItemConfig? GetSelectedItem()
    {
        if (_listView.SelectedItems.Count == 0)
        {
            return null;
        }

        var id = (string)_listView.SelectedItems[0].Tag!;
        return _app.Config.Items.FirstOrDefault(i => i.Id == id);
    }

    private void AddNew()
    {
        using var dialog = new AddItemForm();
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result is not null)
        {
            _app.AddItem(dialog.Result);
        }
    }

    private void EditSelected()
    {
        var item = GetSelectedItem();
        if (item is null)
        {
            return;
        }

        using var dialog = new AddItemForm(item);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result is not null)
        {
            dialog.Result.Id = item.Id; // preserve identity so the same tray icon updates in place
            _app.UpdateItem(dialog.Result);
        }
    }

    private void RemoveSelected()
    {
        var item = GetSelectedItem();
        if (item is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Remove \"{item.DisplayName}\" from the tray?",
            "PocketTray",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm == DialogResult.Yes)
        {
            _app.RemoveItem(item.Id);
        }
    }

    private void MoveSelected(int direction)
    {
        var item = GetSelectedItem();
        if (item is null)
        {
            return;
        }

        var index = _app.Config.Items.FindIndex(i => i.Id == item.Id);
        var newIndex = index + direction;
        if (newIndex < 0 || newIndex >= _app.Config.Items.Count)
        {
            return;
        }

        _app.MoveItem(index, newIndex);

        // Keep the moved row selected.
        if (newIndex < _listView.Items.Count)
        {
            _listView.Items[newIndex].Selected = true;
            _listView.Items[newIndex].Focused = true;
        }
    }
}
