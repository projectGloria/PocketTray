using System.Runtime.InteropServices;

namespace PocketTray;

/// <summary>
/// Applies a dark palette to PocketTray's own windows and menus when Windows
/// is set to dark mode. WinForms has no built-in dark theme, so this hand-rolls
/// one: solid dark colors on every control, a dark ContextMenuStrip renderer,
/// the DWM immersive-dark titlebar, and the undocumented-but-widely-used
/// "DarkMode_Explorer" visual style for ListView headers/scrollbars (the same
/// trick most WinForms dark-theme libraries use, since ListView headers ignore
/// BackColor/ForeColor otherwise).
///
/// Native common dialogs (folder/file pickers) already follow the system
/// theme on their own. Win32 MessageBox does not, and there's no supported
/// way to theme it without deeper hooking — left light as a known limitation.
/// </summary>
internal static class DarkThemeHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    private static readonly Color WindowBackground = Color.FromArgb(32, 32, 32);
    private static readonly Color ControlBackground = Color.FromArgb(45, 45, 48);
    private static readonly Color Foreground = Color.FromArgb(240, 240, 240);
    private static readonly Color BorderColor = Color.FromArgb(80, 80, 80);
    private static readonly Color SelectionColor = Color.FromArgb(62, 62, 66);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    public static bool IsSystemDarkModeEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Themes a whole form (and everything inside it) if the system is in dark mode. No-op otherwise.</summary>
    public static void Apply(Form form)
    {
        if (!IsSystemDarkModeEnabled())
        {
            return;
        }

        form.BackColor = WindowBackground;
        form.ForeColor = Foreground;
        ApplyToControls(form.Controls);
        EnableDarkTitleBarWhenReady(form);
    }

    /// <summary>Themes a standalone context menu (e.g. a tray icon's menu) not attached to a themed form.</summary>
    public static void ApplyToMenu(ContextMenuStrip menu)
    {
        if (!IsSystemDarkModeEnabled())
        {
            return;
        }

        menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColorTable());
        menu.BackColor = ControlBackground;
        menu.ForeColor = Foreground;

        foreach (ToolStripItem item in menu.Items)
        {
            item.ForeColor = Foreground;
        }
    }

    private static void EnableDarkTitleBarWhenReady(Form form)
    {
        if (form.IsHandleCreated)
        {
            EnableDarkTitleBar(form.Handle);
        }
        else
        {
            form.HandleCreated += (_, _) => EnableDarkTitleBar(form.Handle);
        }
    }

    private static void EnableDarkTitleBar(IntPtr handle)
    {
        var useDark = 1;
        if (DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref useDark, sizeof(int));
        }
    }

    private static void ApplyToControls(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            switch (control)
            {
                case Button button:
                    button.BackColor = ControlBackground;
                    button.ForeColor = Foreground;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = BorderColor;
                    break;

                case TextBox textBox:
                    textBox.BackColor = ControlBackground;
                    textBox.ForeColor = Foreground;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case ListBox listBox:
                    listBox.BackColor = ControlBackground;
                    listBox.ForeColor = Foreground;
                    break;

                case ListView listView:
                    listView.BackColor = ControlBackground;
                    listView.ForeColor = Foreground;
                    ApplyDarkNativeTheme(listView);
                    break;

                case CheckBox checkBox:
                    checkBox.ForeColor = Foreground;
                    break;

                case RadioButton radioButton:
                    radioButton.ForeColor = Foreground;
                    break;

                case Label label:
                    label.ForeColor = Foreground;
                    break;

                case Panel or TableLayoutPanel or FlowLayoutPanel or GroupBox:
                    control.BackColor = WindowBackground;
                    control.ForeColor = Foreground;
                    break;
            }

            if (control.ContextMenuStrip is { } menu)
            {
                ApplyToMenu(menu);
            }

            if (control.HasChildren)
            {
                ApplyToControls(control.Controls);
            }
        }
    }

    /// <summary>Dark-themes native chrome (headers, scrollbars) that BackColor/ForeColor alone can't reach.</summary>
    private static void ApplyDarkNativeTheme(Control control)
    {
        void Apply() => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);

        if (control.IsHandleCreated)
        {
            Apply();
        }
        else
        {
            control.HandleCreated += (_, _) => Apply();
        }
    }

    private sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => SelectionColor;
        public override Color MenuItemSelectedGradientBegin => SelectionColor;
        public override Color MenuItemSelectedGradientEnd => SelectionColor;
        public override Color MenuItemPressedGradientBegin => SelectionColor;
        public override Color MenuItemPressedGradientEnd => SelectionColor;
        public override Color MenuItemBorder => BorderColor;
        public override Color MenuBorder => BorderColor;
        public override Color ToolStripDropDownBackground => ControlBackground;
        public override Color ImageMarginGradientBegin => ControlBackground;
        public override Color ImageMarginGradientMiddle => ControlBackground;
        public override Color ImageMarginGradientEnd => ControlBackground;
        public override Color SeparatorDark => BorderColor;
        public override Color SeparatorLight => BorderColor;
    }
}
