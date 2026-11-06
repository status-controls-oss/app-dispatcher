using System.ComponentModel;
using AppDispatcher.Core;
using static AppDispatcher.Native;

namespace AppDispatcher;

internal static class Program
{
    const string Caption = "App Dispatcher";

    static int Main(string[] args)
    {
        if (args.Length == 0) return OpenSettings();

        var argument = args[0];
        var isUrl = Uri.TryCreate(argument, UriKind.Absolute, out var url) &&
                    (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
        return isUrl ? OpenUrl(url!) : OpenFile(argument);
    }

    static int OpenFile(string file)
    {
        file = Path.GetFullPath(file);
        if (!File.Exists(file))
            return Fail($"File not found:\n\n{file}");

        var extension = Path.GetExtension(file);
        var rule = AppConfig.Load().FindExtension(extension);
        var targets = TargetResolver.Resolve(rule?.Targets ?? new List<FileTarget>(), file, Environment.ProcessPath, EditorCatalog.FindExe);
        if (targets.Count == 0)
        {
            var answer = MessageBoxW(0, $"No available apps are set up for {extension} files.\n\nOpen Settings?", Caption, MB_YESNO | MB_ICONWARNING | MB_TOPMOST);
            return answer == IDYES ? OpenSettings() : 1;
        }

        var chosen = targets[0];
        if (targets.Count > 1)
        {
            var rows = targets.Select(t => new PickerRow(t.Label, t.Exe)).ToList();
            var last = targets.FindIndex(t => t.Label == rule!.LastTargetLabel);
            var result = PickerWindow.Show(Path.GetFileName(file), file, rows, Math.Max(last, 0), domain: null);
            if (result == null) return 0;
            chosen = targets[result.Value.Row];
        }

        if (!TryLaunch(() => Launcher.Start(chosen.Exe, chosen.Args))) return 1;
        Remember(c =>
        {
            if (c.FindExtension(extension) is { } r) r.LastTargetLabel = chosen.Label;
        });
        return 0;
    }

    static int OpenUrl(Uri url)
    {
        var config = AppConfig.Load();
        var browsers = BrowserDiscovery.Discover();
        var ruleMatch = config.FindRule(url.Host);
        var target = ruleMatch == null ? null : browsers.FirstOrDefault(b => b.Matches(ruleMatch.Browser, ruleMatch.ProfileId));
        if (target != null)
            return TryLaunch(() => target.Launch(url)) ? 0 : 1;

        if (browsers.Count == 0)
            return Fail("No supported browsers (Chrome, Edge, Firefox) were found.");

        var last = browsers.FindIndex(b => config.LastBrowser != null && b.Matches(config.LastBrowser, config.LastProfileId ?? ""));
        var rows = browsers.Select(b => new PickerRow(b.Display, b.ExePath)).ToList();
        var result = PickerWindow.Show("Open link with", url.AbsoluteUri, rows, Math.Max(last, 0), url.Host);
        if (result == null) return 0;

        var chosen = browsers[result.Value.Row];
        if (!TryLaunch(() => chosen.Launch(url))) return 1;
        Remember(c =>
        {
            c.LastBrowser = chosen.Browser;
            c.LastProfileId = chosen.ProfileId;
            if (result.Value.Always) c.SetRule(result.Value.Domain, chosen.Browser, chosen.ProfileId, chosen.ProfileName);
        });
        return 0;
    }

    static int OpenSettings()
    {
        var settings = Path.Combine(AppContext.BaseDirectory, "AppDispatcher.Settings.exe");
        return File.Exists(settings)
            ? (TryLaunch(() => Launcher.Start(settings, Array.Empty<string>())) ? 0 : 1)
            : Fail($"Settings app not found:\n\n{settings}");
    }

    static bool TryLaunch(Action launch)
    {
        try
        {
            launch();
            return true;
        }
        catch (Win32Exception ex)
        {
            if (!Launcher.IsUserCancelled(ex)) Fail($"Couldn't start the app.\n\n{ex.Message}");
            return false;
        }
    }

    /// <summary>Runs after launching, so saving never delays the app opening. Losing this state is harmless.</summary>
    static void Remember(Action<AppConfig> change)
    {
        try { AppConfig.Update(change); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    static int Fail(string message)
    {
        MessageBoxW(0, message, Caption, MB_OK | MB_ICONERROR | MB_TOPMOST);
        return 1;
    }
}
