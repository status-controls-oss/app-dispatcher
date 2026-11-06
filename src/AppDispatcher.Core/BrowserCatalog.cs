using Microsoft.Win32;

namespace AppDispatcher.Core;

/// <summary>Where to look for browsers. Separate so that tests never read the real machine.</summary>
public sealed record BrowserRoots(string Local, string Roaming, IReadOnlyList<string> Programs)
{
    public static BrowserRoots ForCurrentUser()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new BrowserRoots(local, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            local,
            Path.Combine(local, "Programs"),
        });
    }
}

/// <summary>An entry in Windows' own list of browsers, the one behind Default Apps.</summary>
public sealed record RegisteredBrowser(string Key, string Name, string Command);

/// <summary>
/// Finds browsers beyond the three the picker detects itself, so they can be offered in Settings.
/// This reads the registry and several folders, so it must never run in the picker's link path.
/// </summary>
public static class BrowserCatalog
{
    enum Engine { Chromium, Firefox, NoProfiles }

    /// <param name="Name">Matched against the start of the name a browser registers with Windows.</param>
    /// <param name="Install">Path of the exe under a Programs root.</param>
    /// <param name="Data">Profile folder: under Local for Chromium, under Roaming for Firefox.</param>
    sealed record Known(string Name, Engine Engine, string Install, string? Data = null);

    // "Opera GX" comes before "Opera" because registered names are matched by their start.
    static readonly Known[] Catalog =
    {
        new("Brave", Engine.Chromium, @"BraveSoftware\Brave-Browser\Application\brave.exe", @"BraveSoftware\Brave-Browser\User Data"),
        new("Vivaldi", Engine.Chromium, @"Vivaldi\Application\vivaldi.exe", @"Vivaldi\User Data"),
        new("Chromium", Engine.Chromium, @"Chromium\Application\chrome.exe", @"Chromium\User Data"),
        new("Thorium", Engine.Chromium, @"Thorium\Application\thorium.exe", @"Thorium\User Data"),
        new("Yandex", Engine.Chromium, @"Yandex\YandexBrowser\Application\browser.exe", @"Yandex\YandexBrowser\User Data"),
        new("Opera GX", Engine.NoProfiles, @"Opera GX\launcher.exe"),
        new("Opera", Engine.NoProfiles, @"Opera\launcher.exe"),
        new("Waterfox", Engine.Firefox, @"Waterfox\waterfox.exe", "Waterfox"),
        new("LibreWolf", Engine.Firefox, @"LibreWolf\librewolf.exe", "librewolf"),
        new("Floorp", Engine.Firefox, @"Ablaze Floorp\floorp.exe", "Floorp"),
        new("Zen", Engine.Firefox, @"Zen Browser\zen.exe", "zen"),
        new("Pale Moon", Engine.Firefox, @"Pale Moon\palemoon.exe", @"Moonchild Productions\Pale Moon"),
        new("Mullvad Browser", Engine.NoProfiles, @"Mullvad Browser\Browser\mullvadbrowser.exe"),
    };

    // Registered as browsers, but not useful as a row: IE only redirects, and the others are link pickers themselves.
    static readonly HashSet<string> NeverOffered = new(StringComparer.OrdinalIgnoreCase) { "IEXPLORE.EXE", "AppDispatcher", "WebDispatch" };

    public static List<CustomBrowser> Detect(IEnumerable<string> skipExes) =>
        Detect(BrowserRoots.ForCurrentUser(), RegisteredBrowsers(), skipExes);

    /// <param name="skipExes">Programs that shouldn't be offered, such as browsers the picker already lists.</param>
    public static List<CustomBrowser> Detect(BrowserRoots roots, IEnumerable<RegisteredBrowser> registered, IEnumerable<string> skipExes)
    {
        var found = new List<CustomBrowser>();
        var seen = new HashSet<string>(skipExes, StringComparer.OrdinalIgnoreCase);
        var candidates = registered
            .Where(r => !NeverOffered.Contains(r.Key))
            .Select(r => (Browser: r, Exe: ExeFromCommand(r.Command)))
            .Where(c => c.Exe != null && File.Exists(c.Exe))
            .ToList();

        foreach (var known in Catalog)
        {
            // Windows' answer goes first, as it also covers an install in a custom folder.
            var exe = candidates
                .Where(c => c.Browser.Name.StartsWith(known.Name, StringComparison.OrdinalIgnoreCase) && !seen.Contains(c.Exe!))
                .Select(c => c.Exe)
                .FirstOrDefault()
                ?? roots.Programs.Select(p => Path.Combine(p, known.Install)).FirstOrDefault(File.Exists);
            if (exe == null || !seen.Add(exe)) continue;
            found.AddRange(Rows(known, exe, roots));
        }

        foreach (var (browser, exe) in candidates)
        {
            if (seen.Add(exe!))
                found.Add(new CustomBrowser { Label = browser.Name.Length > 0 ? browser.Name : browser.Key, Exe = exe!, Args = new() { BrowserTarget.UrlPlaceholder } });
        }
        return found;
    }

    static IEnumerable<CustomBrowser> Rows(Known known, string exe, BrowserRoots roots)
    {
        var profiles = known.Engine switch
        {
            Engine.Chromium => BrowserDiscovery.ChromiumProfiles(Path.Combine(roots.Local, known.Data!)),
            Engine.Firefox => BrowserDiscovery.FirefoxProfiles(Path.Combine(roots.Roaming, known.Data!)),
            _ => new List<(string Id, string Name)>(),
        };
        // A browser that hasn't been run yet has no profiles, but can still open a link.
        if (profiles.Count == 0)
            return new[] { new CustomBrowser { Label = known.Name, Exe = exe, Args = new() { BrowserTarget.UrlPlaceholder } } };

        return profiles.Select(p => new CustomBrowser
        {
            Label = $"{known.Name} — {p.Name}",
            Exe = exe,
            Args = known.Engine == Engine.Firefox
                ? new() { "-P", p.Id, "-url", BrowserTarget.UrlPlaceholder }
                : new() { $"--profile-directory={p.Id}", BrowserTarget.UrlPlaceholder },
        });
    }

    /// <summary>The program in a registry command line, which may be quoted and may have arguments.</summary>
    public static string? ExeFromCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        var exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe < 0 ? null : command[..(exe + 4)];
    }

    static IEnumerable<RegisteredBrowser> RegisteredBrowsers()
    {
        const string Clients = @"SOFTWARE\Clients\StartMenuInternet";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var clients = root.OpenSubKey(Clients);
            if (clients == null) continue;
            foreach (var key in clients.GetSubKeyNames())
            {
                using var client = clients.OpenSubKey(key);
                using var command = client?.OpenSubKey(@"shell\open\command");
                if (command?.GetValue(null) is string line)
                    yield return new RegisteredBrowser(key, client?.GetValue(null) as string ?? "", line);
            }
        }
    }
}
