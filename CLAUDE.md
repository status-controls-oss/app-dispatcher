# App Dispatcher

README.md covers what this does and how it's laid out. This file covers what's easy to get wrong while working on it.

## Commands

```powershell
dotnet test tests\AppDispatcher.Tests   # Unit tests
.\publish.ps1                           # Builds both exes into artifacts\AppDispatcher; needs the VS C++ tools
.\assets\make-icon.ps1                  # Regenerates assets\AppDispatcher.ico
```

Publishing is what proves the picker still compiles under Native AOT, so run it as well as the tests when picker or Core code changes.

## Speed comes first

A slow picker is worse than the Windows context menu, so time to first paint is the top requirement.

- `src/AppDispatcher` is Native AOT with hand-painted raw Win32. Don't add WPF, common controls or anything else that loads at startup.
- Anything that scans the registry or disk (`BrowserCatalog`, `FileHandlers`) runs only in Settings, never on the picker's path.
- `src/AppDispatcher.Core` is shared. `Shortcut` uses COM interop, which Native AOT doesn't support, and is safe only because the picker never calls it.
- Slow work in the picker goes on the background thread in `LoadIconsInBackground`, after the window paints.
- When changing the picker, compare startup against the installed build over many alternating runs. Single runs are too noisy to mean anything.

## Windows behaviour

- Windows reserves the choice of default app for the user. Don't try to set `UserChoice` from code.
- A process's first `ShowWindow` obeys the launcher's `STARTUPINFO`, so the picker calls it twice. Removing that makes it invisible when opened by tools that launch hidden.
- Rider ignores `--temp-project` and fails on `--edit` when already running. Don't re-add them.

## Security

URLs and file names reach launched apps from untrusted sources, so:

- `Launcher.CanLaunch` allows only `.exe` targets, and not `cmd.exe`. Don't weaken it: `cmd.exe` would treat `&` in a link or file name as a new command, and .NET's quoting doesn't prevent that.
- Pass arguments through `ProcessStartInfo.ArgumentList`, never a built command string.

## Testing on a machine with a real install

The installed copy may be handling real file and link opens, and its config and registry entries are live.

- Don't click **Unregister**, delete `%APPDATA%\AppDispatcher\config.json`, or overwrite the install folder without the operator's agreement.
- A copy run from `artifacts` still reads the real config, and saves to it if you change anything in Settings. It shows "Not registered yet" because registration records the installed path.
- Opening a URL on this machine goes through the installed picker, so an automated launch can leave a window waiting on the operator's screen.
- Don't test new flags against a running Rider. Earlier probes logged internal errors and left a stray tab.
- Don't run `code --help` where `Code.exe` is marked "Run as administrator". It left a stray elevated window.
- Don't move the operator's mouse to test hover, or capture the screen. Use `PrintWindow` on the app's own window instead.

## Tests

`AppConfig.PathOnDisk` is static and xUnit runs test classes in parallel. Any test that overrides it must be in `AppConfigTests`, or it can race another test and reach the real config.
