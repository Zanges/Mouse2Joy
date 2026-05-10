# Setup & Settings folder links

## Context

Two small UI tidy-ups in the Setup and Settings tabs of `MainWindow`.

The Setup tab used to permanently display "App folder: `<bin path>`" — useful only when the user is being asked to drop `interception.dll` into that folder. In every other state (the common case once drivers are installed) it was visual noise. There was also no click-to-open affordance: the user had to copy/paste the path into Explorer.

Separately, the Settings tab had no way to reach `%APPDATA%\Mouse2Joy\` — the home of `profiles\`, `settings.json`, and `logs\`. Reaching it required typing the path into Explorer. Useful for ad-hoc inspection, log forwarding when troubleshooting, manual profile backup.

## What changed

- Setup tab: a new collapsed-by-default `DllMissingPanel` groups everything related to the missing-DLL recovery flow — separator, "Fix interception.dll" bold heading, the `App folder:` line + "Open app folder" button, and the "Re-check" button. `Recheck()` shows the panel only when `InterceptionStatus == DllNotFound`. Previously `Re-check` lived in the always-visible button row; it has been moved into this panel, since the other unhealthy states (DriverNotInstalled, AdminRequired) require an app restart anyway and don't benefit from re-checking in place.
- Setup tab: the always-visible button row now contains only "Open ViGEmBus releases" and "Open Interception releases" (links to upstream).
- Setup tab: new `OnOpenAppFolder` handler opens `AppContext.BaseDirectory` via `ProcessStartInfo { UseShellExecute = true }`.
- Settings tab: new "User data" section below the Quit button — a `Separator`, bold heading, one-line description, and an "Open app data folder" button styled to match Quit (Width 180, Padding `8,4`).
- Settings tab: new `OnOpenAppDataFolder` handler opens `AppPaths.AppDataRoot` (`%APPDATA%\Mouse2Joy\`) via the same shell-execute pattern.

## Key decisions

- **Gate the entire DLL-fix section on `DllNotFound`, not just the path.** The path, the "Open app folder" button, and the "Re-check" button all serve the same recovery flow (drop the DLL, verify). Grouping them under one heading and one visibility flag presents them as a coherent action, not three disconnected widgets. In the healthy case all three vanish, leaving only the status lines and the upstream-release links.
- **Re-check is gated on `DllNotFound` too, not "any unhealthy state".** Re-check is most useful right after the user drops `interception.dll`. The other unhealthy states (`DriverNotInstalled`, `AdminRequired`) are resolved by reboot or elevated re-launch — those naturally re-run the probe at startup, so an in-place Re-check button adds nothing. Trade-off: a user who installs the driver while the app is open won't see live verification; they'll need to relaunch. Acceptable since driver install requires a reboot anyway.
- **Single root folder button on Settings, not per-subfolder.** `%APPDATA%\Mouse2Joy\` shows `profiles\`, `logs\`, and `settings.json` all at once — one Explorer click reaches any of them. Adding three buttons (root + logs + profiles) saves at most one click in the logs/profiles cases at the cost of more UI clutter. Can revisit if the user finds themselves jumping straight to logs often.
- **Reused `AppPaths.AppDataRoot` as the source of truth.** Did not hardcode `%APPDATA%\Mouse2Joy` in the UI. `AppPaths.EnsureDirectories()` already runs at app startup so the folder is guaranteed to exist by the time the user can click.
- **Click handlers in code-behind, not commands.** Mirrors the existing `OnOpenVigem` / `OnOpenInterception` pattern in `MainWindow.xaml.cs` rather than introducing a new `RelayCommand` for two trivial calls.

## Files touched

- [src/Mouse2Joy.UI/Views/MainWindow.xaml](../../src/Mouse2Joy.UI/Views/MainWindow.xaml) — `ExePathTb` wrapped in collapsed `ExePathPanel` with new "Open app folder" button; new "User data" section in Settings tab.
- [src/Mouse2Joy.UI/Views/MainWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/MainWindow.xaml.cs) — visibility gating for `ExePathPanel` in `Recheck()`; new `OnOpenAppFolder` and `OnOpenAppDataFolder` handlers.
- [src/Mouse2Joy.Persistence/AppPaths.cs](../../src/Mouse2Joy.Persistence/AppPaths.cs) — **unchanged**, reused via `AppDataRoot`.

## Follow-ups

- None planned. If the user finds themselves opening logs frequently for troubleshooting, a dedicated "Open logs folder" button could be added alongside (deliberately deferred to keep the Settings tab uncluttered until proven needed).
