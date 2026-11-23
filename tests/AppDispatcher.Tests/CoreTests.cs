using AppDispatcher.Core;

namespace AppDispatcher.Tests;

public sealed class TempTree : IDisposable
{
    /// <summary>Not under %TEMP%, where SolutionFinder deliberately finds nothing.</summary>
    public string Root { get; } = Directory.CreateDirectory(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AppDispatcher.Tests", Guid.NewGuid().ToString("N"))).FullName;

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

public class SolutionBoundaryTests
{
    [Fact]
    public void A_solution_in_a_shared_folder_is_not_offered_for_files_in_or_below_it()
    {
        using var tree = new TempTree();
        var downloads = tree.Dir("Downloads");
        tree.File("Downloads/planted.sln");
        var beside = tree.File("Downloads/a.json");
        var below = tree.File("Downloads/sub/b.json");

        Assert.Null(SolutionFinder.Find(beside, new[] { downloads }));
        Assert.Null(SolutionFinder.Find(below, new[] { downloads + "\\" }));
    }

    [Fact]
    public void A_project_inside_a_shared_folder_still_finds_its_own_solution()
    {
        using var tree = new TempTree();
        var downloads = tree.Dir("Downloads");
        var sln = tree.File("Downloads/project/App.sln");
        var file = tree.File("Downloads/project/src/a.json");

        Assert.Equal(sln, SolutionFinder.Find(file, new[] { downloads }));
    }

    [Fact]
    public void A_git_file_marks_the_root_of_a_worktree_or_submodule()
    {
        using var tree = new TempTree();
        tree.File("Above.sln");
        tree.File("worktree/.git", "gitdir: elsewhere");
        var file = tree.File("worktree/data/a.json");

        Assert.Null(SolutionFinder.Find(file, Array.Empty<string>()));
    }

    [Fact]
    public void The_default_shared_folders_leave_ordinary_projects_alone()
    {
        using var tree = new TempTree();
        var sln = tree.File("repo/App.sln");

        Assert.Equal(sln, SolutionFinder.Find(tree.File("repo/src/a.json")));
    }

    [Fact]
    public void Nothing_below_an_untrusted_root_gets_a_solution()
    {
        using var tree = new TempTree();
        tree.File("Temp/Temp1_attachment.zip/evil.sln");
        var file = tree.File("Temp/Temp1_attachment.zip/readme.txt");

        Assert.Null(SolutionFinder.Find(file, Array.Empty<string>(), new[] { Path.Combine(tree.Root, "Temp") }));
        Assert.NotNull(SolutionFinder.Find(file, Array.Empty<string>(), Array.Empty<string>()));
    }

    [Fact]
    public void The_temp_folder_is_untrusted_by_default()
    {
        var file = Path.Combine(Path.GetTempPath(), "Temp1_attachment.zip", "readme.txt");
        Assert.Null(SolutionFinder.Find(file, Array.Empty<string>()));
    }
}

public class FileArgumentTests
{
    [Theory]
    [InlineData(@"C:\Users\me\a.json", true)]
    [InlineData(@"\\server\share\a.json", true)]
    [InlineData(@"a.json", true)]
    [InlineData(@"sub\a.json", true)]
    [InlineData(@"..\a.json", true)]
    [InlineData(@"appdispatcher.url:x/../../../Users/me/a.json", false)]
    [InlineData(@"file:///C:/Users/me/a.json", false)]
    [InlineData(@"ftp://example.com/a.json", false)]
    [InlineData(@"http://[bad/../a.json", false)]
    [InlineData(@"C:a.json", false)]
    public void Only_real_paths_are_treated_as_files(string argument, bool isFile) =>
        Assert.Equal(isFile, TargetResolver.IsFilePath(argument));
}

public class TargetResolverTests
{
    static FileTarget Target(string label, string exe, params string[] args) =>
        new() { Label = label, Exe = exe, Args = args.ToList() };

    [Fact]
    public void Placeholder_text_inside_a_path_is_left_alone()
    {
        using var tree = new TempTree();
        var exe = tree.File("app.exe");
        var sln = tree.File("repo/App.sln");
        var file = tree.File("repo/pa{solution}y{dir}load{file}.json");

        var result = TargetResolver.Resolve(new[] { Target("App", exe, "{solution}", "{file}", "--dir={dir}") }, file).Single();

        Assert.Equal(new[] { sln, file, "--dir=" + Path.GetDirectoryName(file) }, result.Args);
    }

    [Fact]
    public void A_folder_named_like_a_placeholder_does_not_change_the_folder_passed()
    {
        using var tree = new TempTree();
        var exe = tree.File("app.exe");
        var file = tree.File("{solution}/a.json");

        var result = TargetResolver.Resolve(new[] { Target("App", exe, "{dir}") }, file).Single();

        Assert.Equal(Path.GetDirectoryName(file), result.Args.Single());
    }

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

public class ScriptTargetTests
{
    [Theory]
    [InlineData(@"C:\apps\brave.exe", true)]
    [InlineData(@"C:\apps\BRAVE.EXE", true)]
    [InlineData(@"C:\apps\open.cmd", false)]
    [InlineData(@"C:\apps\open.BAT", false)]
    [InlineData(@"C:\apps\open.exe.cmd", false)]
    [InlineData(@"C:\apps\open.com", false)]
    [InlineData(@"C:\apps\open.ps1", false)]
    [InlineData(@"C:\apps\open", false)]
    [InlineData(@"C:\Windows\System32\cmd.exe", false)]
    [InlineData(@"C:\Windows\System32\CMD.EXE", false)]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", false)]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe", false)]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\WindowsApps\wt.exe", false)]
    [InlineData(@"C:\Windows\System32\conhost.exe", false)]
    [InlineData(@"C:\Windows\System32\wsl.exe", false)]
    [InlineData(@"C:\Windows\System32\mshta.exe", false)]
    [InlineData(@"C:\Windows\System32\rundll32.exe", false)]
    [InlineData("brave.exe", false)]
    [InlineData(@"apps\brave.exe", false)]
    [InlineData(@"\apps\brave.exe", false)]
    [InlineData(@"C:brave.exe", false)]
    [InlineData(@"\\server\share\brave.exe", true)]
    public void Only_real_programs_can_be_launched(string exe, bool allowed) =>
        Assert.Equal(allowed, Launcher.CanLaunch(exe));

    [Fact]
    public void A_crafted_link_cannot_run_commands_through_a_script()
    {
        using var tree = new TempTree();
        var proof = Path.Combine(tree.Root, "ran.txt");
        var script = tree.File("open.cmd", $"@echo ran>\"{proof}\"");
        var target = new BrowserTarget { Browser = "Script", ExePath = script, CustomArgs = new[] { "{url}" } };

        var refused = Assert.Throws<System.ComponentModel.Win32Exception>(() => target.Launch(new Uri("https://evil.example/&calc&")));

        Assert.Contains("Only .exe programs", refused.Message);
        Assert.False(File.Exists(proof));
    }

    [Fact]
    public void A_script_in_an_edited_config_gets_no_row()
    {
        using var tree = new TempTree();
        var script = tree.File("open.cmd");
        var file = tree.File("a.json");

        Assert.Empty(BrowserDiscovery.FromCustom(new[] { new CustomBrowser { Label = "Script", Exe = script, Args = new() { "{url}" } } }, null));
        Assert.Empty(TargetResolver.Resolve(new[] { new FileTarget { Label = "Script", Exe = script, Args = new() { "{file}" } } }, file));
    }

    [Fact]
    public void Scripts_and_cmd_are_not_offered_as_handlers_or_browsers()
    {
        using var tree = new TempTree();
        var script = tree.File("open.cmd");
        var cmd = tree.File(@"sys\cmd.exe");
        var handlers = new[]
        {
            new RawHandler("Script", $"\"{script}\" \"%1\""),
            new RawHandler("Shell", $"\"{cmd}\" /c start \"\" \"%1\""),
        };
        var browsers = new[]
        {
            new RegisteredBrowser("Script", "Script", $"\"{script}\""),
            new RegisteredBrowser("Shell", "Shell", $"\"{cmd}\""),
        };
        var roots = new BrowserRoots(Path.Combine(tree.Root, "local"), Path.Combine(tree.Root, "roaming"), new[] { Path.Combine(tree.Root, "pf") });

        Assert.Empty(FileHandlers.Build(handlers, null, _ => null));
        Assert.Empty(BrowserCatalog.Detect(roots, browsers, Array.Empty<string>()));
    }
}

public class FileHandlersTests
{
    static string? NoDescription(string exe) => null;

