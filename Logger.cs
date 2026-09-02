using System.Text;

namespace PocketTray;

/// <summary>
/// A tiny append-only event log, written next to the exe as logs/pockettray.log,
/// so a run that misbehaved can be inspected after the fact — PocketTray has no
/// main window and swallows most errors to stay alive in the tray, which
/// otherwise leaves nothing to look at.
///
/// Deliberately dependency-free and best-effort: logging must never be the
/// reason the app fails, so every write is wrapped and a failure to log is
/// silently ignored.
/// </summary>
internal static class Logger
{
    /// <summary>Rotate once the log passes this size, keeping one previous file.</summary>
    private const long MaxBytes = 512 * 1024;

    private static readonly object Gate = new();
    private static string? _logPath;
    private static bool _failed;

    public static string LogPath =>
        _logPath ??= Path.Combine(ConfigManager.ConfigDir, "logs", "pockettray.log");

    /// <summary>Starts a new session block in the log. Call once at startup.</summary>
    public static void StartSession()
    {
        var version = typeof(Logger).Assembly.GetName().Version?.ToString() ?? "unknown";
        Write("====", $"PocketTray {version} starting (pid {Environment.ProcessId}, {Environment.OSVersion})");
        Write("info", $"data folder: {ConfigManager.ConfigDir}");
    }

    public static void Info(string message) => Write("info", message);

    public static void Warn(string message) => Write("warn", message);

    /// <summary>
    /// Logs an exception with its type and message. Stack traces are included
    /// only for unexpected failures (<paramref name="includeStack"/>), since the
    /// expected ones — a missing icon file, an unreadable path — are noise.
    /// </summary>
    public static void Error(string message, Exception ex, bool includeStack = false)
    {
        var detail = $"{message}: {ex.GetType().Name}: {ex.Message}";
        if (includeStack && ex.StackTrace is not null)
        {
            detail += Environment.NewLine + ex.StackTrace;
        }

        Write("error", detail);
    }

    private static void Write(string level, string message)
    {
        if (_failed)
        {
            // The log directory is unwritable (read-only folder, removable drive
            // pulled out). Stop retrying on every event.
            return;
        }

        try
        {
            lock (Gate)
            {
                var dir = Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(dir);
                RotateIfTooLarge();

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _failed = true;
        }
    }

    /// <summary>
    /// Keeps the log bounded: past the size cap the current file becomes
    /// pockettray.prev.log (replacing any older copy) and logging restarts from
    /// empty, so an app that runs for months can't fill the folder.
    /// </summary>
    private static void RotateIfTooLarge()
    {
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length < MaxBytes)
        {
            return;
        }

        var previous = Path.Combine(Path.GetDirectoryName(LogPath)!, "pockettray.prev.log");
        File.Delete(previous);
        File.Move(LogPath, previous);
    }
}
