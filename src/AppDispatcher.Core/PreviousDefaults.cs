namespace AppDispatcher.Core;

/// <summary>
/// The app Windows used for a file type or link scheme before App Dispatcher, so that unregistering
/// can tell the user what to choose again. Windows doesn't let apps set the default themselves.
/// </summary>
public class PreviousDefault
{
    /// <summary>An extension such as ".txt", or "http" or "https".</summary>
    public string Key { get; set; } = "";
    public string ProgId { get; set; } = "";
    public string? Name { get; set; }
    public string? Exe { get; set; }
}

public static class PreviousDefaults
{
    public static readonly string[] LinkSchemes = { "http", "https" };

    /// <summary>Whether a ProgId is one of App Dispatcher's, from this copy or any other.</summary>
    public static bool IsOurs(string? progId) =>
        progId?.StartsWith("AppDispatcher.", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Remembers what Windows uses now, unless that's App Dispatcher. The latest other app wins,
    /// as it's the one the user chose most recently. Returns whether anything changed.
    /// </summary>
    public static bool Record(List<PreviousDefault> snapshot, PreviousDefault current)
    {
        if (current.ProgId.Length == 0 || IsOurs(current.ProgId)) return false;
        var existing = snapshot.FindIndex(p => string.Equals(p.Key, current.Key, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            var old = snapshot[existing];
            if (old.ProgId == current.ProgId && old.Name == current.Name && old.Exe == current.Exe) return false;
            snapshot[existing] = current;
        }
        else snapshot.Add(current);
        return true;
    }

    /// <summary>One line per file type or link scheme that Windows was sending to App Dispatcher.</summary>
    public static List<string> RestoreLines(IEnumerable<string> keysSentToUs, IReadOnlyList<PreviousDefault> snapshot)
    {
        var keys = keysSentToUs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string? NameOf(string key) => snapshot
            .Where(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
            .Select(Display)
            .FirstOrDefault();
        static string Line(string what, string? name) =>
            name == null ? $"{what}: no earlier app was recorded" : $"{what} used {name}";

        var lines = new List<string>();
        var links = keys.Where(k => LinkSchemes.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
        if (links.Count == 2 && NameOf("http") == NameOf("https"))
            lines.Add(Line("Links", NameOf("https")));
        else
            lines.AddRange(links.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .Select(k => Line($"{k.ToUpperInvariant()} links", NameOf(k))));

        lines.AddRange(keys.Except(links, StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(k => Line($"{k} files", NameOf(k))));
        return lines;
    }

    static string Display(PreviousDefault p) =>
        !string.IsNullOrWhiteSpace(p.Name) ? p.Name :
        !string.IsNullOrWhiteSpace(p.Exe) ? Path.GetFileNameWithoutExtension(p.Exe) :
        p.ProgId;
}
