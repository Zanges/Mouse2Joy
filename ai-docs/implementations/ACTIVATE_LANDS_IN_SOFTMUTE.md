# Activate lands in SoftMute + UI Deactivate button

## Context

Two related issues with the engine's enable/disable lifecycle as exposed to the UI:

1. **Clicking Activate could lock the user out of their mouse.** The Activate button jumped straight into `EngineMode.Active`. On a profile with a mouse-axis binding the cursor froze instantly — and if the user hadn't yet configured a soft-toggle hotkey (or didn't remember the panic key Ctrl+Shift+F12), they were stuck. The Activate button needed a safer landing zone.
2. **No UI affordance to fully deactivate.** `Off` was reachable only via the hard toggle hotkey, the tray menu, or the panic key. A user who never configured hotkeys had no in-window way to cleanly disconnect the virtual pad.

Outcome: **Activate now lands in `SoftMuted`** (pad connected, input passes through) — the user engages emulation by hitting the soft toggle hotkey or the tray "Soft mute" item. A new **Deactivate** button at the right edge of the status bar drops the engine all the way to `Off` from any state. Startup behavior is unchanged: `Off` (capture on for hotkeys, no suppression, pad disconnected).

## What changed

- Added `InputEngine.EnterSoftMute()` — connects the virtual pad, sets suppression mode to `PassThrough`, lands in `EngineMode.SoftMuted`. Idempotent. There was no public path into SoftMuted from `Off` before; `RequestToggle(Soft)` is a no-op from `Off` and `EnableEmulation` jumps straight to Active.
- Renamed App-side `EnsureEngineRunning` → `EnsureEngineArmed`; switched its body from `EnableEmulation()` to `EnterSoftMute()`. This is the helper that the Activate button's `ApplyActiveProfile` action funnels through.
- Added a **Deactivate** button to the bottom status bar, right-aligned. Bound to a new `MainViewModel.DeactivateCommand` that calls `AppServices.DeactivateEngine` (which calls `InputEngine.DisableEmulation`).
- Updated the Activate button tooltip on the Profiles tab to explain that it arms the profile in soft-mute, not Active.

`EnableEmulation`, `DisableEmulation`, `RequestToggle`, the Hard hotkey, the panic hotkey, and the tray menu items are all unchanged.

## Key decisions

- **Activate lands in SoftMuted, not Active.** This is the safe-by-default landing zone for a user who has just clicked a button. The pad is connected and the engine is armed; no input is being suppressed yet. To engage emulation the user hits the soft toggle hotkey, opens the tray menu's "Soft mute (toggle)" item, or rebinds those if they want a different gesture. Going straight to Active stranded users who hadn't configured a toggle hotkey on profiles that bind mouse movement — a one-way trip into a frozen cursor. The Hard and Soft toggle hotkeys can still go `Off → Active` directly; those are deliberate engage gestures, not a button-click.
- **No UI button to enter Active mode.** It would be redundant: Activate already arms the engine, and the soft toggle (hotkey or tray) flips into Active. A second "Engage" UI button would be one more thing to maintain and would re-introduce the lockout footgun. The Hard or Soft hotkey covers users who want a one-press `Off → Active`.
- **Deactivate button placement: status bar, right edge.** The status bar already shows the live mode/profile state, so a global engine action at the right edge of the same bar is the natural pairing. Putting it next to Activate on the Profiles tab would have wrongly suggested it's profile-scoped — Deactivate works on the engine regardless of which profile is selected in the list.
- **No "auto-engage on activate" preference (yet).** Power users who want a profile to go straight into Active might want to opt out of the soft-mute landing. Deferred — adding a setting for it is easy if the demand shows up, but starting safe is the right default. Users who want one-press engage can configure the Hard toggle hotkey.

## Files touched

- [src/Mouse2Joy.Engine/InputEngine.cs](../../src/Mouse2Joy.Engine/InputEngine.cs) — new `EnterSoftMute()` method.
- [src/Mouse2Joy.App/App.xaml.cs](../../src/Mouse2Joy.App/App.xaml.cs) — renamed `EnsureEngineRunning` → `EnsureEngineArmed`, switched it to `EnterSoftMute()`. Updated both `AppServices` construction sites (`OnStartup` and `ShowMain`'s rehydrate path) to wire `RefreshActiveProfile` and `DeactivateEngine` alongside the existing `ApplyActiveProfile`.
- [src/Mouse2Joy.UI/ViewModels/AppServices.cs](../../src/Mouse2Joy.UI/ViewModels/AppServices.cs) — added `RefreshActiveProfile` and `DeactivateEngine` actions alongside `ApplyActiveProfile`. The three exist as separate fields so call sites can't accidentally use the wrong one.
- [src/Mouse2Joy.UI/ViewModels/MainViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/MainViewModel.cs) — added `DeactivateCommand`.
- [src/Mouse2Joy.UI/Views/MainWindow.xaml](../../src/Mouse2Joy.UI/Views/MainWindow.xaml) — Deactivate button in the status bar (right-aligned via a `DockPanel` ItemsPanel + `DockPanel.Dock="Right"`); Activate-button tooltip rewritten.

Deliberately unchanged:

- `RequestToggle`, the Hard hotkey, the panic hotkey, and the tray "Enable / disable" / "Soft mute" items. They already had the correct semantics — only the in-UI Activate button was unsafe-by-default.
- App startup mode (`Off`). Capture starts immediately so hotkeys are live, the virtual pad is disconnected, no suppression — what the user calls "soft off" is exactly today's `Off` mode.

## Follow-ups

- A "remember last engine mode and restore on startup" preference could come later, but right now `LastProfileName` is loaded as the active profile reference without flipping the engine on, which is the right safety default.
- An optional "Activate to Active directly" preference for power users (see Key decisions). Trivially small if needed.
