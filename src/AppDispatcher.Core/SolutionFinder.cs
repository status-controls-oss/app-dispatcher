namespace AppDispatcher.Core;

/// <summary>Finds the .NET solution a file belongs to by walking up its parent folders.</summary>
public static class SolutionFinder
{
    const int MaxLevels = 12;

    /// <summary>
    /// Nearest .sln or .slnx, stopping at the git root. Network paths are skipped because this runs
    /// before the picker is drawn and a slow share would stall it.
    /// </summary>
    /// <param name="sharedFolders">
    /// Folders where anything can land, such as Downloads. A solution directly in one of these belongs
    /// to no particular file, and could have been planted to be offered for everything beneath it.
    /// </param>
    public static string? Find(string file, IEnumerable<string>? sharedFolders = null)
    {
        try
        {
            if (IsNetworkPath(file)) return null;
            var shared = new HashSet<string>(
                (sharedFolders ?? DefaultSharedFolders()).Select(f => f.TrimEnd('\\', '/')),
                StringComparer.OrdinalIgnoreCase);
            var dir = Path.GetDirectoryName(file);
            for (var level = 0; dir != null && level < MaxLevels; level++)
            {
                if (shared.Contains(dir.TrimEnd('\\', '/')) || Path.GetPathRoot(dir) == dir) return null;
                var found = Directory.EnumerateFiles(dir, "*.sln")
                    .Concat(Directory.EnumerateFiles(dir, "*.slnx"))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (found != null) return found;
                // In a worktree or submodule, .git is a file rather than a folder.
                var git = Path.Combine(dir, ".git");
                if (Directory.Exists(git) || File.Exists(git)) return null;
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // An unreadable folder only means no solution row is offered.
        }
        return null;
    }

    static IEnumerable<string> DefaultSharedFolders()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
        {
            profile,
            Path.Combine(profile, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.GetTempPath(),
        }.Where(f => f.Length > 0);
    }

    static bool IsNetworkPath(string path)
    {
        if (path.StartsWith(@"\\")) return true;
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network;
    }
}
