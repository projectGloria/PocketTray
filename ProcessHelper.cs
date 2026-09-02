using System.Diagnostics;

namespace PocketTray;

internal static class ProcessHelper
{
    /// <summary>
    /// Finds a running process whose main module image path matches the given
    /// exe path (case-insensitive) and returns its process id. Returns null if
    /// none is running or if the module path can't be read (e.g. elevated
    /// process, access denied).
    ///
    /// Returns only the id, not the <see cref="Process"/> itself: every
    /// <see cref="Process"/> handed back by <see cref="Process.GetProcessesByName"/>
    /// owns a native handle that must be disposed, and this is polled every few
    /// seconds by the "hide while running" timer — returning (and leaving
    /// undisposed) a live Process object here leaked a handle per tick.
    /// </summary>
    public static int? FindRunningProcessId(string exePath)
    {
        var targetName = Path.GetFileNameWithoutExtension(exePath);
        foreach (var process in Process.GetProcessesByName(targetName))
        {
            using (process)
            {
                try
                {
                    var moduleFileName = process.MainModule?.FileName;
                    if (moduleFileName is not null &&
                        string.Equals(moduleFileName, exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return process.Id;
                    }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // Access denied reading MainModule (e.g. elevated/protected process) — skip it.
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Launches the app, or if already running, brings its window (optionally
    /// matching a window-title substring) to the foreground.
    /// </summary>
    public static void LaunchOrFocus(TrayItemConfig item)
    {
        var processId = FindRunningProcessId(item.Path);
        if (processId is not null)
        {
            var windows = NativeMethods.GetVisibleWindowsForProcess((uint)processId.Value);

            var match = string.IsNullOrWhiteSpace(item.WindowTitleMatch)
                ? windows.FirstOrDefault()
                : windows.FirstOrDefault(w => w.Title.Contains(item.WindowTitleMatch, StringComparison.OrdinalIgnoreCase));

            if (match.Handle != IntPtr.Zero)
            {
                NativeMethods.FocusWindow(match.Handle);
                return;
            }

            // Process is running but has no matching visible window (e.g. still
            // starting up, or minimized to tray itself) — nothing more we can do.
            return;
        }

        Process.Start(new ProcessStartInfo(item.Path)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(item.Path) ?? string.Empty
        });
    }
}
