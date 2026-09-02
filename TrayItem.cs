using System.Text.Json.Serialization;

namespace PocketTray;

public enum TrayItemType
{
    Folder,
    App
}

/// <summary>
/// One pinned folder or application. This is the plain-data record that gets
/// persisted to config.json; TrayIconController wraps one of these with the
/// live NotifyIcon and behavior.
/// </summary>
public sealed class TrayItemConfig
{
    /// <summary>Stable id so we can match a NotifyIcon back to its config entry across saves.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public TrayItemType Type { get; set; }

    /// <summary>Folder path, or full path to the .exe.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Optional user-chosen name. Falls back to the file/folder name when blank.</summary>
    public string? Label { get; set; }

    /// <summary>
    /// For apps with multiple windows: a substring to match against open window
    /// titles when deciding which window to bring to focus. Blank = any window
    /// belonging to the process.
    /// </summary>
    public string? WindowTitleMatch { get; set; }

    /// <summary>
    /// Apps only. Some apps (Steam, Discord, etc.) put their own icon in the
    /// tray whenever they're running, so PocketTray's pin would otherwise look
    /// like a duplicate of it. When set, PocketTray paints a small green status
    /// bar on the left edge of the pin's icon while a matching process is
    /// running, and clears it once the process exits, instead of hiding the
    /// icon outright (which made it ambiguous whether the app just hadn't been
    /// launched yet, or something was broken).
    /// </summary>
    public bool HideWhileRunning { get; set; }

    /// <summary>
    /// Optional override for the tray icon, instead of the real folder/app
    /// icon. Stored as a path relative to <see cref="ConfigManager.ConfigDir"/>
    /// (e.g. "icons\abc123.png") — PocketTray copies whatever file you pick
    /// into an "icons" folder next to the exe, so it travels along with the
    /// rest of the portable install instead of pointing at wherever the
    /// original file happened to be on this machine.
    /// </summary>
    public string? CustomIconPath { get; set; }

    [JsonIgnore]
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Label) ? Label! :
        Type == TrayItemType.Folder
            ? (System.IO.Path.GetFileName(Path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Path)
            : System.IO.Path.GetFileNameWithoutExtension(Path);

    [JsonIgnore]
    public bool PathExists => Type == TrayItemType.Folder ? Directory.Exists(Path) : File.Exists(Path);

    [JsonIgnore]
    public string OpenMenuLabel => Type == TrayItemType.Folder ? "Open Folder" : "Open";
}
