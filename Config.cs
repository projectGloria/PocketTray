using System.Text.Json;
using System.Text.Json.Serialization;

namespace PocketTray;

/// <summary>
/// The full persisted application state: the pinned items plus a couple of
/// app-wide flags. Serialized as-is to config.json.
/// </summary>
public sealed class AppConfig
{
    public List<TrayItemConfig> Items { get; set; } = new();
    public bool RunAtStartup { get; set; }
    public bool FirstRunComplete { get; set; }

    /// <summary>
    /// Optional path (relative to <see cref="ConfigManager.ConfigDir"/>) of a
    /// user-chosen icon for PocketTray's own anchor tray icon. Null means use
    /// the bundled/default icon.
    /// </summary>
    public string? AppIconPath { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppConfig))]
internal partial class AppConfigJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Loads and saves AppConfig to config.json next to the PocketTray executable
/// itself (not %APPDATA%), so the whole tool — exe, config, and any custom
/// icon files — is one portable folder you can copy anywhere.
/// </summary>
public static class ConfigManager
{
    public static readonly string ConfigDir =
        AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    /// <summary>Where PocketTray used to store its data before it became portable.</summary>
    private static readonly string LegacyDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PocketTray");

    /// <summary>
    /// One-time copy of config.json and any icon files from the old
    /// %APPDATA%\PocketTray location into the portable folder, so upgrading
    /// doesn't lose existing pins. Safe to call every launch — it only copies
    /// files that don't already exist at the new location.
    /// </summary>
    public static void MigrateLegacyDataIfNeeded()
    {
        if (!Directory.Exists(LegacyDir))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(ConfigDir);

            var legacyConfigPath = Path.Combine(LegacyDir, "config.json");
            if (!File.Exists(ConfigPath) && File.Exists(legacyConfigPath))
            {
                File.Copy(legacyConfigPath, ConfigPath);
            }

            foreach (var name in new[] { "x-mark-512.ico", "separator-icon.ico", "separator-icon.png" })
            {
                var legacyIconPath = Path.Combine(LegacyDir, name);
                var newIconPath = Path.Combine(ConfigDir, name);
                if (!File.Exists(newIconPath) && File.Exists(legacyIconPath))
                {
                    File.Copy(legacyIconPath, newIconPath);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort — if migration fails, PocketTray just starts fresh in the new location.
        }
    }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfig);
                if (config is not null)
                {
                    return config;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Logger.Error("couldn't read config.json - starting from an empty config", ex);
        }

        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, AppConfigJsonContext.Default.AppConfig);

        // Write to a temp file then replace, so a crash mid-write can't corrupt the config.
        var tempPath = ConfigPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Copy(tempPath, ConfigPath, overwrite: true);
        File.Delete(tempPath);

        Logger.Info($"config saved ({config.Items.Count} items)");
    }
}