    [Fact]
    public void Side_by_side_versions_are_named_after_their_folders()
    {
        using var tree = new TempTree();
        var older = tree.File(@"Designer 9.10\designer.exe");
        var newer = tree.File(@"Designer 10.4\designer.exe");

        var rows = FileHandlers.Build(new[]
        {
            new RawHandler(null, $"\"{older}\" \"%1\""),
            new RawHandler(null, $"\"{newer}\" \"%1\""),
        }, null, _ => "Designer");

        Assert.Equal(new[] { "Designer 10.4", "Designer 9.10" }, rows.Select(r => r.Label));
        Assert.Equal(new[] { "{file}" }, rows[0].Args);
    }

    [Fact]
    public void Names_come_from_the_registry_then_the_exe_then_its_file_name()
    {
        using var tree = new TempTree();
        var a = tree.File(@"a\one.exe");
        var b = tree.File(@"b\two.exe");
        var c = tree.File(@"c\three.exe");

        var rows = FileHandlers.Build(new[]
        {
            new RawHandler("Registered Name", $"\"{a}\" %1"),
            new RawHandler(null, $"\"{b}\" %1"),
            new RawHandler(null, $"\"{c}\" %1"),
        }, null, exe => exe == b ? "Described" : exe == a ? "Ignored" : " ");

        Assert.Equal(new[] { "Described", "Registered Name", "three" }, rows.Select(r => r.Label));
    }

