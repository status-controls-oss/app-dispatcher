using System.Text.Json;
using Microsoft.Win32;

namespace AppDispatcher.Core;

public class BrowserTarget
{
    public const string UrlPlaceholder = "{url}";

    public string Browser { get; init; } = "";      // Chrome | Edge | Firefox | a custom browser's label
    public string ExePath { get; init; } = "";
    public string ProfileId { get; init; } = "";    // chromium: directory name; firefox: profile name; custom: empty
    public string ProfileName { get; init; } = "";

    /// <summary>Set for custom browsers, which are invoked exactly as configured.</summary>
    public IReadOnlyList<string>? CustomArgs { get; init; }

    public string Display => ProfileName.Length == 0 ? Browser : $"{Browser} — {ProfileName}";

    /// <summary>Identifies this row in <see cref="AppConfig.BrowserOrder"/>.</summary>
    public string OrderKey => $"{Browser}|{ProfileId}";

    public bool Matches(string browser, string profileId) =>
        Browser == browser && string.Equals(ProfileId, profileId, StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> LaunchArgs(Uri url)
    {
        if (CustomArgs != null)
            return CustomArgs.Select(a => a.Replace(UrlPlaceholder, url.AbsoluteUri)).ToList();
        return Browser == "Firefox"
            ? new[] { "-P", ProfileId, "-url", url.AbsoluteUri }
            : new[] { $"--profile-directory={ProfileId}", url.AbsoluteUri };
    }

    public void Launch(Uri url) => Launcher.Start(ExePath, LaunchArgs(url));
}

public static class BrowserDiscovery
{
    /// <summary>
    /// Installed browsers' profiles, followed by the custom browsers that are still usable,
    /// rearranged into the order chosen in Settings.
    /// </summary>
    public static List<BrowserTarget> Discover(IEnumerable<CustomBrowser> custom, string? selfExe, IReadOnlyList<string>? order = null)
    {
        var list = DiscoverInstalled();
        list.AddRange(FromCustom(custom, selfExe));
        return order is { Count: > 0 } ? InOrder(list, order) : list;
    }

    /// <summary>
    /// Puts browsers in the chosen order. Ones that aren't in it yet, such as a newly found profile,
    /// keep their usual order after the rest.
    /// </summary>
    public static List<BrowserTarget> InOrder(IEnumerable<BrowserTarget> browsers, IReadOnlyList<string> order)
    {
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < order.Count; i++) rank.TryAdd(order[i], i);
        return browsers
            .Select((b, i) => (Browser: b, Index: i))
            .OrderBy(x => rank.TryGetValue(x.Browser.OrderKey, out var r) ? r : int.MaxValue)
            .ThenBy(x => x.Index)
            .Select(x => x.Browser)
            .ToList();
    }

    /// <summary>Drops an app that is no longer installed, a script, or the dispatcher itself, which would reopen the picker forever.</summary>
    public static IEnumerable<BrowserTarget> FromCustom(IEnumerable<CustomBrowser> custom, string? selfExe) =>
        custom
            .Where(c => File.Exists(c.Exe) && Launcher.CanLaunch(c.Exe) && !string.Equals(c.Exe, selfExe, StringComparison.OrdinalIgnoreCase))
            .Select(c => new BrowserTarget { Browser = c.Label, ExePath = c.Exe, CustomArgs = c.Args });

    static List<BrowserTarget> DiscoverInstalled()
    {
        var list = new List<BrowserTarget>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        AddChromium(list, "Chrome", FindExe("chrome.exe", @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(local, @"Google\Chrome\User Data"));
        AddChromium(list, "Edge", FindExe("msedge.exe", @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(local, @"Microsoft\Edge\User Data"));
        AddFirefox(list, FindExe("firefox.exe", @"Mozilla Firefox\firefox.exe"),
            Path.Combine(roaming, @"Mozilla\Firefox"));
        return list;
    }

    static string? FindExe(string exeName, string relativeUnderProgramFiles)
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var k = root.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
            if (k?.GetValue(null) is string p && File.Exists(p)) return p;
        }
        foreach (var pf in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Programs",
        })
        {
            var p = Path.Combine(pf, relativeUnderProgramFiles);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    static void AddChromium(List<BrowserTarget> list, string browser, string? exe, string userDataDir)
    {
        if (exe == null) return;
        foreach (var (id, name) in ChromiumProfiles(userDataDir))
            list.Add(new BrowserTarget { Browser = browser, ExePath = exe, ProfileId = id, ProfileName = name });
    }

    static void AddFirefox(List<BrowserTarget> list, string? exe, string firefoxDir)
    {
        if (exe == null) return;
        foreach (var (id, name) in FirefoxProfiles(firefoxDir))
            list.Add(new BrowserTarget { Browser = "Firefox", ExePath = exe, ProfileId = id, ProfileName = name });
    }

    /// <summary>A Chromium browser's profiles, or none if its Local State is missing or unreadable.</summary>
    internal static List<(string Id, string Name)> ChromiumProfiles(string userDataDir)
    {
        var profiles = new List<(string Id, string Name)>();
        var localState = Path.Combine(userDataDir, "Local State");
        if (!File.Exists(localState)) return profiles;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(localState));
            if (!doc.RootElement.TryGetProperty("profile", out var prof) ||
                !prof.TryGetProperty("info_cache", out var cache)) return profiles;
            foreach (var p in cache.EnumerateObject())
            {
                var name = p.Value.TryGetProperty("name", out var n) ? n.GetString() : null;
                var user = p.Value.TryGetProperty("user_name", out var u) ? u.GetString() : null;
                var display = string.IsNullOrWhiteSpace(name) ? p.Name : name;
                if (!string.IsNullOrWhiteSpace(user)) display += $" ({user})";
                profiles.Add((p.Name, display));
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Unreadable Local State: treat as having no profiles.
        }
        return profiles;
    }

    /// <summary>A Firefox-family browser's profiles, or none if its profiles.ini is missing or unreadable.</summary>
    internal static List<(string Id, string Name)> FirefoxProfiles(string firefoxDir)
    {
        var profiles = new List<(string Id, string Name)>();
        var ini = Path.Combine(firefoxDir, "profiles.ini");
        if (!File.Exists(ini)) return profiles;
        string? name = null;
        bool inProfile = false;
        void Flush()
        {
            if (inProfile && !string.IsNullOrEmpty(name)) profiles.Add((name, name));
            name = null;
        }
        try
        {
            foreach (var raw in File.ReadLines(ini))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    Flush();
                    inProfile = line.StartsWith("[Profile", StringComparison.OrdinalIgnoreCase);
                }
                else if (inProfile && line.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                    name = line[5..];
            }
            Flush();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable profiles.ini: keep whatever was read before the failure.
        }
        return profiles;
    }
}
