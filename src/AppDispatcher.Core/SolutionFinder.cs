namespace AppDispatcher.Core;

/// <summary>Finds the .NET solution a file belongs to by walking up its parent folders.</summary>
public static class SolutionFinder
{
    const int MaxLevels = 12;

    /// <summary>
    /// Nearest .sln or .slnx, stopping at the git root. Network paths are skipped because this runs
    /// before the picker is drawn and a slow share would stall it.
    /// </summary>
    public static string? Find(string file)
    {
        try
        {
            if (IsNetworkPath(file)) return null;
            var dir = Path.GetDirectoryName(file);
            for (var level = 0; dir != null && level < MaxLevels; level++)
            {
                var found = Directory.EnumerateFiles(dir, "*.sln")
                    .Concat(Directory.EnumerateFiles(dir, "*.slnx"))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (found != null) return found;
                if (Directory.Exists(Path.Combine(dir, ".git"))) return null;
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // An unreadable folder only means no solution row is offered.
        }
        return null;
    }

    static bool IsNetworkPath(string path)
    {
        if (path.StartsWith(@"\\")) return true;
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network;
    }
}
