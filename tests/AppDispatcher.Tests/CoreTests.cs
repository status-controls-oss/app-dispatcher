using AppDispatcher.Core;

namespace AppDispatcher.Tests;

public sealed class TempTree : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("appdispatcher-test").FullName;

    public string File(string relative, string content = "")
    {
        var path = Path.GetFullPath(Path.Combine(Root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    public string Dir(string relative) => Directory.CreateDirectory(Path.Combine(Root, relative)).FullName;

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

public class SolutionFinderTests
{
    [Fact]
    public void Finds_nearest_solution_in_a_parent_folder()
    {
        using var tree = new TempTree();
        tree.File("repo/Outer.sln");
        var inner = tree.File("repo/src/Inner.slnx");
        var file = tree.File("repo/src/project/settings.json");

        Assert.Equal(inner, SolutionFinder.Find(file));
    }

    [Fact]
    public void Stops_at_the_git_root()
    {
        using var tree = new TempTree();
        tree.File("Above.sln");
        tree.Dir("repo/.git");
        var file = tree.File("repo/data/settings.json");

        Assert.Null(SolutionFinder.Find(file));
    }

    [Fact]
    public void Checks_the_git_root_folder_itself()
    {
        using var tree = new TempTree();
        tree.Dir("repo/.git");
        var sln = tree.File("repo/App.sln");
        var file = tree.File("repo/settings.json");

        Assert.Equal(sln, SolutionFinder.Find(file));
    }

    [Fact]
    public void Picks_alphabetically_first_when_a_folder_has_several()
    {
        using var tree = new TempTree();
        tree.File("repo/Zeta.sln");
        var alpha = tree.File("repo/Alpha.sln");
        var file = tree.File("repo/a.json");

        Assert.Equal(alpha, SolutionFinder.Find(file));
    }
}

public class TargetResolverTests
{
    static FileTarget Target(string label, string exe, params string[] args) =>
        new() { Label = label, Exe = exe, Args = args.ToList() };

    [Fact]
    public void Expands_file_and_folder_without_splitting_paths_with_spaces()
    {
        using var tree = new TempTree();
        var exe = tree.File("app.exe");
        var file = tree.File("my folder/a b.json");

        var result = TargetResolver.Resolve(new[] { Target("App", exe, "-n", "{dir}", "{file}") }, file);

        Assert.Equal(new[] { "-n", Path.GetDirectoryName(file)!, file }, result.Single().Args);
    }

    [Fact]
    public void Solution_target_names_the_solution_when_one_exists()
    {
        using var tree = new TempTree();
        var exe = tree.File("app.exe");
        var sln = tree.File("repo/Foo.sln");
        var file = tree.File("repo/a.json");

        var result = TargetResolver.Resolve(new[] { Target("Rider — {solutionName}", exe, "{solution}", "{file}") }, file).Single();

        Assert.Equal("Rider — Foo.sln", result.Label);
        Assert.Equal(new[] { sln, file }, result.Args);
    }

    [Fact]
    public void Solution_target_is_hidden_when_there_is_no_solution()
    {
        using var tree = new TempTree();
        tree.Dir("repo/.git");
        var exe = tree.File("app.exe");
        var file = tree.File("repo/a.json");

        var result = TargetResolver.Resolve(new[]
        {
            Target("Rider — {solutionName}", exe, "{solution}", "{file}"),
            Target("Rider — current window", exe, "{file}"),
        }, file);

        Assert.Equal("Rider — current window", result.Single().Label);
    }

    [Fact]
    public void Skips_the_dispatcher_itself_so_a_file_cannot_reopen_the_picker_forever()
    {
        using var tree = new TempTree();
        var self = tree.File("AppDispatcher.exe");
        var file = tree.File("a.json");

        Assert.Empty(TargetResolver.Resolve(new[] { Target("Loop", self.ToUpperInvariant(), "{file}") }, file, self));
    }

    [Fact]
    public void Missing_custom_app_is_dropped_but_a_moved_catalog_app_is_found_again()
    {
        using var tree = new TempTree();
        var moved = tree.File("new-version/rider64.exe");
        var file = tree.File("a.json");
        var gone = Path.Combine(tree.Root, "old-version", "rider64.exe");

        var result = TargetResolver.Resolve(new[]
        {
            Target("Custom", gone, "{file}"),
            new FileTarget { Label = "Rider", Exe = gone, Args = new() { "{file}" }, AppId = "rider" },
        }, file, findExe: id => id == "rider" ? moved : null);

        Assert.Equal(moved, result.Single().Exe);
    }

    [Theory]
    [InlineData("{file}", new[] { "{file}" })]
    [InlineData("-n {file}", new[] { "-n", "{file}" })]
    [InlineData("--flag", new[] { "--flag", "{file}" })]
    [InlineData("", new[] { "{file}" })]
    [InlineData("--title \"my app\" {file}", new[] { "--title", "my app", "{file}" })]
    [InlineData("--empty \"\" {file}", new[] { "--empty", "", "{file}" })]
    public void ParseArgs_splits_on_spaces_outside_quotes_and_ensures_file(string text, string[] expected) =>
        Assert.Equal(expected, TargetResolver.ParseArgs(text));
}

public class AppConfigTests
{
    [Theory]
    [InlineData("json", ".json")]
    [InlineData(".JSON", ".json")]
    [InlineData("*.md", ".md")]
    [InlineData(" .c++ ", ".c++")]
    [InlineData("", null)]
    [InlineData(".", null)]
    [InlineData(@"..\evil", null)]
    [InlineData(".a b", null)]
    public void NormalizeExtension(string raw, string? expected) =>
        Assert.Equal(expected, AppConfig.NormalizeExtension(raw));

    [Theory]
    [InlineData("github.com", "github.com")]
    [InlineData("gist.github.com", "gist.github.com")]
    [InlineData("a.gist.github.com", "gist.github.com")]
    [InlineData("notgithub.com", null)]
    public void FindRule_matches_subdomains_and_prefers_the_most_specific(string host, string? expected)
    {
        var config = new AppConfig();
        config.SetRule("github.com", "Chrome", "Default", "Me");
        config.SetRule("*.gist.github.com", "Edge", "Default", "Me");

        Assert.Equal(expected, config.FindRule(host)?.Domain);
    }

    [Fact]
    public void Round_trips_and_Update_preserves_changes_made_since_load()
    {
        using var tree = new TempTree();
        var original = AppConfig.PathOnDisk;
        AppConfig.PathOnDisk = Path.Combine(tree.Root, "config.json");
        try
        {
            var settings = new AppConfig();
            settings.Extensions.Add(new ExtensionRule { Extension = ".json" });
            settings.Save();

            AppConfig.Update(c => c.FindExtension(".JSON")!.LastTargetLabel = "Zed");

            var reloaded = AppConfig.Load();
            Assert.Equal("Zed", reloaded.FindExtension(".json")!.LastTargetLabel);
        }
        finally { AppConfig.PathOnDisk = original; }
    }

    [Fact]
    public void Corrupt_config_is_kept_aside_instead_of_being_overwritten()
    {
        using var tree = new TempTree();
        var original = AppConfig.PathOnDisk;
        AppConfig.PathOnDisk = tree.File("config.json", "{ not json");
        try
        {
            Assert.Empty(AppConfig.Load().Extensions);
            Assert.Equal("{ not json", File.ReadAllText(AppConfig.PathOnDisk + ".bad"));
        }
        finally { AppConfig.PathOnDisk = original; }
    }
}
