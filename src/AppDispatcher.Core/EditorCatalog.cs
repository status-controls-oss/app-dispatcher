using Microsoft.Win32;

namespace AppDispatcher.Core;

/// <summary>Editors we know how to find, with the window modes each one actually supports.</summary>
public static class EditorCatalog
{
    const string F = "{file}";

    /// <summary>All picker targets for the editors installed on this machine.</summary>
    public static List<FileTarget> Discover()
    {
        var list = new List<FileTarget>();
        foreach (var (id, find, presets) in Editors)
        {
            var exe = find();
            if (exe == null) continue;
            list.AddRange(presets.Select(p => new FileTarget { Label = p.Label, Exe = exe, Args = p.Args.ToList(), AppId = id }));
        }
        return list;
    }

    /// <summary>Finds a catalog app's exe again, e.g. after an update moved it to a versioned folder.</summary>
    public static string? FindExe(string appId) => Editors.FirstOrDefault(e => e.Id == appId).Find?.Invoke();

    static readonly (string Id, Func<string?> Find, (string Label, string[] Args)[] Presets)[] Editors =
    {
        ("zed", () => FirstExisting(Local(@"Programs\Zed\bin\zed.exe")), new[]
        {
            ("Zed — existing window", new[] { "-e", F }),
            ("Zed — new window", new[] { "-n", F }),
        }),
        // Rider ignores --temp-project and fails on --edit, so a separate window means opening a solution or folder.
        ("rider", FindRider, new[]
        {
            ("Rider — current window", new[] { F }),
            ("Rider — {solutionName}", new[] { "{solution}", F }),
            ("Rider — file's folder in new window", new[] { "{dir}", F }),
        }),
        ("vscode", () => FirstExisting(Local(@"Programs\Microsoft VS Code\Code.exe"), Pf(@"Microsoft VS Code\Code.exe")), new[]
        {
            ("VS Code — existing window", new[] { "-r", F }),
            ("VS Code — new window", new[] { "-n", F }),
        }),
        ("notepad++", () => AppPath("notepad++.exe") ?? FirstExisting(Pf(@"Notepad++\notepad++.exe")), new[]
        {
            ("Notepad++", new[] { F }),
            ("Notepad++ — new instance", new[] { "-multiInst", "-nosession", F }),
        }),
        ("devenv", () => AppPath("devenv.exe"), new[]
        {
            ("Visual Studio — existing instance", new[] { "/Edit", F }),
            ("Visual Studio — new instance", new[] { F }),
        }),
    };

    /// <summary>JetBrains keeps old version folders around, so pick the most recently updated exe.</summary>
    static string? FindRider()
    {
        var roots = new[]
        {
            Pf("JetBrains"),
            Local(@"Programs"),
            Local(@"JetBrains\Toolbox\apps\Rider"),
            Local(@"JetBrains\Toolbox\apps\rider"),
        };
        return roots.Where(Directory.Exists)
            .SelectMany(r => SafeFind(r, "rider64.exe"))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>Checks {root}\*\bin and, for Toolbox's channel layout, {root}\*\*\bin.</summary>
    static IEnumerable<string> SafeFind(string root, string exe)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true };
        foreach (var a in Directory.EnumerateDirectories(root, "*", options))
        {
            var direct = Path.Combine(a, "bin", exe);
            if (File.Exists(direct)) yield return direct;
            foreach (var b in Directory.EnumerateDirectories(a, "*", options))
            {
                var nested = Path.Combine(b, "bin", exe);
                if (File.Exists(nested)) yield return nested;
            }
        }
    }

    static string? AppPath(string exe)
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var k = root.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
            if (k?.GetValue(null) is string p && File.Exists(p.Trim('"'))) return p.Trim('"');
        }
        return null;
    }

    static string? FirstExisting(params string[] paths) => paths.FirstOrDefault(File.Exists);

    static string Local(string relative) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), relative);

    static string Pf(string relative) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), relative);
}
