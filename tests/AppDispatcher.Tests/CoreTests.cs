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

public class CustomBrowserTests
{
    static readonly Uri Url = new("https://example.com/a?b=c");

    [Fact]
    public void Expands_url_inside_and_as_a_whole_argument()
    {
        var target = new BrowserTarget { Browser = "Brave", CustomArgs = new[] { "--incognito", "--app={url}", "{url}" } };

        Assert.Equal(new[] { "--incognito", "--app=https://example.com/a?b=c", "https://example.com/a?b=c" }, target.LaunchArgs(Url));
    }

    [Fact]
    public void Shows_only_its_label_and_matches_with_an_empty_profile()
    {
        using var tree = new TempTree();
        var exe = tree.File("brave.exe");

        var target = BrowserDiscovery.FromCustom(new[] { new CustomBrowser { Label = "Brave", Exe = exe } }, null).Single();

        Assert.Equal("Brave", target.Display);
        Assert.True(target.Matches("Brave", ""));
    }

    [Fact]
    public void Skips_missing_programs_and_the_dispatcher_itself()
    {
        using var tree = new TempTree();
        var self = tree.File("AppDispatcher.exe");
        var browsers = new[]
        {
            new CustomBrowser { Label = "Gone", Exe = Path.Combine(tree.Root, "gone.exe") },
            new CustomBrowser { Label = "Loop", Exe = self.ToUpperInvariant() },
        };

        Assert.Empty(BrowserDiscovery.FromCustom(browsers, self));
    }

    [Fact]
    public void Installed_browsers_keep_their_profile_arguments()
    {
        var chrome = new BrowserTarget { Browser = "Chrome", ProfileId = "Profile 1", ProfileName = "Work" };
        var firefox = new BrowserTarget { Browser = "Firefox", ProfileId = "dev", ProfileName = "dev" };

        Assert.Equal("Chrome — Work", chrome.Display);
        Assert.Equal(new[] { "--profile-directory=Profile 1", Url.AbsoluteUri }, chrome.LaunchArgs(Url));
        Assert.Equal(new[] { "-P", "dev", "-url", Url.AbsoluteUri }, firefox.LaunchArgs(Url));
    }

    [Theory]
    [InlineData("", new[] { "{url}" })]
    [InlineData("--incognito", new[] { "--incognito", "{url}" })]
    [InlineData("--app={url}", new[] { "--app={url}" })]
    public void ParseArgs_ensures_the_url_placeholder(string text, string[] expected) =>
        Assert.Equal(expected, TargetResolver.ParseArgs(text, BrowserTarget.UrlPlaceholder));
}

public class ShortcutTests
{
    [Fact]
    public void Creates_a_shortcut_Windows_can_read_and_can_replace_and_delete_it()
    {
        using var tree = new TempTree();
        var first = tree.File(@"a b\first.exe");
        var second = tree.File(@"a b\second.exe");
        var path = Path.Combine(tree.Root, "App.lnk");

        Shortcut.Create(path, first, "Old");
        Shortcut.Create(path, second, "New");

        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic link = shell.CreateShortcut(path);
        Assert.Equal(second, (string)link.TargetPath, ignoreCase: true);
        Assert.Equal(Path.GetDirectoryName(second), (string)link.WorkingDirectory, ignoreCase: true);
        Assert.Equal("New", (string)link.Description);

        Shortcut.Delete(path);
        Assert.False(File.Exists(path));
        Shortcut.Delete(path); // Unregistering twice must not fail.
    }
}

public class BrowserCatalogTests
{
    const string Url = BrowserTarget.UrlPlaceholder;

    static BrowserRoots Roots(TempTree tree) =>
        new(Path.Combine(tree.Root, "local"), Path.Combine(tree.Root, "roaming"), new[] { Path.Combine(tree.Root, "pf") });

    static List<CustomBrowser> Detect(TempTree tree, params RegisteredBrowser[] registered) =>
        BrowserCatalog.Detect(Roots(tree), registered, Array.Empty<string>());

    [Fact]
    public void Offers_one_row_per_profile_of_a_chromium_browser()
    {
        using var tree = new TempTree();
        var exe = tree.File(@"pf\BraveSoftware\Brave-Browser\Application\brave.exe");
        tree.File(@"local\BraveSoftware\Brave-Browser\User Data\Local State",
            """{ "profile": { "info_cache": { "Default": { "name": "Home" }, "Profile 2": { "name": "Work", "user_name": "me@work" } } } }""");

        var rows = Detect(tree);

        Assert.All(rows, r => Assert.Equal(exe, r.Exe));
        Assert.Equal(new[] { "Brave — Home", "Brave — Work (me@work)" }, rows.Select(r => r.Label));
        Assert.Equal(new[] { "--profile-directory=Profile 2", Url }, rows[1].Args);
    }

