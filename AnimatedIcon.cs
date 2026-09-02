using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PocketTray;

/// <summary>
/// An animated tray icon: a decoded GIF's frames plus a timer that swaps
/// <see cref="NotifyIcon.Icon"/> from one frame to the next. Windows gives no
/// native "animated tray icon" — the animation is literally us re-assigning a
/// new 16x16 HICON on every frame, so we pre-render every frame once at
/// construction and only ever hand out already-built icons at run time.
/// </summary>
internal sealed class AnimatedIcon : IDisposable
{
    private readonly List<Icon> _frames;
    private readonly List<int> _delaysMs;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Action<Icon> _apply;

    private int _index;
    private bool _disposed;

    private AnimatedIcon(List<Icon> frames, List<int> delaysMs, Action<Icon> apply)
    {
        _frames = frames;
        _delaysMs = delaysMs;
        _apply = apply;

        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(20, _delaysMs[0]) };
        _timer.Tick += (_, _) => Advance();
    }

    /// <summary>The frame currently on screen. Owned by this instance — do not dispose.</summary>
    public Icon CurrentFrame => _frames[_index];

    /// <summary>
    /// Decodes <paramref name="path"/> as an animated image and returns an
    /// animator, or null if the file isn't animated (single-frame GIF, PNG,
    /// .ico, unreadable file...) — in which case the caller should fall back to
    /// the ordinary static-icon path.
    /// </summary>
    public static AnimatedIcon? TryCreate(string? path, Action<Icon> apply)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        List<Icon>? frames = null;

        try
        {
            using var image = Image.FromFile(path);

            var dimension = new FrameDimension(image.FrameDimensionsList[0]);
            var frameCount = image.GetFrameCount(dimension);
            if (frameCount < 2)
            {
                Logger.Info($"icon '{Path.GetFileName(path)}' is a single frame - using it as a static icon");
                return null;
            }

            var delays = ReadFrameDelays(image, frameCount);
            frames = new List<Icon>(frameCount);

            for (var i = 0; i < frameCount; i++)
            {
                image.SelectActiveFrame(dimension, i);
                frames.Add(IconHelper.IconFromImage(image));
            }

            Logger.Info($"animating icon '{Path.GetFileName(path)}': {frameCount} frames, {delays.Sum()}ms per loop");
            return new AnimatedIcon(frames, delays, apply);
        }
        catch (Exception ex) when (ex is OutOfMemoryException or ArgumentException or ExternalException or IOException)
        {
            Logger.Error($"couldn't decode '{Path.GetFileName(path)}' as an animated image", ex);

            // Not a decodable/animated image — caller falls back to a static icon.
            if (frames is not null)
            {
                foreach (var frame in frames)
                {
                    frame.Dispose();
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Per-frame delays from the GIF's Graphic Control Extension blocks, in
    /// milliseconds. The property stores each delay as a 4-byte little-endian
    /// value in hundredths of a second.
    /// </summary>
    private static List<int> ReadFrameDelays(Image image, int frameCount)
    {
        var delays = new List<int>(frameCount);

        byte[]? raw = null;
        try
        {
            raw = image.GetPropertyItem(0x5100)?.Value; // PropertyTagFrameDelay
        }
        catch (ArgumentException)
        {
            // No delay property (not a GIF) — fall through to the default below.
        }

        for (var i = 0; i < frameCount; i++)
        {
            var offset = i * 4;
            var hundredths = raw is not null && offset + 4 <= raw.Length
                ? BitConverter.ToInt32(raw, offset)
                : 0;

            // Browsers treat 0 (and 1) as "as fast as sensible" — 100ms, the de
            // facto default — rather than a zero-length frame that would spin
            // the timer flat out.
            delays.Add(hundredths <= 1 ? 100 : hundredths * 10);
        }

        return delays;
    }

    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        _index = 0;
        _apply(_frames[0]);
        _timer.Interval = Math.Max(20, _delaysMs[0]);
        _timer.Start();
    }

    /// <summary>
    /// Stops/restarts the frame timer without discarding the decoded frames —
    /// used while the tray icon is hidden, so a hidden pin costs nothing.
    /// </summary>
    public void SetPaused(bool paused)
    {
        if (_disposed)
        {
            return;
        }

        if (paused)
        {
            _timer.Stop();
        }
        else
        {
            _timer.Start();
        }
    }

    private void Advance()
    {
        if (_disposed)
        {
            return;
        }

        _index = (_index + 1) % _frames.Count;
        _timer.Interval = Math.Max(20, _delaysMs[_index]);
        _apply(_frames[_index]);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Dispose();

        foreach (var frame in _frames)
        {
            frame.Dispose();
        }
    }
}
