namespace PocketTray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstanceMutex = new Mutex(initiallyOwned: true, "PocketTray-SingleInstance-3f6b2b7e", out var createdNew);
        if (!createdNew)
        {
            Logger.Info("another instance is already running - exiting");
            MessageBox.Show(
                "PocketTray is already running. Check the system tray.",
                "PocketTray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Logger.StartSession();

        // PocketTray lives in the tray with no window to show a crash in, so an
        // unhandled exception would otherwise vanish silently. Log both the UI
        // thread's and the domain's, then let the default handling proceed.
        Application.ThreadException += (_, e) =>
            Logger.Error("unhandled UI exception", e.Exception, includeStack: true);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Logger.Error("unhandled exception", ex, includeStack: true);
            }
        };

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
        Logger.Info("exited normally");
    }
}
