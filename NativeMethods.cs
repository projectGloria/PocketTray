using System.Runtime.InteropServices;
using System.Text;

namespace PocketTray;

internal static class NativeMethods
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    public const int SW_RESTORE = 9;
    public const int SW_SHOW = 5;

    /// <summary>
    /// Returns every visible top-level window handle owned by the given process id,
    /// paired with its title text.
    /// </summary>
    public static List<(IntPtr Handle, string Title)> GetVisibleWindowsForProcess(uint processId)
    {
        var results = new List<(IntPtr, string)>();

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid != processId || !IsWindowVisible(hWnd))
            {
                return true;
            }

            var length = GetWindowTextLength(hWnd);
            if (length == 0)
            {
                return true;
            }

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            results.Add((hWnd, sb.ToString()));
            return true;
        }, IntPtr.Zero);

        return results;
    }

    /// <summary>
    /// Enumerates every visible top-level window on the system exactly once and
    /// groups the titles by owning process id. Used instead of calling
    /// <see cref="GetVisibleWindowsForProcess"/> once per process — that pattern
    /// re-scans every window on screen for every single process, turning an
    /// O(processCount) job into an O(processCount * windowCount) storm of
    /// EnumWindows passes (~250 processes x ~80 windows = ~20,000 native calls
    /// just to list running windows). This does the same job in one pass.
    /// </summary>
    public static Dictionary<int, List<string>> GetAllVisibleWindowTitlesByProcess()
    {
        var results = new Dictionary<int, List<string>>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
            {
                return true;
            }

            var length = GetWindowTextLength(hWnd);
            if (length == 0)
            {
                return true;
            }

            GetWindowThreadProcessId(hWnd, out var pid);

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);

            if (!results.TryGetValue((int)pid, out var titles))
            {
                titles = new List<string>();
                results[(int)pid] = titles;
            }
            titles.Add(sb.ToString());

            return true;
        }, IntPtr.Zero);

        return results;
    }

    /// <summary>Restores (if minimized) and brings a window to the foreground.</summary>
    public static void FocusWindow(IntPtr hWnd)
    {
        if (!IsWindow(hWnd))
        {
            return;
        }

        if (IsIconic(hWnd))
        {
            ShowWindowAsync(hWnd, SW_RESTORE);
        }
        else
        {
            ShowWindowAsync(hWnd, SW_SHOW);
        }

        SetForegroundWindow(hWnd);
    }
}
