using Microsoft.Win32;

namespace PocketTray;

/// <summary>
/// Toggles "start with Windows" via a registry value under
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
///
/// Chosen over a shell:startup shortcut because it's a single registry write
/// with no filesystem shortcut (.lnk) to generate — .lnk creation from .NET
/// needs either COM interop (IWshRuntimeLibrary) or a hand-rolled binary
/// format, both more moving parts than a two-line registry call. HKCU
/// requires no elevation and is per-user, matching a portable single-exe tool.
/// </summary>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PocketTray";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return value is not null && string.Equals(value.Trim('"'), GetExePath(), StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                         ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{GetExePath()}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string GetExePath() =>
        Environment.ProcessPath ?? Application.ExecutablePath;
}
