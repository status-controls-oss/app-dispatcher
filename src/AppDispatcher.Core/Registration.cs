using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AppDispatcher.Core;

/// <summary>
/// Registers the picker as a candidate handler for http/https and chosen file types (per-user, no admin).
/// This only makes it selectable: Windows reserves the choice of default for the user, in Default Apps.
/// </summary>
public static partial class Registration
{
    const string AppName = "AppDispatcher";
    const string AppKey = @"Software\Clients\StartMenuInternet\" + AppName;
    const string CapabilitiesKey = AppKey + @"\Capabilities";
    const string UrlProgId = "AppDispatcher.URL";
    const string FileProgIdPrefix = "AppDispatcher.File";
    const string Classes = @"Software\Classes\";

    /// <summary>Opens Windows' Default Apps page directly on this app's entry.</summary>
    public const string DefaultAppsUri = "ms-settings:defaultapps?registeredAppUser=" + AppName;

    /// <summary>Opens Windows' Default Apps page itself, for after this app is unregistered.</summary>
    public const string AllDefaultAppsUri = "ms-settings:defaultapps";

    /// <summary>Writes or updates everything, removing file types that are no longer configured.</summary>
    public static void Sync(string pickerExe, IReadOnlyCollection<ExtensionRule> extensions)
    {
        var cmd = $"\"{pickerExe}\" \"%1\"";
        // An extension becomes part of registry key paths, so one edited by hand to contain a
        // backslash would otherwise create keys elsewhere under Software\Classes.
        extensions = extensions
            .Where(e => string.Equals(AppConfig.NormalizeExtension(e.Extension), e.Extension, StringComparison.OrdinalIgnoreCase))
            .ToList();

        using (var k = Registry.CurrentUser.CreateSubKey(Classes + UrlProgId))
        {
            k.SetValue(null, "App Dispatcher URL");
            k.SetValue("URL Protocol", "");
            using var c = k.CreateSubKey(@"shell\open\command");
            c.SetValue(null, cmd);
        }
        using (var k = Registry.CurrentUser.CreateSubKey(AppKey))
        {
            k.SetValue(null, "App Dispatcher");
            using (var c = k.CreateSubKey(@"shell\open\command")) c.SetValue(null, $"\"{pickerExe}\"");
            using var cap = k.CreateSubKey("Capabilities");
            cap.SetValue("ApplicationName", "App Dispatcher");
            cap.SetValue("ApplicationDescription", "Choose which app, window and profile opens each file type and link.");
            using var urls = cap.CreateSubKey("URLAssociations");
            urls.SetValue("http", UrlProgId);
            urls.SetValue("https", UrlProgId);
        }

        var wanted = extensions.Select(e => e.Extension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in RegisteredExtensions().Where(e => !wanted.Contains(e)).ToList())
            RemoveExtension(stale);
        foreach (var e in extensions)
            WriteExtension(e, cmd);

        using (var reg = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            reg.SetValue(AppName, CapabilitiesKey);
        NotifyShell();
    }

    public static void Unregister()
    {
        foreach (var e in RegisteredExtensions().ToList()) RemoveExtension(e);
        Registry.CurrentUser.DeleteSubKeyTree(Classes + UrlProgId, false);
        Registry.CurrentUser.DeleteSubKeyTree(AppKey, false);
        using (var reg = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true))
            reg?.DeleteValue(AppName, false);
        NotifyShell();
    }