    [Fact]
    public void Offers_one_row_per_profile_of_a_firefox_family_browser()
    {
        using var tree = new TempTree();
        tree.File(@"pf\LibreWolf\librewolf.exe");
        tree.File(@"roaming\librewolf\profiles.ini", "[General]\nVersion=2\n[Profile0]\nName=dev\nPath=x\n");

        var row = Detect(tree).Single();

        Assert.Equal("LibreWolf — dev", row.Label);
        Assert.Equal(new[] { "-P", "dev", "-url", Url }, row.Args);
    }

    [Fact]
    public void A_browser_with_no_profile_data_still_gets_a_plain_row()
    {
        using var tree = new TempTree();
        tree.File(@"pf\Vivaldi\Application\vivaldi.exe");

        var row = Detect(tree).Single();

        Assert.Equal("Vivaldi", row.Label);
        Assert.Equal(new[] { Url }, row.Args);
    }

    [Fact]
    public void A_registered_install_in_a_custom_folder_is_recognised_and_keeps_its_profiles()
    {
        using var tree = new TempTree();
        var exe = tree.File(@"D\apps\brave.exe");
        tree.File(@"local\BraveSoftware\Brave-Browser\User Data\Local State",
            """{ "profile": { "info_cache": { "Default": { "name": "Home" } } } }""");

        var row = Detect(tree, new RegisteredBrowser("Brave", "Brave", $"\"{exe}\"")).Single();

        Assert.Equal(exe, row.Exe);
        Assert.Equal("Brave — Home", row.Label);
    }

    [Fact]
    public void Opera_GX_is_not_mistaken_for_Opera()
    {
        using var tree = new TempTree();
        var exe = tree.File(@"custom\launcher.exe");

        var row = Detect(tree, new RegisteredBrowser("Opera GXStable", "Opera GX Stable", $"\"{exe}\"")).Single();

        Assert.Equal("Opera GX", row.Label);
    }

    [Fact]
    public void An_unknown_registered_browser_is_offered_once_under_its_own_name()
    {
        using var tree = new TempTree();
        var exe = tree.File(@"new\newbrowser.exe");
        var entry = new RegisteredBrowser("NewBrowser", "New Browser", $"\"{exe}\" --flag");

        var row = Detect(tree, entry, entry).Single();

        Assert.Equal("New Browser", row.Label);
        Assert.Equal(exe, row.Exe);
        Assert.Equal(new[] { Url }, row.Args);
    }

    [Fact]
    public void Skips_listed_browsers_redirectors_pickers_and_entries_that_cannot_run()
    {
        using var tree = new TempTree();
        var chrome = tree.File(@"g\chrome.exe");
        var ie = tree.File(@"ie\iexplore.exe");
        var self = tree.File(@"ad\AppDispatcher.exe");

        var rows = BrowserCatalog.Detect(Roots(tree), new[]
        {
            new RegisteredBrowser("Google Chrome", "Google Chrome", $"\"{chrome}\""),
            new RegisteredBrowser("IEXPLORE.EXE", "Internet Explorer", ie),
            new RegisteredBrowser("AppDispatcher", "App Dispatcher", $"\"{self}\""),
            new RegisteredBrowser("Gone", "Gone", $"\"{Path.Combine(tree.Root, "gone.exe")}\""),
            new RegisteredBrowser("Blank", "", ""),
        }, new[] { chrome.ToUpperInvariant() });

        Assert.Empty(rows);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\B\\b.exe\"", "C:\\Program Files\\B\\b.exe")]
    [InlineData("\"C:\\Program Files\\B\\b.exe\" --single-argument %1", "C:\\Program Files\\B\\b.exe")]
    [InlineData("C:\\Program Files\\B\\b.exe -x", "C:\\Program Files\\B\\b.exe")]
    [InlineData("", null)]
    [InlineData("\"unterminated", null)]
    [InlineData("no program here", null)]
    public void ExeFromCommand_handles_quotes_and_arguments(string command, string? expected) =>
        Assert.Equal(expected, BrowserCatalog.ExeFromCommand(command));
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
    public void Custom_browsers_survive_a_save_and_a_config_without_them_still_loads()
    {
        using var tree = new TempTree();
        var original = AppConfig.PathOnDisk;
        AppConfig.PathOnDisk = tree.File("config.json", """{ "Rules": [] }""");
        try
        {
            Assert.Empty(AppConfig.Load().CustomBrowsers);

            AppConfig.Update(c => c.CustomBrowsers.Add(new CustomBrowser { Label = "Brave", Exe = "b.exe", Args = new() { "{url}" } }));

            var saved = AppConfig.Load().CustomBrowsers.Single();
            Assert.Equal("Brave", saved.Label);
            Assert.Equal(new[] { "{url}" }, saved.Args);
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
