namespace PocketTray;

/// <summary>
/// Owns one NotifyIcon (and its context menu) for a single pinned item.
/// </summary>
internal sealed class TrayIconController : IDisposable
{
    public TrayItemConfig Item { get; private set; }

    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _openMenuItem;
    private readonly ToolStripMenuItem _startupMenuItem;

    private Icon? _baseIcon;

    /// <summary>True while a matching process is running and this pin is hidden.</summary>
    private bool _hiddenWhileRunning;

    /// <summary>Non-null while this pin's custom icon is an animated GIF.</summary>
    private AnimatedIcon? _animation;

    /// <summary>The animation frame currently on screen. Owned by <see cref="_animation"/>.</summary>
    private Icon? _currentFrame;

    public event Action<TrayIconController>? RemoveRequested;
    public event Action? ExitRequested;
    public event Action? ManageRequested;
    public event Action<bool>? StartupToggled;

    public TrayIconController(TrayItemConfig item, bool startupEnabled)
    {
        Item = item;

        _menu = new ContextMenuStrip();

        _openMenuItem = new ToolStripMenuItem(item.OpenMenuLabel);
        _openMenuItem.Click += (_, _) => Activate();
        _menu.Items.Add(_openMenuItem);

        _menu.Items.Add(new ToolStripSeparator());

        var manageItem = new ToolStripMenuItem("Manage Items...");
        manageItem.Click += (_, _) => ManageRequested?.Invoke();
        _menu.Items.Add(manageItem);

        var removeItem = new ToolStripMenuItem("Remove from Tray");
        removeItem.Click += (_, _) => RemoveRequested?.Invoke(this);
        _menu.Items.Add(removeItem);

        _startupMenuItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = startupEnabled
        };
        _startupMenuItem.Click += (_, _) => StartupToggled?.Invoke(_startupMenuItem.Checked);
        _menu.Items.Add(_startupMenuItem);

        _menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();
        _menu.Items.Add(exitItem);

        DarkThemeHelper.ApplyToMenu(_menu);

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Visible = true
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Activate();
            }
        };

        Refresh(item);
    }

    /// <summary>Re-applies icon, tooltip, and labels — used after add/edit or on reorder.</summary>
    public void Refresh(TrayItemConfig item)
    {
        Item = item;
        _openMenuItem.Text = item.OpenMenuLabel;

        StopAnimation();

        _baseIcon?.Dispose();
        var shellIcon = IconHelper.GetIconForPath(item.Path, item.Type == TrayItemType.Folder);
        _baseIcon = string.IsNullOrWhiteSpace(item.CustomIconPath)
            ? shellIcon
            : IconHelper.LoadCustomOrDefault(Path.Combine(ConfigManager.ConfigDir, item.CustomIconPath), shellIcon);

        // An animated custom icon drives the tray icon frame by frame; anything
        // else (shell icon, .ico, .png, single-frame .gif) stays static.
        if (!string.IsNullOrWhiteSpace(item.CustomIconPath))
        {
            if (!File.Exists(Path.Combine(ConfigManager.ConfigDir, item.CustomIconPath)))
            {
                Logger.Warn($"custom icon '{item.CustomIconPath}' for '{item.DisplayName}' is missing - using the shell icon");
            }

            _animation = AnimatedIcon.TryCreate(
                Path.Combine(ConfigManager.ConfigDir, item.CustomIconPath),
                ShowAnimationFrame);
        }

        if (_animation is not null)
        {
            _animation.Start();

            // An edit while the app is running leaves the pin hidden — don't
            // let the fresh animation tick away behind an invisible icon.
            _animation.SetPaused(_hiddenWhileRunning);
        }
        else
        {
            UpdateDisplayedIcon();
        }

        UpdateTooltip();
    }

    public void SetStartupChecked(bool enabled) => _startupMenuItem.Checked = enabled;

    /// <summary>
    /// Hides or restores this pin for <see cref="TrayItemConfig.HideWhileRunning"/>
    /// items: the icon disappears from the tray entirely while a matching
    /// process is running, and comes back once it exits. Meant for apps that
    /// place their own icon in the tray, so the two don't sit side by side.
    /// </summary>
    public void SetRunningState(bool isRunning)
    {
        if (_hiddenWhileRunning == isRunning)
        {
            return;
        }

        _hiddenWhileRunning = isRunning;
        _notifyIcon.Visible = !isRunning;

        // No point spending frames on an icon nobody can see.
        _animation?.SetPaused(isRunning);

        Logger.Info($"pin '{Item.DisplayName}' {(isRunning ? "hidden - app is running" : "shown - app exited")}");
    }

    /// <summary>Puts one animation frame on the tray icon.</summary>
    private void ShowAnimationFrame(Icon frame)
    {
        _currentFrame = frame;
        _notifyIcon.Icon = frame;
    }

    /// <summary>Stops any running animation and drops its cached composited frames.</summary>
    private void StopAnimation()
    {
        _animation?.Dispose();
        _animation = null;
        _currentFrame = null;
    }

    private void UpdateDisplayedIcon() => _notifyIcon.Icon = _baseIcon;

    private void UpdateTooltip()
    {
        var tooltip = $"{Item.DisplayName}\n{Item.Path}";

        // NotifyIcon tooltips are capped at 127 characters.
        _notifyIcon.Text = tooltip.Length > 127 ? tooltip[..124] + "..." : tooltip;
    }

    private void Activate()
    {
        Logger.Info($"activate '{Item.DisplayName}' ({Item.Type})");

        if (!Item.PathExists)
        {
            Logger.Warn($"path missing for '{Item.DisplayName}': {Item.Path}");
            var kind = Item.Type == TrayItemType.Folder ? "folder" : "application";
            var result = MessageBox.Show(
                $"The {kind} for \"{Item.DisplayName}\" can no longer be found:\n{Item.Path}\n\n" +
                "Open Manage Items to re-select or remove it?",
                "PocketTray - Item Not Found",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                ManageRequested?.Invoke();
            }

            return;
        }

        try
        {
            if (Item.Type == TrayItemType.Folder)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Item.Path)
                {
                    UseShellExecute = true
                });
            }
            else
            {
                ProcessHelper.LaunchOrFocus(Item);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"couldn't open '{Item.DisplayName}' ({Item.Path})", ex);
            MessageBox.Show(
                $"Couldn't open \"{Item.DisplayName}\":\n{ex.Message}",
                "PocketTray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    public void ShowBalloon(string title, string text) =>
        _notifyIcon.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);

    public void Dispose()
    {
        StopAnimation();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _baseIcon?.Dispose();
    }
}
