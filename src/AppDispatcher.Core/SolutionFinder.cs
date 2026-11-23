using System.Runtime.InteropServices;

namespace AppDispatcher.Core;

/// <summary>Finds the .NET solution a file belongs to by walking up its parent folders.</summary>
public static partial class SolutionFinder
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
    /// <param name="untrustedRoots">
    /// Folders where nothing below is a project, such as temp, where archives and email attachments are
    /// extracted. A solution anywhere in these could have come with the file.
    /// </param>
    public static string? Find(string file, IEnumerable<string>? sharedFolders = null, IEnumerable<string>? untrustedRoots = null)
    {
        try
        {
            if (IsNetworkPath(file)) return null;
            if ((untrustedRoots ?? DefaultUntrustedRoots()).Any(root => IsBelow(file, root))) return null;
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
            KnownFolder(DownloadsFolderId) ?? "", // Downloads can be moved, for example into OneDrive.
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.GetTempPath(),
        }.Where(f => f.Length > 0);
    }

    static IEnumerable<string> DefaultUntrustedRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new[]
        {
            Path.GetTempPath(),
            local.Length == 0 ? "" : Path.Combine(local, @"Microsoft\Windows\INetCache"),
        }.Where(f => f.Length > 0);
    }

    static bool IsBelow(string path, string folder)
    {
        folder = folder.TrimEnd('\\', '/') + "\\";
        return path.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
    }

    static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    static string? KnownFolder(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, 0, out var ptr) != 0) return null;
        try { return Marshal.PtrToStringUni(ptr); }
        finally { Marshal.FreeCoTaskMem(ptr); }
    }

    [LibraryImport("shell32")]
    private static partial int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);

    static bool IsNetworkPath(string path)
    {
        if (path.StartsWith(@"\\")) return true;
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network;
    }
}
