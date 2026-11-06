# App Dispatcher

Makes Windows ask which app should open a file or link. Register it as the default for a file type, list the apps that make sense for that type, and double-clicking a file shows a picker in about 30 ms.

It grew out of Eric's WebDispatch, whose link handling it keeps: choosing a browser and profile per link, plus "always open this domain" rules.

## Using the picker

| Input | Action |
|---|---|
| `1`–`9` or click | Open with that row |
| Arrows, then `Enter` | Move, then open with the selected row |
| `Esc` | Cancel |
| `Space` or `Tab` (links only) | Toggle "Always open" or edit its domain |

The row you used last is preselected. If only one app is available for a file, it opens straight away with no picker.

## Install

Requires the .NET 8 SDK, the .NET 8 Desktop Runtime and the Visual Studio C++ build tools.

```powershell
.\publish.ps1
```

1. Move `artifacts\AppDispatcher` somewhere permanent, because registration records the exe's path.
2. Run `AppDispatcher.exe` to open Settings.
3. On **File types**, add extensions and choose apps for each.
4. Click **Register**.
5. Click **Open Windows Default Apps** and choose App Dispatcher for each type (and HTTP/HTTPS for links). Windows doesn't allow apps to set this themselves.

Registration is per-user and needs no admin rights. **Unregister** removes it. Any file type still set to App Dispatcher then needs a new default chosen in Windows.

## Window modes

Each picker row is an app plus a way of invoking it. These are built in:

| App | Modes |
|---|---|
| Zed | Existing window (`-e`), new window (`-n`) |
| Rider | Current window, the file's solution, the file's folder in a new window |
| VS Code | Existing window (`-r`), new window (`-n`) |
| Notepad++ | Normal, new instance (`-multiInst -nosession`) |
| Visual Studio | Existing instance (`/Edit`), new instance |

Custom apps can use `{file}`, `{dir}` and `{solution}` in their arguments, and `{solutionName}` in their label. A row using a solution appears only when a `.sln` or `.slnx` exists above the file, searching up to the git root. Network paths aren't searched.

### Rider

Rider runs one process per install, so "new instance" isn't available and these modes open windows instead. Rider 2026.2 doesn't support the obvious flags when already running: `--temp-project` is ignored and `--edit` fails with an internal error. A separate window therefore means opening a solution or folder as its own project:

- **Rider — current window** opens the file in whichever window Rider picks, which can be an unrelated solution.
- **Rider — \<solution\>** opens the owning solution, which is the way to keep tabs with their project.
- **Rider — file's folder in new window** works for loose files, but loads a full project, asks you to trust new folders once, and creates `.idea` in that folder.

Declining Rider's trust prompt still opens the file, in your current Rider window.

## Why two executables

Startup time decides whether this is worth using, so the two jobs are built differently:

| Exe | Built with | Time to visible window |
|---|---|---|
| `AppDispatcher.exe` (picker) | Native AOT, raw Win32 | ~30 ms |
| WebDispatch's WPF picker, for comparison | WPF, JIT | ~450 ms |
| The same WPF picker with ReadyToRun | WPF, precompiled | ~440 ms |
| `AppDispatcher.Settings.exe` | WPF | Not measured; speed doesn't matter here |

Measured from process start to window visible, with warm disk caches. ReadyToRun barely helps because WPF's own startup dominates, and WPF can't be compiled with Native AOT.

To keep it fast, the picker paints before loading icons, which arrive from a background thread. It saves state only after launching the chosen app.

## Layout

```
src/AppDispatcher.Core      Config, target resolution, discovery, registration, launching
src/AppDispatcher           The picker (Native AOT)
src/AppDispatcher.Settings  The settings window (WPF)
tests/AppDispatcher.Tests   Unit tests: dotnet test
```

Config lives in `%APPDATA%\AppDispatcher\config.json`. On first run, link rules are read from an existing WebDispatch config.

## Known limitations

- Opening several selected files at once shows one picker per file, because Explorer starts one process per file.
- x64 only, as `publish.ps1` targets `win-x64`.
- An app marked "Run as administrator" in its compatibility settings shows a UAC prompt on every open.
