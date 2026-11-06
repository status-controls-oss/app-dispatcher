using System.Text.Json;
using Microsoft.Win32;

namespace AppDispatcher.Core;

public class BrowserTarget
{
    public string Browser { get; init; } = "";      // Chrome | Edge | Firefox
    public string ExePath { get; init; } = "";
    public string ProfileId { get; init; } = "";    // chromium: directory name; firefox: profile name
    public string ProfileName { get; init; } = "";
    public string Display => $"{Browser} — {ProfileName}";

    public bool Matches(string browser, string profileId) =>
        Browser == browser && string.Equals(ProfileId, profileId, StringComparison.OrdinalIgnoreCase);

    public void Launch(Uri url)
    {
        var args = Browser == "Firefox"
            ? new[] { "-P", ProfileId, "-url", url.AbsoluteUri }
            : new[] { $"--profile-directory={ProfileId}", url.AbsoluteUri };
        Launcher.Start(ExePath, args);
    }
}

public static class BrowserDiscovery
{
    public static List<BrowserTarget> Discover()
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
        var localState = Path.Combine(userDataDir, "Local State");
        if (exe == null || !File.Exists(localState)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(localState));
            if (!doc.RootElement.TryGetProperty("profile", out var prof) ||
                !prof.TryGetProperty("info_cache", out var cache)) return;
            foreach (var p in cache.EnumerateObject())
            {
                var name = p.Value.TryGetProperty("name", out var n) ? n.GetString() : null;
                var user = p.Value.TryGetProperty("user_name", out var u) ? u.GetString() : null;
                var display = string.IsNullOrWhiteSpace(name) ? p.Name : name;
                if (!string.IsNullOrWhiteSpace(user)) display += $" ({user})";
                list.Add(new BrowserTarget { Browser = browser, ExePath = exe, ProfileId = p.Name, ProfileName = display });
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Unreadable Local State: skip this browser.
        }
    }

    static void AddFirefox(List<BrowserTarget> list, string? exe, string firefoxDir)
    {
        var ini = Path.Combine(firefoxDir, "profiles.ini");
        if (exe == null || !File.Exists(ini)) return;
        string? name = null;
        bool inProfile = false;
        void Flush()
        {
            if (inProfile && !string.IsNullOrEmpty(name))
                list.Add(new BrowserTarget { Browser = "Firefox", ExePath = exe, ProfileId = name, ProfileName = name });
            name = null;
        }
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
}