    [Fact]
    public void Skips_the_dispatcher_missing_programs_and_duplicates()
    {
        using var tree = new TempTree();
        var self = tree.File("AppDispatcher.exe");
        var app = tree.File("app.exe");

        var rows = FileHandlers.Build(new[]
        {
            new RawHandler(null, $"\"{self.ToUpperInvariant()}\" \"%1\""),
            new RawHandler(null, $"\"{Path.Combine(tree.Root, "gone.exe")}\" \"%1\""),
            new RawHandler(null, $"\"{app}\" \"%1\""),
            new RawHandler(null, $"\"{app}\" %1"),
            new RawHandler(null, ""),
        }, self, NoDescription);

        Assert.Equal(app, rows.Single().Exe);
    }

    [Fact]
    public void The_same_program_with_different_arguments_is_kept()
    {
        using var tree = new TempTree();
        var app = tree.File("app.exe");

        var rows = FileHandlers.Build(new[]
        {
            new RawHandler("Open", $"\"{app}\" \"%1\""),
            new RawHandler("Edit", $"\"{app}\" --edit \"%1\""),
        }, null, NoDescription);

        Assert.Equal(2, rows.Count);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\A\\a.exe\" \"%1\"", "C:\\Program Files\\A\\a.exe", new[] { "{file}" })]
    [InlineData("\"C:\\A\\a.exe\" --new %1", "C:\\A\\a.exe", new[] { "--new", "{file}" })]
    [InlineData("C:\\Program Files\\A\\a.exe %L", "C:\\Program Files\\A\\a.exe", new[] { "{file}" })]
    [InlineData("\"C:\\A\\a.exe\"", "C:\\A\\a.exe", new[] { "{file}" })]
    [InlineData("\"C:\\A\\a.exe\" \"%1\" %*", "C:\\A\\a.exe", new[] { "{file}" })]
    [InlineData("\"C:\\A\\a.exe\" --title \"my app\" \"%1\"", "C:\\A\\a.exe", new[] { "--title", "my app", "{file}" })]
    public void ParseCommand_turns_a_registry_command_into_picker_arguments(string command, string exe, string[] args)
    {
        var parsed = FileHandlers.ParseCommand(command);
        Assert.Equal(exe, parsed?.Exe);
        Assert.Equal(args, parsed?.Args);
    }
}

