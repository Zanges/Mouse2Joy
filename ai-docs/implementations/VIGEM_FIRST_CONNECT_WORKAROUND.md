# ViGEm first-connect workaround

## Context

On the very first profile activation after launching Mouse2Joy, users hit a
spurious error dialog reading:

> Failed to enable emulation: The operation completed successfully.
> Make sure Interception and ViGEmBus are installed and the app runs as administrator.

The status would drop to `Off`. Clicking **Activate** a second time worked
normally. ViGEmBus and Interception were both installed correctly; the message
was misleading.

The text "The operation completed successfully" is the system-default message
for Win32 `ERROR_SUCCESS` (HResult 0). An exception with `HResult == 0` was
being constructed inside `Nefarius.ViGEm.Client` v1.21.256 during the very
first connect of a process and surfaced through
[ViGEmVirtualPad.Connect()](../../src/Mouse2Joy.VirtualPad/ViGEmVirtualPad.cs)
→ [InputEngine.EnableEmulation()](../../src/Mouse2Joy.Engine/InputEngine.cs)
→ [App.EnsureEngineRunning()](../../src/Mouse2Joy.App/App.xaml.cs).

The bug was deterministic: first call per process failed, all subsequent
calls succeeded — a classic first-init quirk in the third-party wrapper.

## What changed

- Added `ViGEmVirtualPad.Prewarm()` that constructs the underlying
  `ViGEmClient` once at app startup. This pays the COM/IOCTL first-init cost
  before any user interaction, so the spurious exception (when it fires)
  happens off the user's path and is logged silently.
- Added a single-shot retry inside `ViGEmVirtualPad.Connect()`. The retry
  fires only on the first connect attempt of the process, only when the
  caught exception's `HResult` is 0 *or* its message matches the
  ERROR_SUCCESS text. Anything else propagates unchanged.
- `App.OnStartup` calls `_pad.Prewarm()` immediately after constructing
  `ViGEmVirtualPad`, wrapped in its own try/log so a missing bus driver
  cannot prevent the UI from coming up.
- `App.EnsureEngineRunning` now also logs the failure at Error level before
  showing the `MessageBox` (the engine layer was already logging, but the App
  layer wasn't — useful when triaging from logs).

## Key decisions

- **Pre-warm + targeted retry, not a blanket catch.** The error message that
  caused the bug is impossible to distinguish from a real failure by text
  alone — except the `HResult` field is `0`, which a *real* failure can never
  have. The retry filter (`HResult == 0` or message match) is precise enough
  that genuine "bus not installed" / "no admin" errors still surface as the
  same dialog they did before. We do not blanket-retry, and we do not
  blanket-suppress.
- **Workaround in our wrapper, not in the package.** Per the user's standing
  preference, we don't vendor or fork third-party source. The fix lives
  entirely in
  [ViGEmVirtualPad.cs](../../src/Mouse2Joy.VirtualPad/ViGEmVirtualPad.cs). If
  `Nefarius.ViGEm.Client` is upgraded, re-test on a clean machine — the retry
  may become unnecessary and can be removed.
- **`Prewarm()` is on the concrete class, not on `IVirtualPad`.** This is a
  Win32-/library-specific quirk; broadening the interface would force every
  test/mock implementation to care about it. App.xaml.cs already references
  the concrete `ViGEmVirtualPad` field, so no abstraction was lost.
- **Pre-warm tolerates failure.** A missing bus driver should not stop the UI
  from starting — the user needs to be able to see the Setup tab to learn what
  to install. This matches how `_engine.StartCapture()` is also wrapped at
  startup (it logs and continues if Interception is missing).
- **50 ms sleep before retry.** Not strictly necessary, but cheap insurance
  against any internal timing race in the library's first-init path. The
  retry only fires once per process, so the cost is paid at most once.

## Files touched

- [src/Mouse2Joy.VirtualPad/ViGEmVirtualPad.cs](../../src/Mouse2Joy.VirtualPad/ViGEmVirtualPad.cs) — added `Prewarm()`, extracted `ConnectInternal()`, added the gated retry in `Connect()`, added `IsSpuriousSuccessException()` predicate.
- [src/Mouse2Joy.App/App.xaml.cs](../../src/Mouse2Joy.App/App.xaml.cs) — call `_pad.Prewarm()` right after the pad is constructed in `OnStartup`; added Error-level log in `EnsureEngineRunning`.

Deliberately unchanged:

- [src/Mouse2Joy.Engine/InputEngine.cs](../../src/Mouse2Joy.Engine/InputEngine.cs) — `EnableEmulation` already logs and rethrows correctly. The retry belongs in the wrapper layer (where the library-specific quirk is known), not in the engine.
- `IVirtualPad` interface — `Prewarm` was deliberately *not* added to it (see Key decisions).

## Follow-ups

- If `Nefarius.ViGEm.Client` is bumped past v1.21.256, re-test the
  first-activation path on a clean Windows install. If the package fixes the
  underlying bug, the retry block in `Connect()` can be removed (pre-warm is
  cheap enough to keep regardless).
- The Setup tab's `ViGEmHealth.Probe()` ([ViGEmHealth.cs](../../src/Mouse2Joy.VirtualPad/ViGEmHealth.cs))
  also constructs and disposes a `ViGEmClient`. If the user's first
  interaction is opening the Setup tab (which probes), pre-warm becomes
  redundant — but harmless, since `Prewarm()` short-circuits when `_client`
  is already set. No change needed.
