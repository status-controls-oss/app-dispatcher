using System.IO;
using System.Text;
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
    /// Writes what to choose again in Windows to standard output, one line each, for the uninstaller to show.
    /// </summary>
    static int Unregister()
    {
        try
        {
            if (Registration.IsRegistered(Path.Combine(AppContext.BaseDirectory, "AppDispatcher.exe")))
            {
                var lines = Registration.RestoreLines(AppConfig.Load());
                Registration.Unregister();
                Shortcut.Delete(Shortcut.StartMenuPath);
                WriteLines(lines);
            }
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return 1;
        }
    }

    /// <summary>The registration is already gone by now, so failing to report it mustn't fail the uninstall.</summary>
    static void WriteLines(List<string> lines)
    {
        try
        {
            // A WinExe has no console, so Console.Out's encoding can't be relied on.
            using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            foreach (var line in lines) output.WriteLine(line);
        }
        catch (IOException) { }
    }
}