public class SetupGuideTests
{
    [Theory]
    [InlineData(false, 3, true, SetupState.NotRegistered)]
    [InlineData(true, 0, true, SetupState.NoApps)]
    [InlineData(true, 0, false, SetupState.NoApps)]
    [InlineData(true, 2, false, SetupState.NotDefault)]
    [InlineData(true, 2, true, SetupState.Ready)]
    public void Reports_the_earliest_unfinished_step(bool registered, int apps, bool isDefault, SetupState expected) =>
        Assert.Equal(expected, SetupGuide.StateOf(registered, apps, isDefault));

    [Fact]
    public void A_single_app_is_explained_because_it_skips_the_picker()
    {
        Assert.Contains("open straight in this app", SetupGuide.Message(".qsys", SetupState.Ready, 1));
        Assert.Contains("shows the picker", SetupGuide.Message(".qsys", SetupState.Ready, 2));
    }

    [Theory]
    [InlineData(SetupState.NotRegistered)]
    [InlineData(SetupState.NoApps)]
    [InlineData(SetupState.NotDefault)]
    [InlineData(SetupState.Ready)]
    public void Every_state_names_the_file_type(SetupState state) =>
        Assert.Contains(".qsys", SetupGuide.Message(".qsys", state, 1));

    [Fact]
    public void The_links_banner_describes_what_links_currently_do()
    {
        Assert.Contains("show the picker unless", SetupGuide.LinkMessage(SetupState.Ready, null));
        Assert.Contains("open in Brave — Work unless", SetupGuide.LinkMessage(SetupState.Ready, "Brave — Work"));
    }

    [Theory]
    [InlineData(SetupState.NotRegistered, "Register")]
    [InlineData(SetupState.NoApps, "add a browser")]
    [InlineData(SetupState.NotDefault, "HTTP and HTTPS")]
    public void The_links_banner_gives_the_next_step_before_it_is_ready(SetupState state, string expected) =>
        Assert.Contains(expected, SetupGuide.LinkMessage(state, "Ignored until ready"));

    [Fact]
    public void The_windows_step_says_the_choice_is_the_users()
    {
        Assert.Contains("only you can change that", SetupGuide.Message(".qsys", SetupState.NotDefault, 2));
    }
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

    static (string, string)? Direct(AppConfig config, string url) => config.BrowserWithoutAsking(new Uri(url));

    [Fact]
    public void With_no_default_browser_links_ask_unless_a_rule_says_otherwise()
    {
        var config = new AppConfig();
        config.SetRule("github.com", "Chrome", "Work", "Work");

        Assert.Null(Direct(config, "https://example.com/"));
        Assert.Equal(("Chrome", "Work"), Direct(config, "https://github.com/"));
    }

    [Fact]
    public void With_a_default_browser_links_open_there_unless_a_rule_says_otherwise()
    {
        var config = new AppConfig { DefaultBrowser = "Brave", DefaultProfileId = "Default" };
        config.SetRule("github.com", Rule.ShowPicker, "", "");
        config.SetRule("mail.example.com", "Edge", "Default", "Me");

        Assert.Equal(("Brave", "Default"), Direct(config, "https://example.com/"));
        Assert.Null(Direct(config, "https://github.com/x"));
        Assert.Null(Direct(config, "https://gist.github.com/"));
        Assert.Equal(("Edge", "Default"), Direct(config, "https://mail.example.com/"));
    }

