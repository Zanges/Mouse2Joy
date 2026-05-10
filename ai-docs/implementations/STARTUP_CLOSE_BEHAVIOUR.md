# Startup & Close Behavior

## Context

Originally Mouse2Joy launched hidden (tray-only) by default and offered no in-window way to exit — the only quit path was the tray menu's Quit item. This change makes the window visible by default for fresh installs, surfaces the previously-internal `StartMinimized` setting in the UI, lets the user choose what the window's X button does (hide-to-tray vs. fully exit), and adds an explicit Quit button inside the window. It also introduces a new dedicated **Settings** tab as the home for app-level preferences and lays the groundwork for a future "Start with Windows" feature.

## What changed

- New **Settings** tab in `MainWindow` between Overlay and Setup, containing:
  - "Start minimized to tray" checkbox (controls `AppSettings.StartMinimized`).
  - "Close button minimizes to tray (instead of exiting)" checkbox (controls new `AppSettings.CloseButtonMinimizesToTray`).
  - "Start with Windows" checkbox — **disabled** with explanatory tooltip; intentional placeholder for a separate future change.
  - "Quit Mouse2Joy" button that fully exits the app.
- `AppSettings.StartMinimized` default flipped from `true` → `false` (fresh installs only; no schema bump, existing `settings.json` keeps its persisted value).
- New `AppSettings.CloseButtonMinimizesToTray` field (default `true`, preserves prior X-button-hides behavior for existing users).
- `MainWindow.Closing` event now respects the close-to-tray setting, and triggers `Application.Current.Shutdown()` when the user opts for X-fully-exits (because `ShutdownMode="OnExplicitShutdown"` would otherwise leave the app running headless in the tray).

## Key decisions

- **No schema migration.** Existing installs keep `StartMinimized=true` until the user toggles it via the new UI. Avoided bumping `CurrentSchemaVersion` to keep the change minimal and avoid silently overriding a setting users may have come to rely on. Trade-off: the user has to flip one checkbox once.
- **Close behavior is user-configurable, not a fixed policy.** Default is hide-to-tray (preserves the engine + panic hotkey safety net). User can opt into X-fully-exits.
- **X-fully-exits routes through `Shutdown()`, not just `Close()`.** Because `App.xaml`'s `ShutdownMode="OnExplicitShutdown"` keeps the process alive after the last window closes, a plain Close would look indistinguishable from minimize-to-tray (window vanishes, app + tray icon remain). Bug discovered during testing — fixed by explicitly calling `Application.Current.Shutdown()` from the Closing handler.
- **`_isShuttingDown` re-entrancy guard.** When the Quit button or tray-Quit calls `Shutdown()`, `App.OnExit` calls `_main?.Close()`, which re-fires `Closing`. Without the guard, the close-to-tray branch would cancel the closure mid-shutdown. The flag is set both by Quit-button and by the X→shutdown branch.
- **Settings tab as a new home for app-level preferences.** Existing tabs (Hotkeys, Overlay) hold feature-scoped settings; app-wide preferences had no obvious home. The new tab is also the natural place for future settings.
- **`StartWithWindows` deliberately not wired.** Mouse2Joy needs admin (Interception). A plain `HKCU\...\Run` entry would launch non-elevated at logon and capture would silently fail. The proper fix is a Task Scheduler entry registered with "Run with highest privileges" — out of scope here. Checkbox is rendered disabled with an explanatory tooltip; the persistence field stays in the model. A `// TODO` block in `MainWindow.xaml.cs` `OnLoaded` documents the rationale.
- **No new `RelayCommand`s.** New click handlers follow the existing MainWindow pattern (`Click=` in XAML → handler in code-behind that calls `_vm.SaveSettings(_vm.Settings with { ... })`).

## Files touched

- [src/Mouse2Joy.Persistence/Models/AppSettings.cs](../../src/Mouse2Joy.Persistence/Models/AppSettings.cs) — `StartMinimized` default flipped to `false`; added `CloseButtonMinimizesToTray` (default `true`).
- [src/Mouse2Joy.UI/Views/MainWindow.xaml](../../src/Mouse2Joy.UI/Views/MainWindow.xaml) — new `<TabItem Header="Settings">` between Overlay and Setup.
- [src/Mouse2Joy.UI/Views/MainWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/MainWindow.xaml.cs) — `Closing` handler with shutdown logic + `_isShuttingDown` flag, three new click handlers (`OnToggleStartMinimized`, `OnToggleCloseToTray`, `OnQuitApp`), `OnLoaded` initializes the new checkboxes and contains the `StartWithWindows` TODO.
- [src/Mouse2Joy.App/App.xaml.cs](../../src/Mouse2Joy.App/App.xaml.cs) — **unchanged**. Existing `if (!settings.StartMinimized) _main.Show();` (line ~130) already reads the setting correctly; no edits were needed.

## Follow-ups

- **Implement elevated auto-start.** Wire the disabled "Start with Windows" checkbox via Task Scheduler ("Run with highest privileges"). Required because Interception needs admin; a plain Run-key entry would silently fail at logon.
- **Consider migrating existing `StartMinimized=true` installs.** If the new visible-by-default UX proves clearly better, a future schema bump (v1 → v2) could force `StartMinimized=false` on upgrade. Deferred — current users who liked tray-only behavior would dislike the surprise change.
