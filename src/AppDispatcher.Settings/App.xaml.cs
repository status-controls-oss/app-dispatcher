using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
        if (AlreadyOpen())
        {
            Shutdown(0);
            return;
        }
        // The installer asks the user, then passes their answer here so that only this app writes the config.
        foreach (var (arg, on) in new[] { ($"{RecordArg}=yes", true), ($"{RecordArg}=no", false) })
            if (e.Args.Contains(arg)) SaveRecordingChoice(on);
        new SettingsWindow().Show();
    }

    const string RecordArg = "--record-previous-defaults";

    /// <summary>Held while this window is open, so the picker's gear can't open a second one.</summary>
    static Mutex? _instance;

    /// <summary>
    /// Whether this install's Settings is already open, in which case that window is brought forward.
    /// Per folder, so a test build doesn't stand in for the installed one.
    /// </summary>
    static bool AlreadyOpen()
    {
        var folder = AppContext.BaseDirectory.TrimEnd('\\').ToLowerInvariant();
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder)))[..16];
        _instance = new Mutex(true, $@"Local\AppDispatcher.Settings.{id}", out var created);
        if (created) return false;
        BringForward();
        return true;
    }

    static void BringForward()
    {
        var self = Environment.ProcessPath;
        var myId = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName("AppDispatcher.Settings"))
        {
            using (process)
            {
                if (process.Id == myId) continue;
                string? path;
                try { path = process.MainModule?.FileName; }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { continue; }
                var window = process.MainWindowHandle;
                if (window == 0 || !string.Equals(path, self, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsIconic(window)) ShowWindow(window, SwRestore);
                SetForegroundWindow(window);
                return;
            }
        }
    }

    const int SwRestore = 9;

    [DllImport("user32")] static extern bool IsIconic(nint window);
    [DllImport("user32")] static extern bool ShowWindow(nint window, int command);
    [DllImport("user32")] static extern bool SetForegroundWindow(nint window);

    static void SaveRecordingChoice(bool on)
    {
        try
        {
            AppConfig.Update(c =>
            {
                c.RecordPreviousDefaults = on;
                if (!on) c.PreviousDefaults.Clear();
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings asks again on first run, as the choice is still unset.
        }
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
