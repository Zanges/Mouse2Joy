# Soft toggle hotkey activates from Off

## Context

The soft toggle hotkey was historically `Active ↔ SoftMuted` only. From `Off` it was a no-op, with the rationale "nothing to mute yet — user has to first Activate a profile".

That left a usability hole: a user who configured only a soft toggle hotkey (skipping the Hard one) had no way to engage emulation from a fresh start except by opening the main window and clicking Activate, then hitting the soft hotkey to flip from SoftMuted into Active. Two manual steps just to get going.

Outcome: the soft hotkey now also handles `Off → Active`, mirroring what Hard already did. A single hotkey now drives the full lifecycle for users who want it that way.

## What changed

[InputEngine.RequestToggle(ToggleAction.Soft)](../../src/Mouse2Joy.Engine/InputEngine.cs) now handles three transitions:

- `Off       → Active`   (initial activation; same code path as Hard from Off)
- `Active    → SoftMuted` (unchanged)
- `SoftMuted → Active`    (unchanged)

The `Off → Active` branch reuses the same guard as Hard: if no profile is set, the press is logged and ignored.

## Key decisions

- **Mirror the Hard-from-Off guard.** Both Hard and Soft now refuse to engage when `_activeProfile.Name` is empty. Without an active profile there are no bindings to apply, so suppression mode would be meaningless.
- **No new "Soft from Off lands in SoftMuted" sub-behavior.** It would mean three transitions for Soft (`Off → SoftMuted`, `Active → SoftMuted`, `SoftMuted → Active`) and force the user to press the hotkey twice for first-time engage. That defeats the point of letting Soft drive the whole lifecycle. If a user wants the safe arming behavior, the Activate button already provides it.
- **Hard remains the "panic-style engage / disengage" hotkey.** Hard's `Off ↔ Active` behavior is unchanged. Soft now overlaps with it on the `Off → Active` edge but keeps its mute semantics for the other two — so users who set up both hotkeys still get distinct gestures (Hard for full disengage, Soft for mid-session mute).

## Files touched

- [src/Mouse2Joy.Engine/InputEngine.cs](../../src/Mouse2Joy.Engine/InputEngine.cs) — extended `RequestToggle(Soft)` with the `Off → Active` branch and matching no-profile guard.
- [ai-docs/implementations/ACTIVATE_LANDS_IN_SOFTMUTE.md](ACTIVATE_LANDS_IN_SOFTMUTE.md) — small note that Soft (in addition to Hard) can engage from Off.

## Follow-ups

None — the change is self-contained.