    [Fact]
    public void A_more_specific_rule_can_undo_a_broader_one()
    {
        var config = new AppConfig { DefaultBrowser = "Brave" };
        config.SetRule("github.com", Rule.ShowPicker, "", "");
        config.SetRule("github.com/myorg", "Chrome", "Work", "Work");
        config.SetRule("docs.github.com", "Edge", "Default", "Me");

        Assert.Null(Direct(config, "https://github.com/other"));
        Assert.Equal(("Chrome", "Work"), Direct(config, "https://github.com/myorg/repo"));
        Assert.Equal(("Edge", "Default"), Direct(config, "https://docs.github.com/en"));
    }

    [Theory]
    [InlineData("/team", true)]
    [InlineData("/TEAM/page", true)]
    [InlineData("/team/", true)]
    [InlineData("/teams", false)]
    [InlineData("/", false)]
    public void A_path_rule_matches_that_path_and_below_only(string path, bool matches)
    {
        var config = new AppConfig();
        config.SetRule("example.com/team", "Chrome", "Default", "Me");

        Assert.Equal(matches, config.FindRule("example.com", path) != null);
    }

    [Theory]
    [InlineData("GitHub.com", "github.com")]
    [InlineData("*.github.com", "github.com")]
    [InlineData("https://github.com/MyOrg/?tab=repos#top", "github.com/myorg")]
    [InlineData("github.com/myorg/", "github.com/myorg")]
    [InlineData("http://localhost:3000/app", "localhost/app")]
    [InlineData(" example.com. ", "example.com")]
    [InlineData("Bücher.example", "xn--bcher-kva.example")]
    public void NormalizeDomain_accepts_domains_and_pasted_links(string raw, string expected) =>
        Assert.Equal(expected, AppConfig.NormalizeDomain(raw));

    [Theory]
    [InlineData("https://bank.com./")]
    [InlineData("https://BANK.com./login")]
    [InlineData("https://www.bank.com./")]
    public void A_trailing_dot_doesnt_skip_a_rule(string url)
    {
        var config = new AppConfig { DefaultBrowser = "Brave" };
        config.SetRule("bank.com", Rule.ShowPicker, "", "");

        Assert.Null(Direct(config, url));
    }

    [Theory]
    [InlineData("https://bücher.example/")]
    [InlineData("https://xn--bcher-kva.example/")]
    public void An_international_domain_matches_in_either_form(string url)
    {
        var config = new AppConfig { DefaultBrowser = "Brave" };
        config.SetRule("bücher.example", Rule.ShowPicker, "", "");
        // Rules saved before hosts were normalized can still be in Unicode.
        config.Rules.Add(new Rule { Domain = "Straße.example", Browser = Rule.ShowPicker });

        Assert.Null(Direct(config, url));
        Assert.Null(Direct(config, "https://xn--strae-oqa.example/"));
    }

    [Fact]
    public void An_existing_config_keeps_showing_the_picker_and_the_default_browser_is_saved()
    {
        using var tree = new TempTree();
        var original = AppConfig.PathOnDisk;
        AppConfig.PathOnDisk = tree.File("config.json", """{ "Rules": [] }""");
        try
        {
            Assert.Null(AppConfig.Load().BrowserWithoutAsking(new Uri("https://example.com/")));

            AppConfig.Update(c => { c.DefaultBrowser = "Brave"; c.DefaultProfileId = "Default"; });

            Assert.Equal(("Brave", "Default"), AppConfig.Load().BrowserWithoutAsking(new Uri("https://example.com/")));
        }
        finally { AppConfig.PathOnDisk = original; }
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
