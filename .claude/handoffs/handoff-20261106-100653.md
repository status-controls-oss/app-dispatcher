# Handoff — 2026-11-06 10:06

**Branch:** feature/file-type-picker (ahead of main, pushed to origin, merge request open)
**Repo state:** clean

## Goal
Expand Eric's WebDispatch into App Dispatcher: register as the Windows default for chosen file types and links, then show a very fast picker of apps and window modes. Speed is the top requirement, because a slow picker is worse than the Windows context menu.

## Status
- Done, in commit dc4a7c7: Native AOT picker, WPF Settings, shared Core library, 29 passing unit tests, `publish.ps1`, README.
- Measured with warm caches, process start to visible window: picker ~30 ms, WebDispatch's WPF picker ~450 ms. Cold start not measured.
- Verified through the published exe with a dummy target app: file picker keys, solution and folder rows, last-used preselection, single-target direct launch, real shell open of a registered `.apdtest` file, registry sync and unregister.
- Verified with real apps: only Zed's new-window mode.
- Not verified:
  - Rider: file landing in the new window after trust is accepted, and the solution row at all.
  - Choosing App Dispatcher as a real default for `.json` in Default Apps, then double-clicking in Explorer.
  - Link flow with a real browser launch, including an "always open" rule.
  - Elevation fallback (`Launcher.cs`) against real VS Code. Only the ShellExecute path was tested, with a dummy app under AOT.
  - Zed existing-window; all VS Code, Notepad++ and Visual Studio modes; DPI changes.
- Nothing is registered and no `%APPDATA%\AppDispatcher` config exists; test state was removed.

## Next step
Run the operator's acceptance test: `.\publish.ps1`, move `artifacts\AppDispatcher` somewhere permanent, run `AppDispatcher.exe`, add `.json` with Zed and Rider rows, click Register, choose App Dispatcher for `.json` in Default Apps, then open a `.json` under a solution and pick the Rider solution row. Confirm the file lands in that solution's window, and fix the Rider presets in `src/AppDispatcher.Core/EditorCatalog.cs` if not.

## Blockers / waiting on
Operator decisions:
- Whether to remove "Run as administrator" from `Code.exe`. It is set in HKCU AppCompatFlags, causes a UAC prompt on every VS Code open, and is the operator's setting, so leave it unless told otherwise.
- Whether to merge the open merge request. The operator said branches and merges can be skipped for now, so later work may go straight to main once this lands.

## Context pointers
- `README.md` — behaviour, Rider window modes, the speed rationale, known limitations.
- `src/AppDispatcher/PickerWindow.cs` — hand-painted picker; deliberately no WPF or common controls.
- `src/AppDispatcher.Core/EditorCatalog.cs` — editor discovery and window-mode presets. Rider ignores `--temp-project` and fails on `--edit` when already running; don't re-add them.
- `src/AppDispatcher.Core/Registration.cs` — registry layout. Windows requires the user to choose defaults; don't try to set `UserChoice` programmatically.
- `publish.ps1` — needs the VS C++ tools, and adds `vswhere` to PATH for the AOT linker.

## Avoid
- Don't test new flags against the operator's running Rider. The earlier probes logged internal errors and left a stray tab in their window. Use a scratch folder only with their agreement.
- Don't run `code --help` here. `code` launches elevated on this machine, and it left a stray `cli.js` Administrator window.

## Uncommitted files
none. `WebDispatch.zip` (Eric's original source) and `artifacts/` are present locally but gitignored.
