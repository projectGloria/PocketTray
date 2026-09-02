using System.Runtime.InteropServices;

namespace PocketTray;

internal static class IconHelper
{
    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Gets the actual shell icon for a path (folder or file). Tries the real,
    /// on-disk path first (so custom folder icons via desktop.ini are honored),
    /// then falls back to a generic icon for that item type, then to a bundled
    /// default if even that fails.
    /// </summary>
    public static Icon GetIconForPath(string path, bool isDirectory)
    {
        try
        {
            if ((isDirectory && Directory.Exists(path)) || (!isDirectory && File.Exists(path)))
            {
                var icon = TryGetShellIcon(path, useFileAttributes: false, isDirectory);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch
        {
            // Fall through to generic icon below.
        }

        try
        {
            // Generic icon by "type" only — works even if the real path is missing.
            var generic = TryGetShellIcon(isDirectory ? "folder" : ".exe", useFileAttributes: true, isDirectory);
            if (generic is not null)
            {
                return generic;
            }
        }
        catch
        {
            // Fall through to bundled default.
        }

        return SystemIcons.Application;
    }

    private static Icon? TryGetShellIcon(string path, bool useFileAttributes, bool isDirectory)
    {
        var shinfo = new SHFILEINFO();
        uint flags = SHGFI_ICON | SHGFI_SMALLICON;
        uint attributes = 0;

        if (useFileAttributes)
        {
            flags |= SHGFI_USEFILEATTRIBUTES;
            attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        }

        var result = SHGetFileInfo(path, attributes, ref shinfo, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (result == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            // Clone so the icon survives after we destroy the shell-owned handle.
            using var shellIcon = Icon.FromHandle(shinfo.hIcon);
            return (Icon)shellIcon.Clone();
        }
        finally
        {
            DestroyIcon(shinfo.hIcon);
        }
    }

    /// <summary>
    /// Builds a simple vertical-bar icon (used as the PocketTray separator icon
    /// when no custom icon file is supplied).
    /// </summary>
    public static Icon CreateVerticalLineIcon(Color color, int size = 16, int lineWidth = 3)
    {
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            var x = (size - lineWidth) / 2;
            g.FillRectangle(brush, x, 1, lineWidth, size - 2);
        }

        var hIcon = bitmap.GetHicon();
        try
        {
            using var tempIcon = Icon.FromHandle(hIcon);
            return (Icon)tempIcon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    /// <summary>
    /// Renders any Image (including one frame of an animated GIF) into a
    /// tray-sized 16x16 Icon the caller owns.
    /// </summary>
    public static Icon IconFromImage(Image image)
    {
        using var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, 0, 0, 16, 16);
        }

        var hIcon = bitmap.GetHicon();
        try
        {
            using var tempIcon = Icon.FromHandle(hIcon);
            return (Icon)tempIcon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    /// <summary>
    /// Loads a custom icon image (.ico, .png, .jpg, .bmp, ...) from disk at
    /// <paramref name="customPath"/> if present, otherwise disposes and returns
    /// <paramref name="fallback"/> unchanged. Used for both the separator icon
    /// and per-item custom icons.
    /// </summary>
    public static Icon LoadCustomOrDefault(string? customPath, Icon fallback)
    {
        if (string.IsNullOrWhiteSpace(customPath) || !File.Exists(customPath))
        {
            return fallback;
        }

        try
        {
            Icon loaded;

            if (string.Equals(Path.GetExtension(customPath), ".ico", StringComparison.OrdinalIgnoreCase))
            {
                // .ico files can hold multiple sizes; Image.FromFile doesn't
                // reliably decode them, so load via the Icon constructor and
                // pick the size closest to a tray icon.
                using var sourceIcon = new Icon(customPath);
                using var sized = new Icon(sourceIcon, 16, 16);
                loaded = (Icon)sized.Clone();
            }
            else
            {
                using var image = Image.FromFile(customPath);
                using var bitmap = new Bitmap(image, new Size(16, 16));
                var hIcon = bitmap.GetHicon();
                try
                {
                    using var tempIcon = Icon.FromHandle(hIcon);
                    loaded = (Icon)tempIcon.Clone();
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }

            fallback.Dispose();
            return loaded;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Copies a user-picked icon/image file into the portable "icons" folder
    /// next to config.json (giving it a random name to avoid collisions), and
    /// returns the path to store on the item, relative to
    /// <see cref="ConfigManager.ConfigDir"/> so it stays valid if the whole
    /// PocketTray folder is moved.
    /// </summary>
    public static string SaveCustomIcon(string sourceFilePath)
    {
        var iconsDir = Path.Combine(ConfigManager.ConfigDir, "icons");
        Directory.CreateDirectory(iconsDir);

        var fileName = Guid.NewGuid().ToString("N") + Path.GetExtension(sourceFilePath);
        File.Copy(sourceFilePath, Path.Combine(iconsDir, fileName), overwrite: true);

        return Path.Combine("icons", fileName);
    }

    /// <summary>Removes a previously saved custom icon file. Best effort — a leftover file isn't harmful.</summary>
    public static void DeleteCustomIconIfExists(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        try
        {
            var fullPath = Path.Combine(ConfigManager.ConfigDir, relativePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }
}
