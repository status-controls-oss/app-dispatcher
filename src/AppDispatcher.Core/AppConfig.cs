using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppDispatcher.Core;

/// <summary>What to do with links to a domain or path: open a browser profile, or show the picker.</summary>
public class Rule
{
    /// <summary>Stored as the browser of a rule that shows the picker instead of opening a browser.</summary>
    public const string ShowPicker = "Show the picker";

    /// <summary>A domain, including its subdomains, optionally with a path: "github.com/myorg".</summary>
    public string Domain { get; set; } = "";
    public string Browser { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string ProfileName { get; set; } = "";
}

/// <summary>One row in the picker: an app plus how to invoke it.</summary>
public class FileTarget
{
    public string Label { get; set; } = "";
    public string Exe { get; set; } = "";

    /// <summary>Arguments; may contain {file}, {dir} and {solution}. See <see cref="TargetResolver"/>.</summary>
    public List<string> Args { get; set; } = new();

    /// <summary>Catalog id, used to find the exe again when an update moves it. Null for custom apps.</summary>
    public string? AppId { get; set; }
}

public class ExtensionRule
{
    public string Extension { get; set; } = "";
    public List<FileTarget> Targets { get; set; } = new();
    public string? LastTargetLabel { get; set; }
}

/// <summary>A browser added by hand in Settings, for anything discovery doesn't know about.</summary>
public class CustomBrowser
{
    public string Label { get; set; } = "";
    public string Exe { get; set; } = "";

    /// <summary>Arguments; may contain {url}. See <see cref="BrowserTarget.UrlPlaceholder"/>.</summary>
    public List<string> Args { get; set; } = new();
}

public class AppConfig
{
    public List<Rule> Rules { get; set; } = new();
    public string? LastBrowser { get; set; }
    public string? LastProfileId { get; set; }

    /// <summary>Opens links that have no rule. When null, those links show the picker.</summary>
    public string? DefaultBrowser { get; set; }
    public string? DefaultProfileId { get; set; }

    public List<ExtensionRule> Extensions { get; set; } = new();
    public List<CustomBrowser> CustomBrowsers { get; set; } = new();

    /// <summary>What opened each file type and link scheme before App Dispatcher, to guide the user in restoring them.</summary>
    public List<PreviousDefault> PreviousDefaults { get; set; } = new();

    static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>Overridable so tests never touch the real config.</summary>
    public static string PathOnDisk { get; set; } = Path.Combine(AppData, "AppDispatcher", "config.json");

    static readonly string LegacyPath = Path.Combine(AppData, "WebDispatch", "config.json");

    public static AppConfig Load()
    {
        var path = File.Exists(PathOnDisk) ? PathOnDisk : File.Exists(LegacyPath) ? LegacyPath : null;
        if (path == null) return new AppConfig();
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), ConfigJson.Default.AppConfig) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Keep the unreadable file: the next Save would otherwise destroy it.
            try { File.Copy(path, path + ".bad", overwrite: true); } catch (IOException) { }
            return new AppConfig();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk)!);
        var temp = PathOnDisk + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, ConfigJson.Default.AppConfig));
        File.Move(temp, PathOnDisk, overwrite: true);
    }

    /// <summary>
    /// Re-reads, changes and saves. The picker uses this because it can stay open for minutes,
    /// and saving its startup copy would discard anything changed in Settings meanwhile.
    /// </summary>
    public static void Update(Action<AppConfig> change)
    {
        var fresh = Load();
        change(fresh);
        fresh.Save();
    }

    /// <summary>Reduces a typed domain or pasted link to "host" or "host/path".</summary>
    public static string NormalizeDomain(string d)
    {
        d = d.Trim().ToLowerInvariant();
        var scheme = d.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) d = d[(scheme + 3)..];
        var query = d.IndexOfAny(new[] { '?', '#' });
        if (query >= 0) d = d[..query];
        if (d.StartsWith("*.")) d = d[2..];

        var slash = d.IndexOf('/');
        var host = slash < 0 ? d : d[..slash];
        var path = slash < 0 ? "" : d[slash..].TrimEnd('/');
        var port = host.IndexOf(':');
        if (port >= 0) host = host[..port];
        return HostKey(host) + path;
    }

    /// <summary>
    /// Compares hosts in one form: "bank.com." is the same site as "bank.com", and an international
    /// name is the same whether written in Unicode or as punycode, so neither form can skip a rule.
    /// </summary>
    static string HostKey(string host)
    {
        host = host.Trim('.').ToLowerInvariant();
        try { return host.Length == 0 ? host : Idn.GetAscii(host); }
        catch (ArgumentException) { return host; }
    }

    static readonly IdnMapping Idn = new();

    /// <summary>
    /// A rule for "example.com" matches example.com and any subdomain, and a rule for
    /// "example.com/team" also needs that path or one below it. Most specific rule wins.
    /// </summary>
    public Rule? FindRule(string host, string path = "/")
    {
        host = HostKey(host);
        return Rules
            .Where(r => RuleMatches(r.Domain, host, path))
            .OrderByDescending(r => r.Domain.Length)
            .FirstOrDefault();
    }

    static bool RuleMatches(string rule, string host, string path)
    {
        var slash = rule.IndexOf('/');
        var ruleHost = HostKey(slash < 0 ? rule : rule[..slash]);
        if (host != ruleHost && !host.EndsWith("." + ruleHost)) return false;
        if (slash < 0) return true;
        var rulePath = rule[slash..];
        return path.Equals(rulePath, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(rulePath + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The browser that opens this link straight away, or null if it should show the picker.</summary>
    public (string Browser, string ProfileId)? BrowserWithoutAsking(Uri url)
    {
        if (FindRule(url.Host, url.AbsolutePath) is { } rule)
            return rule.Browser == Rule.ShowPicker ? null : (rule.Browser, rule.ProfileId);
        return DefaultBrowser == null ? null : (DefaultBrowser, DefaultProfileId ?? "");
    }

    /// <summary>Adds or replaces a rule in memory; the caller saves.</summary>
    public void SetRule(string domain, string browser, string profileId, string profileName)
    {
        domain = NormalizeDomain(domain);
        if (domain.Length == 0) return;
        Rules.RemoveAll(r => r.Domain == domain);
        Rules.Add(new Rule { Domain = domain, Browser = browser, ProfileId = profileId, ProfileName = profileName });
        Rules.Sort((a, b) => string.Compare(a.Domain, b.Domain, StringComparison.Ordinal));
    }

    /// <summary>Returns ".json" for "json", "*.JSON" or ".json"; null if it isn't a usable extension.</summary>
    public static string? NormalizeExtension(string raw)
    {
        var e = raw.Trim().ToLowerInvariant();
        if (e.StartsWith('*')) e = e[1..];
        if (!e.StartsWith('.')) e = "." + e;
        return e.Length > 1 && e.Skip(1).All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '+') ? e : null;
    }

    public ExtensionRule? FindExtension(string extension) =>
        Extensions.FirstOrDefault(x => string.Equals(x.Extension, extension, StringComparison.OrdinalIgnoreCase));
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppConfig))]
internal partial class ConfigJson : JsonSerializerContext;
