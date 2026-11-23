using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

// Load Windows DLLs only from System32, never from the app's folder, where a planted copy would run.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace AppDispatcher.Core;

public static partial class Launcher
{
    const int AnyProcess = -1, ErrorBadExeFormat = 193, ErrorElevationRequired = 740, ErrorCancelled = 1223;

    /// <summary>
    /// Programs that parse their arguments as commands. Windows runs .cmd and .bat files through cmd.exe,
    /// which treats characters such as &amp; as commands, and .NET's argument quoting doesn't guard against
    /// that. The others do the same with their own syntax, such as wt.exe with ';' or powershell.exe with
    /// a quote inside -Command. A crafted link or file name could otherwise run commands.
    /// </summary>
    static readonly HashSet<string> CommandHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "powershell_ise.exe", "pwsh.exe", "wt.exe", "conhost.exe", "openconsole.exe",
        "wsl.exe", "bash.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe",
        "forfiles.exe", "msiexec.exe", "schtasks.exe",
    };

    /// <summary>
    /// Only real programs, given by full path, can be launched. A relative path would be found in the
    /// picker's working directory, which Explorer sets to the opened file's folder.
    /// </summary>
    public static bool CanLaunch(string exe) =>
        Path.IsPathFullyQualified(exe) &&
        string.Equals(Path.GetExtension(exe), ".exe", StringComparison.OrdinalIgnoreCase) &&
        !CommandHosts.Contains(Path.GetFileName(exe));

    [LibraryImport("user32")]
    private static partial int AllowSetForegroundWindow(int processId);

    public static void Start(string exe, IEnumerable<string> args)
    {
        if (!CanLaunch(exe))
            throw new Win32Exception(ErrorBadExeFormat, $"Only .exe programs given by full path can be opened, not scripts or programs that run commands:\n{exe}");

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
