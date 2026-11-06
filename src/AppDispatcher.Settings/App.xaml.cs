using System.IO;
using System.Windows;
using AppDispatcher.Core;

namespace AppDispatcher.Settings;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--unregister"))
        {
            Shutdown(Unregister());
            return;
        }
        new SettingsWindow().Show();
    }

    /// <summary>
    /// Used by the uninstaller. Only the copy that's registered may remove the registration,
    /// so removing some other copy can't break the one Windows is using.
    /// </summary>
    static int Unregister()
    {
        try
        {
            if (Registration.IsRegistered(Path.Combine(AppContext.BaseDirectory, "AppDispatcher.exe")))
            {
                Registration.Unregister();
                Shortcut.Delete(Shortcut.StartMenuPath);
            }
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return 1;
        }
    }
}
