using System.Diagnostics;
using Microsoft.Win32;

namespace AppDispatcher.Core;

/// <summary>An app Windows associates with a file type, before checking that it can run.</summary>
public sealed record RawHandler(string? FriendlyName, string Command);

/// <summary>
/// Finds the apps Windows already has for a file type, so they can be offered in Settings.
/// This reads the registry and version info, so it must never run in the picker's file path.
/// </summary>
public static class FileHandlers
{
    const string File_ = "{file}";

    public static List<FileTarget> Discover(string extension, string? selfExe) =>
        Build(Registered(extension), selfExe, Describe);

    /// <param name="describe">The display name inside an exe, if it has one.</param>
    public static List<FileTarget> Build(IEnumerable<RawHandler> handlers, string? selfExe, Func<string, string?> describe)
    {
        var rows = new List<(string Label, string Exe, List<string> Args)>();
        foreach (var handler in handlers)
        {
            if (ParseCommand(handler.Command) is not { } command) continue;
            var (exe, args) = command;
            if (!File.Exists(exe) || !Launcher.CanLaunch(exe) || string.Equals(exe, selfExe, StringComparison.OrdinalIgnoreCase)) continue;
            if (rows.Any(r => string.Equals(r.Exe, exe, StringComparison.OrdinalIgnoreCase) && r.Args.SequenceEqual(args))) continue;

            var name = new[] { handler.FriendlyName, describe(exe) }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            rows.Add((name ?? Path.GetFileNameWithoutExtension(exe), exe, args));
        }

        // Side-by-side versions of an app describe themselves identically, but their folders usually differ.
        return rows
            .Select(r => new FileTarget
            {
                Label = rows.Count(o => string.Equals(o.Label, r.Label, StringComparison.OrdinalIgnoreCase)) > 1
                    ? Path.GetFileName(Path.GetDirectoryName(r.Exe)) ?? r.Label
                    : r.Label,
                Exe = r.Exe,
                Args = r.Args,
            })
            .OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Splits a registry command line into its program and picker arguments.</summary>
    public static (string Exe, List<string> Args)? ParseCommand(string command)
    {
        command = command.Trim();
        if (BrowserCatalog.ExeFromCommand(command) is not { } exe) return null;
        var rest = command[(command.IndexOf(exe, StringComparison.OrdinalIgnoreCase) + exe.Length)..].TrimStart('"');
        foreach (var token in new[] { "%1", "%L", "%l", "%V", "%v" }) rest = rest.Replace(token, File_);
        return (exe, TargetResolver.ParseArgs(rest).Where(a => a != "%*").ToList());
    }

    static string? Describe(string exe)
    {
        try { return FileVersionInfo.GetVersionInfo(exe).FileDescription; }
        catch (FileNotFoundException) { return null; }
    }

    static IEnumerable<RawHandler> Registered(string extension)
    {
        var progIds = new List<string>();
        void AddApplication(string exeName) => progIds.Add($@"Applications\{exeName}");

        using (var type = Registry.ClassesRoot.OpenSubKey(extension))
        {
            if (type?.GetValue(null) is string main) progIds.Add(main);
            using (var ids = type?.OpenSubKey("OpenWithProgids")) progIds.AddRange(ids?.GetValueNames() ?? Array.Empty<string>());
            using (var list = type?.OpenSubKey("OpenWithList"))
                foreach (var name in list?.GetSubKeyNames() ?? Array.Empty<string>()) AddApplication(name);
        }
        using (var user = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}"))
        {
            using (var choice = user?.OpenSubKey("UserChoice"))
                if (choice?.GetValue("ProgId") is string chosen) progIds.Add(chosen);
            using (var ids = user?.OpenSubKey("OpenWithProgids")) progIds.AddRange(ids?.GetValueNames() ?? Array.Empty<string>());
            using (var list = user?.OpenSubKey("OpenWithList"))
                foreach (var value in list?.GetValueNames() ?? Array.Empty<string>())
                    if (value != "MRUList" && list!.GetValue(value) is string name) AddApplication(name);
        }

        // By name as well as by path, so that a moved or second copy of the dispatcher can't offer itself.
        var candidates = progIds
            .Where(p => p.Length > 0 && !p.StartsWith("AppDispatcher.", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var progId in candidates)
        {
            using var key = Registry.ClassesRoot.OpenSubKey(progId);
            using var open = key?.OpenSubKey(@"shell\open");
            using var command = open?.OpenSubKey("command");
            // Store apps have no command line here, so they can't be launched this way.
            if (command?.GetValue(null) is not string line) continue;
            var friendly = open?.GetValue("FriendlyAppName") as string ?? key?.GetValue("FriendlyAppName") as string;
            // "@file.dll,-123" points at a localized resource, not a name.
            yield return new RawHandler(friendly?.StartsWith('@') == true ? null : friendly, line);
        }
    }
}
