using System.Text;

namespace AppDispatcher.Core;

public sealed record ResolvedTarget(string Label, string Exe, IReadOnlyList<string> Args);

/// <summary>Turns configured targets into concrete command lines for one file.</summary>
public static class TargetResolver
{
    const string File_ = "{file}", Dir = "{dir}", Solution = "{solution}", SolutionName = "{solutionName}";

    /// <summary>
    /// Drops targets that can't apply: a {solution} target when the file has no solution,
    /// an app that is no longer installed, or anything pointing back at the dispatcher itself.
    /// </summary>
    public static List<ResolvedTarget> Resolve(
        IEnumerable<FileTarget> targets, string file, string? selfExe = null, Func<string, string?>? findExe = null)
    {
        var result = new List<ResolvedTarget>();
        var dir = Path.GetDirectoryName(file) ?? "";
        string? solution = null;
        var solutionSearched = false;

        foreach (var t in targets)
        {
            var exe = File.Exists(t.Exe) ? t.Exe : (t.AppId == null ? null : findExe?.Invoke(t.AppId));
            if (exe == null || string.Equals(exe, selfExe, StringComparison.OrdinalIgnoreCase)) continue;

            var usesSolution = t.Label.Contains(SolutionName) || t.Args.Any(a => a.Contains(Solution));
            if (usesSolution && !solutionSearched)
            {
                solution = SolutionFinder.Find(file);
                solutionSearched = true;
            }
            if (usesSolution && solution == null) continue;

            var args = t.Args.Select(a => a
                .Replace(File_, file)
                .Replace(Dir, dir)
                .Replace(Solution, solution ?? "")).ToList();
            var label = t.Label.Replace(SolutionName, solution == null ? "" : Path.GetFileName(solution));
            result.Add(new ResolvedTarget(label, exe, args));
        }
        return result;
    }

    /// <summary>Splits a typed argument string on spaces outside double quotes; adds the placeholder if it is absent.</summary>
    public static List<string> ParseArgs(string text, string placeholder = File_)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var hasToken = false;
        foreach (var c in text)
        {
            if (c == '"') { quoted = !quoted; hasToken = true; }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (hasToken) args.Add(current.ToString());
                current.Clear();
                hasToken = false;
            }
            else { current.Append(c); hasToken = true; }
        }
        if (hasToken) args.Add(current.ToString());
        if (!args.Any(a => a.Contains(placeholder))) args.Add(placeholder);
        return args;
    }
}
