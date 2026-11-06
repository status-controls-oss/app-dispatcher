# Handoff — 2026-11-06 11:39

**Branch:** main (level with origin/main at 65e6a68)
**Repo state:** clean apart from these handoff files

## Goal
Expand Eric's WebDispatch into App Dispatcher: the Windows default for chosen file types and links, showing a very fast picker of apps and window modes. Speed is the top requirement, because a slow picker is worse than the Windows context menu.

## Status
- Merged to main and in daily use by the operator. The feature branch is deleted.
- Installed at `%LOCALAPPDATA%\Programs\AppDispatcher`. Config is at `%APPDATA%\AppDispatcher\config.json`.
- App Dispatcher is the operator's Windows default for `.json` and `.md`, each with Zed and Rider rows. `.json` previously opened in Rider. HTTP/HTTPS status has not been checked.
- Confirmed by the operator: opening a `.md` under a solution offered the correct `.sln`, and choosing it opened a new window with that solution.
- Measured with warm caches, process start to visible window: picker ~30 ms, WebDispatch's WPF picker ~450 ms. Cold start not measured.
- Not verified:
  - Rider solution row when the solution is already open (expected to reuse its window).
  - Rider folder row: file landing in the new window after trust is accepted.
  - Link flow with a real browser launch, including an "always open" rule.
  - Elevation fallback (`Launcher.cs`) against real VS Code; no configured file type has a VS Code row yet.
  - Zed existing-window; all VS Code, Notepad++ and Visual Studio modes; DPI changes.

## Next step
No task is in progress; ask the operator what they want next. Candidates: test the unverified items above, or fix the known limitation that opening several selected files shows one picker per file.

## Blockers / waiting on
Operator decisions:
- Whether to remove "Run as administrator" from `Code.exe`. It is set in HKCU AppCompatFlags and causes a UAC prompt on every VS Code open. Leave it unless told otherwise.
- Whether merge request !1 needs closing. It may have closed itself, and the page hasn't been checked; `glab` isn't installed and no token is set.

## Context pointers
- `README.md` — behaviour, Rider window modes, the speed rationale, known limitations.
- `src/AppDispatcher/PickerWindow.cs` — hand-painted picker; deliberately no WPF or common controls.
- `src/AppDispatcher.Core/EditorCatalog.cs` — editor discovery and window-mode presets. Rider ignores `--temp-project` and fails on `--edit` when already running; don't re-add them.
- `src/AppDispatcher.Core/Registration.cs` — registry layout. Windows requires the user to choose defaults; don't try to set `UserChoice` programmatically.
- `publish.ps1` — needs the VS C++ tools, and adds `vswhere` to PATH for the AOT linker. Its output goes to `artifacts\AppDispatcher`, so copy it to the install folder to update the real install.
- memory: `skip-branches-for-now.md` — work directly on main for now.

## Avoid
- The real install handles the operator's `.json` and `.md` opens, and its config and registry entries are live. Don't use Unregister, delete the config, or overwrite the install folder while testing without their agreement. Tests that create throwaway config assume none exists, so they no longer apply as written.
- Don't test new flags against the operator's running Rider. The earlier probes logged internal errors and left a stray tab in their window.
- Don't run `code --help` here. `code` launches elevated on this machine, and it left a stray `cli.js` Administrator window.

## Uncommitted files
```
?? .claude/handoffs/handoff-20261106-113900.md
 M .claude/handoffs/latest.md
```
`WebDispatch.zip` (Eric's original source) and `artifacts/` are present locally but gitignored.
