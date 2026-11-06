using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AppDispatcher.Core;

public static partial class Launcher
{
    const int AnyProcess = -1, ErrorElevationRequired = 740, ErrorCancelled = 1223;

    [LibraryImport("user32")]
    private static partial int AllowSetForegroundWindow(int processId);

    public static void Start(string exe, IEnumerable<string> args)
    {
        // Without this, an already-running editor receiving the file can't take focus and only flashes in the taskbar.
        AllowSetForegroundWindow(AnyProcess);

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true, // CLI launchers such as Zed's are console apps and would flash a console.
            WorkingDirectory = Path.GetDirectoryName(exe) ?? "", // Not the file's folder, which would lock it.
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var _ = Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorElevationRequired)
        {
            // The app is marked "Run as administrator". Only ShellExecute can show the UAC prompt for it.
            psi.UseShellExecute = true;
            using var _ = Process.Start(psi);
        }
    }

    /// <summary>True when the launch failed only because the user declined the UAC prompt.</summary>
    public static bool IsUserCancelled(Win32Exception ex) => ex.NativeErrorCode == ErrorCancelled;
}