    public static bool IsRegistered(string pickerExe)
    {
        using var k = Registry.CurrentUser.OpenSubKey($@"{Classes}{UrlProgId}\shell\open\command");
        return k?.GetValue(null) is string s && s.Contains(pickerExe, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether Windows currently sends this extension to us, as chosen by the user.</summary>
    public static bool IsDefaultFor(string extension) =>
        string.Equals(CurrentProgId(extension), ProgId(extension), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether Windows currently sends both kinds of web link to us, as chosen by the user.</summary>
    /// <remarks>Asks Windows rather than reading UserChoice: newer builds record the choice under UserChoiceLatest and leave UserChoice stale.</remarks>
    public static bool IsDefaultForLinks() => new[] { "http", "https" }.All(scheme =>
        string.Equals(CurrentProgId(scheme, AssocfIsProtocol), UrlProgId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Records the app Windows uses now for each extension and for http/https, skipping any that
    /// already use App Dispatcher. Returns whether the snapshot changed, so the caller can save.
    /// </summary>
    public static bool SnapshotDefaults(List<PreviousDefault> snapshot, IEnumerable<string> extensions)
    {
        var changed = false;
        foreach (var key in ValidKeys(extensions).Concat(PreviousDefaults.LinkSchemes))
            if (CurrentDefault(key) is { } current)
                changed |= PreviousDefaults.Record(snapshot, current);
        return changed;
    }

    /// <summary>The file types and link schemes Windows sends to App Dispatcher now, as chosen by the user.</summary>
    /// <param name="extensions">Checked as well as the ones registered, such as types removed from the config.</param>
    public static List<string> KeysSentToUs(IEnumerable<string> extensions) =>
        ValidKeys(extensions.Concat(RegisteredExtensions()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Concat(PreviousDefaults.LinkSchemes)
            .Where(key => PreviousDefaults.IsOurs(CurrentProgId(key, FlagsFor(key))))
            .ToList();

    /// <summary>What to choose again in Windows. Call it before <see cref="Unregister"/>, which removes what it checks for.</summary>
    public static List<string> RestoreLines(AppConfig config) => PreviousDefaults.RestoreLines(
        KeysSentToUs(config.Extensions.Select(x => x.Extension).Concat(config.PreviousDefaults.Select(p => p.Key))),
        config.PreviousDefaults);

    static IEnumerable<string> ValidKeys(IEnumerable<string> extensions) =>
        extensions.Where(e => AppConfig.NormalizeExtension(e) is { } n && string.Equals(n, e, StringComparison.OrdinalIgnoreCase));

    static int FlagsFor(string key) => key.StartsWith('.') ? 0 : AssocfIsProtocol;

    static PreviousDefault? CurrentDefault(string key)
    {
        var flags = FlagsFor(key);
        if (CurrentProgId(key, flags) is not { Length: > 0 } progId) return null;
        return new PreviousDefault
        {
            Key = key,
            ProgId = progId,
            Name = Query(key, AssocStrFriendlyAppName, flags),
            // Fails without a verb, even for ordinary desktop apps.
            Exe = Query(key, AssocStrExecutable, flags, "open"),
        };
    }

    static string ProgId(string extension) => $"{FileProgIdPrefix}{extension}";

    static IEnumerable<string> RegisteredExtensions()
    {
        using var k = Registry.CurrentUser.OpenSubKey($@"{CapabilitiesKey}\FileAssociations");
        return k?.GetValueNames() ?? Array.Empty<string>();
    }

    static void WriteExtension(ExtensionRule rule, string cmd)
    {
        var ext = rule.Extension;
        var progId = ProgId(ext);
        using (var k = Registry.CurrentUser.CreateSubKey(Classes + progId))
        {
            k.SetValue(null, $"{ext.TrimStart('.').ToUpperInvariant()} file");
            using (var c = k.CreateSubKey(@"shell\open\command")) c.SetValue(null, cmd);
            // Otherwise every associated file in Explorer would wear a generic icon.
            var icon = rule.Targets.FirstOrDefault()?.Exe;
            using var i = k.CreateSubKey("DefaultIcon");
            if (icon != null) i.SetValue(null, $"\"{icon}\",0");
        }
        using (var assoc = Registry.CurrentUser.CreateSubKey($@"{CapabilitiesKey}\FileAssociations"))
            assoc.SetValue(ext, progId);
        using (var open = Registry.CurrentUser.CreateSubKey($@"{Classes}{ext}\OpenWithProgids"))
            open.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
    }

    static void RemoveExtension(string ext)
    {
        var progId = ProgId(ext);
        Registry.CurrentUser.DeleteSubKeyTree(Classes + progId, false);
        using (var assoc = Registry.CurrentUser.OpenSubKey($@"{CapabilitiesKey}\FileAssociations", true))
            assoc?.DeleteValue(ext, false);
        using (var open = Registry.CurrentUser.OpenSubKey($@"{Classes}{ext}\OpenWithProgids", true))
            open?.DeleteValue(progId, false);
    }

    const int AssocStrExecutable = 2;
    const int AssocStrFriendlyAppName = 4;
    const int AssocStrProgId = 20;
    const int AssocfIsProtocol = 0x1000;
    const int ShcneAssocChanged = 0x08000000;

    [LibraryImport("shlwapi", StringMarshalling = StringMarshalling.Utf16, EntryPoint = "AssocQueryStringW")]
    private static unsafe partial int AssocQueryString(int flags, int str, string assoc, string? extra, char* buffer, ref uint length);

    [LibraryImport("shell32")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);

    static string? CurrentProgId(string extension, int flags = 0) => Query(extension, AssocStrProgId, flags);

    static unsafe string? Query(string assoc, int str, int flags, string? verb = null)
    {
        const int capacity = 1024;
        var buffer = stackalloc char[capacity];
        uint length = capacity;
        return AssocQueryString(flags, str, assoc, verb, buffer, ref length) == 0
            ? new string(buffer)
            : null;
    }

    static void NotifyShell() => SHChangeNotify(ShcneAssocChanged, 0, 0, 0);
}
