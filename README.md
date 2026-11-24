# App Dispatcher

Makes Windows ask which app should open a file or link. Register it as the default for a file type, list the apps that make sense for that type, and double-clicking a file shows a picker in well under 100 ms.

It grew out of Eric's WebDispatch, whose link handling it keeps and extends: links can show a picker of browsers and profiles, or go straight to a default browser, with rules for specific sites either way.

## Using the picker

| Input | Action |
|---|---|
| `1`–`9` or click | Open with that row |
| Arrows, then `Enter` | Move, then open with the selected row |
| `Esc` | Cancel |
| `Space` or `Tab` (links only) | Toggle "Always open" or edit its domain |
| Gear icon | Close the picker and open Settings; the file or link isn't opened |

The row you used last is preselected. If only one app is available for a file, it opens straight away with no picker.

## Install

Requires the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) for Settings. Needs no admin rights.

1. Download the zip from [GitHub releases](https://github.com/status-controls-oss/app-dispatcher/releases) and extract it. You can [verify it](#verifying-a-download) first.
2. In the extracted folder, double-click `Install.cmd`. It installs to `%LOCALAPPDATA%\Programs\AppDispatcher` and opens Settings. The files aren't signed, so Windows may show a warning first. To update, run the new version's `Install.cmd`.
3. On **File types**, add extensions and choose apps for each.
4. Click **Register**, which is in the tab's banner and on **Windows setup**.
5. Click **Open Windows Default Apps** on the banner or **Windows setup**, and choose App Dispatcher for each type (and HTTP/HTTPS for links). Windows doesn't allow apps to set this themselves. Until you do, Windows may ask how to open that type, or keep using its current app.

The banners on **File types** and **Links** show what's still needed. **Refresh status** checks again, and also looks for newly installed apps and browsers. **Windows has** lists the apps Windows already associates with that type, such as each installed version of an app, so most can be added without finding the program yourself. Microsoft Store apps aren't listed, because they can't be launched by a command line this way.

Settings' **About** tab shows the version, license and links for help and for reporting problems.

**Register** also adds **App Dispatcher Settings** to the Start menu. You can also reach Settings from the gear in the picker, or by running `AppDispatcher.exe` with no arguments. The picker itself has no Start menu entry, because it only opens when given a file or link.

Registration is per-user and needs no admin rights. **Unregister** removes it, along with the Start menu entry. Any file type still set to App Dispatcher then needs a new default chosen in Windows.

### Uninstall

Run `Uninstall.cmd` from the install folder. It removes the registration, the Start menu entry and the program, and keeps your settings. To delete those too, run `install.ps1 -Uninstall -RemoveSettings` in PowerShell. File types and links that used App Dispatcher then need a new default chosen in Windows.

### Build from source

Requires the .NET 8 SDK and the Visual Studio C++ build tools.

```powershell
.\publish.ps1   # Builds into artifacts\AppDispatcher
.\package.ps1   # Also makes the release zip, using the version in Directory.Build.props
```

If you run a build directly, keep it somewhere permanent, because registration records the exe's path.

## Security and privacy

App Dispatcher has no network code. It doesn't check for updates, send telemetry or contact any server. It reads the registry and disk to find apps and browsers, and only writes to these:

- `%APPDATA%\AppDispatcher\config.json`, your settings. A `.tmp` copy is written beside it while saving, and a `.bad` copy is kept if it can't be read.
- Per-user registry keys under `HKEY_CURRENT_USER\Software`: `Classes` for its file types, its link handler and its place in each type's **Open with** list, `Clients\StartMenuInternet\AppDispatcher`, and its entry in `RegisteredApplications`. It never writes to `HKEY_LOCAL_MACHINE`, and needs no admin rights.
- The **App Dispatcher Settings** shortcut in your Start menu.
- The install folder, `%LOCALAPPDATA%\Programs\AppDispatcher` by default.

**Unregister** and the uninstaller remove what they added to each of these, except your settings. The uninstaller deletes only the files it installed, as listed in `manifest.txt`. Empty extension keys such as `HKEY_CURRENT_USER\Software\Classes\.json` can remain, as they may be shared with other apps.

Links and file names come from untrusted sources, so the picker only starts `.exe` programs given by full path, refuses scripts and programs that run commands from their arguments (such as `cmd.exe`, PowerShell, Windows Terminal and WSL), and passes each argument separately rather than building a command line. The apps you add are otherwise trusted with the link or file name, so add only apps that treat it as something to open. To report a vulnerability, see [SECURITY.md](SECURITY.md).

### Verifying a download

Release zips are built by GitHub Actions from the tagged source, not on a developer's PC. Each release lists the zip's SHA-256 hash, which you can compare with `(Get-FileHash .\AppDispatcher-<version>-win-x64.zip).Hash` in PowerShell. To confirm the zip was built from this repo, use the [GitHub CLI](https://cli.github.com/):

```powershell
gh attestation verify .\AppDispatcher-<version>-win-x64.zip --repo status-controls-oss/app-dispatcher
```

## Window modes

Each picker row is an app plus a way of invoking it. These are built in:

| App | Modes |
|---|---|
| Zed | Existing window (`-e`), new window (`-n`) |
| Rider | Current window, the file's solution, the file's folder in a new window |
| VS Code | Existing window (`-r`), new window (`-n`) |
| Notepad++ | Normal, new instance (`-multiInst -nosession`) |
| Visual Studio | Existing instance (`/Edit`), new instance |

Custom apps can use `{file}`, `{dir}` and `{solution}` in their arguments, and `{solutionName}` in their label. A row using a solution appears only when a `.sln` or `.slnx` exists above the file, searching up to the git root. Network paths aren't searched, and neither are drive roots or your user folder, Downloads, Desktop, Documents and temp folders themselves, so a stray solution there isn't offered for every file below it. Project folders inside them are searched as normal.

### Rider

Rider runs one process per install, so "new instance" isn't available and these modes open windows instead. Rider 2026.2 doesn't support the obvious flags when already running: `--temp-project` is ignored and `--edit` fails with an internal error. A separate window therefore means opening a solution or folder as its own project:

- **Rider — current window** opens the file in whichever window Rider picks, which can be an unrelated solution.
- **Rider — \<solution\>** opens the owning solution, which is the way to keep tabs with their project.
- **Rider — file's folder in new window** works for loose files, but loads a full project, asks you to trust new folders once, and creates `.idea` in that folder.

Declining Rider's trust prompt still opens the file, in your current Rider window.

## Browsers

**Links without a rule**, at the top of the Links tab, decides what most links do: show the picker, or open a chosen browser straight away. Rules then handle the exceptions, either way round:

| Links without a rule | Add a rule to |
|---|---|
| Show the picker | Send a site straight to one browser |
| A chosen browser | Show the picker for a site, or send it to a different browser |

A rule matches a domain and its subdomains, such as `github.com`. Adding a path, such as `github.com/myorg`, matches only that part of the site. You can also paste a whole link. When several rules match, the most specific one wins. If a rule's browser is no longer available, the picker opens instead.

Chrome, Edge and Firefox are detected automatically, with one row per profile.

To add any other browser, use **Other browsers** on the Links tab in Settings. **Found on this PC** offers browsers registered with Windows, plus common ones from a built-in list, with one entry per profile where the profiles can be read. Detection runs only in Settings, so it never slows the picker.

To add one by hand, give it a label, a program and arguments. `{url}` is replaced with the link, and is added at the end if you leave it out, for example `--incognito {url}`. Add one entry per profile or mode you want as a row.

These browsers appear after Chrome, Edge and Firefox, and one whose program is missing is hidden. Removing one also removes its "Always open" rules.

## Why two executables

Startup time decides whether this is worth using, so the two jobs are built differently:

| Exe | Built with | Time to visible window |
|---|---|---|
| `AppDispatcher.exe` (picker) | Native AOT, raw Win32 | ~30 ms |
| WebDispatch's WPF picker, for comparison | WPF, JIT | ~450 ms |
| The same WPF picker with ReadyToRun | WPF, precompiled | ~440 ms |
| `AppDispatcher.Settings.exe` | WPF | Not measured; speed doesn't matter here |

Measured from process start to window visible, with warm disk caches. ReadyToRun barely helps because WPF's own startup dominates, and WPF can't be compiled with Native AOT. A later test that launches and polls from PowerShell reads higher, with medians of 44–62 ms for this picker, so only compare times taken the same way.

To keep it fast, the picker paints before loading icons, which arrive from a background thread, and it draws its gear in code rather than loading a symbol font. It saves state only after launching the chosen app. Finding browsers and file handlers scans the registry and disk, so that happens only in Settings.

## Layout

```
src/AppDispatcher.Core      Config, target resolution, discovery, registration, launching
src/AppDispatcher           The picker (Native AOT)
src/AppDispatcher.Settings  The settings window (WPF)
tests/AppDispatcher.Tests   Unit tests: dotnet test
assets                      The app icon, and make-icon.ps1, which regenerates it
installer                   The install and uninstall scripts that ship in the release zip
```

Config lives in `%APPDATA%\AppDispatcher\config.json`. On first run, link rules are read from an existing WebDispatch config.

## Known limitations

- Opening several selected files at once shows one picker per file, because Explorer starts one process per file.
- x64 only, as `publish.ps1` targets `win-x64`.
- An app marked "Run as administrator" in its compatibility settings shows a UAC prompt on every open.
- Only `.exe` programs can be targets. Scripts such as `.cmd` and `.bat` are refused, and so are programs that run commands from their arguments, such as `cmd.exe`, PowerShell, Windows Terminal and WSL, because they would treat characters in a link or file name as commands. Use the app's own `.exe`, such as `Code.exe` rather than `code.cmd`.
- Link rules ignore the port, so a rule for `localhost` matches every local port.
- A row stores its program's path. Apps found by catalog (see [Window modes](#window-modes)) are found again after an update moves them, but an added browser or an app from **Windows has** is hidden until you add it again.

## Credits

The idea for App Dispatcher is Eric's. It started as his WebDispatch, a link picker for browsers and profiles, which this project extends to files and window modes.

## AI disclosure

Most of this project's code and documentation was written by Claude, Anthropic's AI model, using Claude Code. A person at Status Controls directed the work, chose its design and tested it. Most commits carry a `Co-Authored-By: Claude` trailer to show this.

## License

[MIT](LICENSE)
