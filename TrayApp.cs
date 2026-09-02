namespace PocketTray;

/// <summary>
/// Root of the application: no main window, just a set of NotifyIcons kept in
/// sync with AppConfig. Runs as an ApplicationContext so the process stays
/// alive purely off the tray icons until Exit() is called.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    public AppConfig Config { get; }

    private readonly List<TrayIconController> _controllers = new();
    private ManageItemsForm? _manageForm;
    private readonly System.Windows.Forms.Timer _runningStateTimer;
    private NotifyIcon? _separatorIcon;
    private ContextMenuStrip? _separatorMenu;
    private Icon? _separatorIconImage;
    private ToolStripMenuItem? _resetAppIconMenuItem;
    private AnimatedIcon? _appIconAnimation;

    /// <summary>Raised whenever PocketTray's own icon changes, so open windows can re-apply it.</summary>
    public event Action? AppIconChanged;

    /// <summary>
    /// A hidden, handle-forced Control used purely to marshal background-thread
    /// results back onto the UI thread via BeginInvoke. Control.Invoke/BeginInvoke
    /// works via the control's own message queue and doesn't depend on
    /// SynchronizationContext.Current being installed yet — unlike
    /// TaskScheduler.FromCurrentSynchronizationContext(), which would be unsafe
    /// here since the very first RefreshRunningIndicators() call happens inside
    /// this constructor, before Application.Run() has started the message loop.
    /// </summary>
    private readonly Control _uiThreadMarshal = new();

    private bool _refreshInProgress;
    private bool _isExiting;

    /// <summary>Raised whenever Config.Items changes, so open windows (Manage Items) can refresh.</summary>
    public event Action? ItemsChanged;

    public TrayApp()
    {
        ConfigManager.MigrateLegacyDataIfNeeded();
        Config = ConfigManager.Load();

        // Registry is the source of truth for the startup toggle (the user could
        // have removed it outside PocketTray), so re-sync on launch.
        Config.RunAtStartup = StartupManager.IsEnabled();

        // Force the marshal control's handle to exist now, so BeginInvoke works
        // immediately — including from the very first RefreshRunningIndicators()
        // call below, which fires before Application.Run() starts the loop.
        _uiThreadMarshal.CreateControl();

        // Created before the per-item icons: PocketTray's own icon acts as an
        // anchor so all the pin icons that follow group together to one side
        // of it, distinguishing them from icons other apps (Steam, Discord,
        // etc.) add to the tray on their own. Windows doesn't give apps direct
        // control over icon order — the first time this runs you may need to
        // drag the icons into your preferred left/right arrangement, but
        // Windows remembers manual placement per icon after that.
        CreateSeparatorIcon();

        foreach (var item in Config.Items)
        {
            _controllers.Add(CreateController(item));
        }

        if (!Config.FirstRunComplete)
        {
            RunFirstLaunchFlow();
        }

        RefreshRunningIndicators();

        Logger.Info($"started with {Config.Items.Count} pinned items (startup entry: {Config.RunAtStartup})");

        _runningStateTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _runningStateTimer.Tick += (_, _) => RefreshRunningIndicators();
        _runningStateTimer.Start();
    }

    /// <summary>
    /// Creates PocketTray's own always-visible tray icon. It carries no pinned
    /// item — it's purely a visual anchor/separator, plus a convenient place
    /// for the global "Manage Items"/"Exit" actions. Looks for a custom image
    /// right next to the exe (x-mark-512.ico, or separator-icon.ico/.png) so
    /// you can swap it out — and carry it along when you move the folder —
    /// without a rebuild; falls back to a plain vertical bar.
    /// </summary>
    private void CreateSeparatorIcon()
    {
        _separatorIconImage = LoadAppIcon();

        _separatorMenu = new ContextMenuStrip();

        var manageItem = new ToolStripMenuItem("Manage Items...");
        manageItem.Click += (_, _) => ShowManageWindow();
        _separatorMenu.Items.Add(manageItem);

        var changeIconItem = new ToolStripMenuItem("Change PocketTray Icon...");
        changeIconItem.Click += (_, _) => ChangeAppIcon();
        _separatorMenu.Items.Add(changeIconItem);

        _resetAppIconMenuItem = new ToolStripMenuItem("Reset PocketTray Icon")
        {
            Enabled = !string.IsNullOrWhiteSpace(Config.AppIconPath)
        };
        _resetAppIconMenuItem.Click += (_, _) => ResetAppIcon();
        _separatorMenu.Items.Add(_resetAppIconMenuItem);

        _separatorMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit PocketTray");
        exitItem.Click += (_, _) => Exit();
        _separatorMenu.Items.Add(exitItem);

        DarkThemeHelper.ApplyToMenu(_separatorMenu);

        _separatorIcon = new NotifyIcon
        {
            Icon = _separatorIconImage,
            Text = "PocketTray — your pins are grouped next to this icon",
            ContextMenuStrip = _separatorMenu,
            Visible = true
        };

        _separatorIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowManageWindow();
            }
        };

        StartAppIconAnimation();
    }

    /// <summary>
    /// The image file PocketTray's own icon comes from: the user's choice if
    /// set, else a bundled image next to the exe, else null (plain bar).
    /// </summary>
    private string? ResolveAppIconPath()
    {
        if (!string.IsNullOrWhiteSpace(Config.AppIconPath))
        {
            var chosen = Path.Combine(ConfigManager.ConfigDir, Config.AppIconPath);
            if (File.Exists(chosen))
            {
                return chosen;
            }

            Logger.Warn($"configured app icon '{Config.AppIconPath}' is missing - falling back to the default");
        }

        return new[] { "x-mark-512.ico", "separator-icon.ico", "separator-icon.png" }
            .Select(name => Path.Combine(ConfigManager.ConfigDir, name))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// If the current app icon is an animated image (a multi-frame GIF), starts
    /// cycling its frames on the tray icon; otherwise leaves the static icon in
    /// place. Always stops any animation already running first, so this is safe
    /// to call on every icon change.
    /// </summary>
    private void StartAppIconAnimation()
    {
        _appIconAnimation?.Dispose();
        _appIconAnimation = null;

        if (_separatorIcon is null)
        {
            return;
        }

        var animation = AnimatedIcon.TryCreate(ResolveAppIconPath(), frame =>
        {
            if (!_isExiting && _separatorIcon is not null)
            {
                _separatorIcon.Icon = frame;
            }
        });

        if (animation is null)
        {
            // Static icon — make sure the tray shows it (an animation may have
            // just been swapped away).
            _separatorIcon.Icon = _separatorIconImage;
            return;
        }

        _appIconAnimation = animation;
        animation.Start();
    }

    /// <summary>
    /// Builds PocketTray's own static icon: the user's chosen image from
    /// <see cref="AppConfig.AppIconPath"/> if set, else a bundled image sitting
    /// next to the exe, else a plain vertical bar.
    /// </summary>
    private Icon LoadAppIcon()
    {
        var fallback = IconHelper.CreateVerticalLineIcon(Color.FromArgb(120, 120, 120));

        return IconHelper.LoadCustomOrDefault(ResolveAppIconPath(), fallback);
    }

    /// <summary>
    /// Lets the user pick a new icon for PocketTray itself. The file is copied
    /// into the portable "icons" folder and applied immediately — no restart.
    /// </summary>
    public void ChangeAppIcon()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose a PocketTray icon",
            Filter = "Icon/image files (*.ico;*.png;*.gif;*.jpg;*.jpeg;*.bmp)|*.ico;*.png;*.gif;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        string savedRelativePath;
        try
        {
            savedRelativePath = IconHelper.SaveCustomIcon(dialog.FileName);
        }
        catch (Exception ex)
        {
            Logger.Error("couldn't copy the chosen icon into the icons folder", ex);
            MessageBox.Show(
                $"Couldn't use that icon:\n{ex.Message}",
                "PocketTray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        Logger.Info($"app icon changed to '{savedRelativePath}'");

        var previous = Config.AppIconPath;
        Config.AppIconPath = savedRelativePath;
        ApplyAppIcon();
        ConfigManager.Save(Config);

        // Only now that the new icon is in use is the old copy safe to drop.
        if (!string.Equals(previous, savedRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            IconHelper.DeleteCustomIconIfExists(previous);
        }
    }

    /// <summary>Drops the user-chosen icon and goes back to the default.</summary>
    public void ResetAppIcon()
    {
        if (string.IsNullOrWhiteSpace(Config.AppIconPath))
        {
            return;
        }

        Logger.Info("app icon reset to default");

        var previous = Config.AppIconPath;
        Config.AppIconPath = null;
        ApplyAppIcon();
        ConfigManager.Save(Config);
        IconHelper.DeleteCustomIconIfExists(previous);
    }

    private void ApplyAppIcon()
    {
        var newIcon = LoadAppIcon();
        var oldIcon = _separatorIconImage;

        _separatorIconImage = newIcon;
        if (_separatorIcon is not null)
        {
            _separatorIcon.Icon = newIcon;
        }

        // Swap first, dispose after: the NotifyIcon must never point at a
        // destroyed handle, not even briefly. The running animation (if any)
        // still holds frames of the *old* icon, so it has to go first — that's
        // what StartAppIconAnimation does before building the new one.
        StartAppIconAnimation();
        oldIcon?.Dispose();

        if (_resetAppIconMenuItem is not null)
        {
            _resetAppIconMenuItem.Enabled = !string.IsNullOrWhiteSpace(Config.AppIconPath);
        }

        AppIconChanged?.Invoke();
    }

    /// <summary>
    /// The image file PocketTray's own icon is currently drawn from, so windows
    /// can animate their own title-bar icon from the same source. Null when the
    /// built-in vertical-bar icon is in use.
    /// </summary>
    public string? AppIconSourcePath => ResolveAppIconPath();

    /// <summary>A copy of PocketTray's current icon, for use as a window icon.</summary>
    public Icon? CloneAppIcon() => _separatorIconImage is null ? null : (Icon)_separatorIconImage.Clone();

    /// <summary>
    /// Paints/clears the green running-status bar on any App item flagged
    /// HideWhileRunning, based on whether a matching process is currently
    /// running. Polled rather than event-driven since there's no cheap
    /// cross-process "process started/exited" signal for an arbitrary path.
    ///
    /// Runs the actual checks on a background thread: File.Exists and the
    /// process/module lookup can both block for a long time against an
    /// unreachable network path or a spun-down drive, and this used to run
    /// straight off the poll timer's Tick — on the UI thread — which meant one
    /// stale pin could freeze every tray icon and every open PocketTray window
    /// for as long as the I/O stayed stuck, every 3 seconds, indefinitely.
    /// </summary>
    private void RefreshRunningIndicators()
    {
        if (_refreshInProgress || _isExiting)
        {
            // A previous check hasn't finished yet (most likely stuck behind
            // an unreachable path) — skip this tick rather than piling up
            // overlapping background scans.
            return;
        }

        // Snapshot just the plain data each item needs, on the UI thread, so
        // the background thread never touches the live Config/_controllers
        // collections — those can be mutated concurrently by add/edit/remove
        // on the UI thread while this check is still in flight.
        var snapshot = _controllers
            .Select(c => (c.Item.Id, c.Item.Type, c.Item.HideWhileRunning, c.Item.Path))
            .ToList();

        _refreshInProgress = true;

        Task.Run(() =>
        {
            var results = new List<(string Id, bool IsRunning)>(snapshot.Count);
            foreach (var (id, type, hideWhileRunning, path) in snapshot)
            {
                if (type != TrayItemType.App || !hideWhileRunning)
                {
                    // Not flagged (or no longer flagged, e.g. just toggled off
                    // in an edit) — report false so any stale indicator gets cleared.
                    results.Add((id, false));
                    continue;
                }

                var isRunning = File.Exists(path) && ProcessHelper.FindRunningProcessId(path) is not null;
                results.Add((id, isRunning));
            }

            return results;
        }).ContinueWith(ApplyRunningIndicatorResults);
    }

    private void ApplyRunningIndicatorResults(Task<List<(string Id, bool IsRunning)>> task)
    {
        try
        {
            _uiThreadMarshal.BeginInvoke(() =>
            {
                _refreshInProgress = false;

                if (_isExiting || task.IsFaulted)
                {
                    return;
                }

                foreach (var (id, isRunning) in task.Result)
                {
                    var controller = _controllers.FirstOrDefault(c => c.Item.Id == id);
                    controller?.SetRunningState(isRunning);
                }
            });
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // App is shutting down and the marshal control is already gone (or
            // its handle is being torn down) — nothing left to update.
        }
    }

    private TrayIconController CreateController(TrayItemConfig item)
    {
        var controller = new TrayIconController(item, Config.RunAtStartup);
        controller.RemoveRequested += c => RemoveItem(c.Item.Id);
        controller.ExitRequested += Exit;
        controller.ManageRequested += ShowManageWindow;
        controller.StartupToggled += enabled => SetRunAtStartup(enabled);
        return controller;
    }

    private void RunFirstLaunchFlow()
    {
        Config.FirstRunComplete = true;
        ConfigManager.Save(Config);

        // Modal on first run: nothing else is on screen yet, and we need to wait
        // for the window to close before deciding whether to show the balloon.
        using var setupForm = new ManageItemsForm(this);
        setupForm.ShowDialog();

        if (_controllers.Count > 0)
        {
            _controllers[0].ShowBalloon(
                "PocketTray is now running",
                "Right-click any tray icon to configure it, or open Manage Items to add more.");
        }
    }

    public void ShowManageWindow()
    {
        if (_manageForm is { IsDisposed: false })
        {
            _manageForm.Activate();
            _manageForm.WindowState = FormWindowState.Normal;
            return;
        }

        _manageForm = new ManageItemsForm(this);
        _manageForm.Show();
    }

    public void AddItem(TrayItemConfig item)
    {
        Logger.Info($"item added: '{item.DisplayName}' -> {item.Path}");
        Config.Items.Add(item);
        _controllers.Add(CreateController(item));
        Persist();
    }

    public void UpdateItem(TrayItemConfig updated)
    {
        var index = Config.Items.FindIndex(i => i.Id == updated.Id);
        if (index < 0)
        {
            return;
        }

        var previousIconPath = Config.Items[index].CustomIconPath;
        if (previousIconPath != updated.CustomIconPath)
        {
            IconHelper.DeleteCustomIconIfExists(previousIconPath);
        }

        Config.Items[index] = updated;
        var controller = _controllers.FirstOrDefault(c => c.Item.Id == updated.Id);
        controller?.Refresh(updated);
        Persist();
    }

    public void RemoveItem(string id)
    {
        var index = Config.Items.FindIndex(i => i.Id == id);
        if (index < 0)
        {
            return;
        }

        IconHelper.DeleteCustomIconIfExists(Config.Items[index].CustomIconPath);
        Config.Items.RemoveAt(index);

        var controller = _controllers.FirstOrDefault(c => c.Item.Id == id);
        if (controller is not null)
        {
            _controllers.Remove(controller);
            controller.Dispose();
        }

        Persist();

        if (Config.Items.Count == 0 && _manageForm is null or { IsDisposed: true })
        {
            // No icons left and no window open to get back in via — keep the app
            // alive only if Manage Items is reachable; otherwise there'd be no
            // way to add anything back. Offer to reopen it.
            var reopen = MessageBox.Show(
                "That was the last pinned item. Open Manage Items to add another, or Exit PocketTray?",
                "PocketTray",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (reopen == DialogResult.Yes)
            {
                ShowManageWindow();
            }
            else
            {
                Exit();
            }
        }
    }

    public void MoveItem(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Config.Items.Count ||
            toIndex < 0 || toIndex >= Config.Items.Count)
        {
            return;
        }

        var item = Config.Items[fromIndex];
        Config.Items.RemoveAt(fromIndex);
        Config.Items.Insert(toIndex, item);
        Persist();
    }

    private void SetRunAtStartup(bool enabled)
    {
        StartupManager.SetEnabled(enabled);
        Config.RunAtStartup = enabled;
        foreach (var controller in _controllers)
        {
            controller.SetStartupChecked(enabled);
        }
        ConfigManager.Save(Config);
    }

    private void Persist()
    {
        ConfigManager.Save(Config);
        RefreshRunningIndicators();
        ItemsChanged?.Invoke();
    }

    public void Exit()
    {
        _isExiting = true;

        _runningStateTimer.Stop();
        _runningStateTimer.Dispose();

        foreach (var controller in _controllers)
        {
            controller.Dispose();
        }
        _controllers.Clear();

        if (_separatorIcon is not null)
        {
            _separatorIcon.Visible = false;
            _separatorIcon.Dispose();
        }
        _appIconAnimation?.Dispose();
        _separatorMenu?.Dispose();
        _separatorIconImage?.Dispose();
        _uiThreadMarshal.Dispose();

        _manageForm?.Close();
        ExitThread();
    }
}
