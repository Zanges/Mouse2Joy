# Mouse2Joy — initial work

A short, pragmatic onboarding doc capturing the state of the project after the
v1 implementation session (2026-05-09). Treat this as a checkpoint, not a
finished spec.

## What it is

A Windows desktop app that emulates an Xbox 360 / XInput gamepad and lets the
user map mouse motion, mouse buttons, scroll, and keyboard keys to virtual
gamepad outputs (sticks, triggers, buttons, d-pad). Use case: playing
controller-only games with mouse + keyboard. Includes per-binding curves
(sensitivity, deadzones, exponent) and three mouse-to-stick algorithms
(velocity with decay; accumulator with spring; persistent with no
auto-recenter). See [STICK_MODES_UPDATE.md](STICK_MODES_UPDATE.md) for the
persistent mode rationale.

## Status

v1 is functional end-to-end. Verified manually: virtual pad enumerates in
`joy.cpl`, mouse motion drives left stick, soft/hard toggle hotkeys flip mode
correctly, panic hotkey forces engine off. Mouse-to-stick has minor tuning
issues to be addressed in a future session (TBD).

All unit tests pass (`dotnet test`).

## Solution layout

```
Mouse2Joy.sln
  src/
    Mouse2Joy.Persistence/   POCOs + JSON store. AppPaths under %APPDATA%\Mouse2Joy.
    Mouse2Joy.Engine/        Tick loop, mapping pipeline, IVirtualPad/IInputBackend
                             interfaces, hotkey matcher + modifier tracker, stick
                             processors, curve evaluator, state snapshot, panic hook.
    Mouse2Joy.VirtualPad/    ViGEmVirtualPad over Nefarius.ViGEm.Client (Xbox 360)
                             + ViGEmHealth probe.
    Mouse2Joy.Input/         Keyboard + mouse capture:
                              - InterceptionNative (P/Invoke over interception.dll)
                              - InterceptionInputBackend (mouse only)
                              - LowLevelKeyboardBackend (WH_KEYBOARD_LL hook)
                              - CompositeInputBackend (multiplexes the two)
                              - DriverHealth probe.
    Mouse2Joy.UI/            WPF: MainWindow, OverlayWindow, BindingEditor,
                             curve editor control, key-capture textbox,
                             overlay widgets, click-through P/Invoke.
    Mouse2Joy.App/           Host: App.xaml, tray icon, single-instance guard,
                             panic hotkey, manifest, native/x64/interception.dll,
                             Serilog file logging.
  tests/
    Mouse2Joy.Engine.Tests/      36 tests (curves, stick processors, hotkey
                                  matcher, binding resolver).
    Mouse2Joy.Persistence.Tests/ 3 tests (polymorphic JSON roundtrip,
                                  schema versioning).
    Mouse2Joy.VirtualPad.Tests/  Placeholder; integration is hands-on.
```

## Tech stack

- .NET 8 (`net8.0-windows`), WPF, central NuGet package management.
- `Nefarius.ViGEm.Client` 1.21.256 for the virtual pad.
- Direct P/Invoke over `interception.dll` for Interception (we deliberately
  did NOT use the `InputInterceptor` NuGet wrapper — its hook loop
  unconditionally forwards strokes after the callback, which would prevent
  clean swallowing).
- `CommunityToolkit.Mvvm`, `Hardcodet.NotifyIcon.Wpf`, `Serilog` (file sink),
  `xunit` + `FluentAssertions` for tests.

## Native dependencies

- **ViGEmBus** kernel driver (https://github.com/nefarius/ViGEmBus) — virtual
  pad. User installs once, no reboot needed.
- **Interception** kernel driver (https://github.com/oblitum/Interception) —
  mouse capture (the kernel-stack reasoning is in the "Critical insight"
  section below). User installs `install-interception.exe /install` from an
  elevated cmd, then reboots.
- **`interception.dll` (user-mode library)** is bundled at
  `src/Mouse2Joy.App/native/x64/interception.dll`. Repo includes it (LGPL
  redistribution allowed for non-commercial use; see `THIRD_PARTY_NOTICES.md`).
  `interception.dll.sha256` pins the canonical hash for verification.

The Setup tab inside the app probes all three pieces (ViGEmBus, Interception
driver, admin rights) and shows actionable hints for whichever is missing.

## Critical insight on input capture

Interception's kernel-mode keyboard filter only sees hardware keystrokes
arriving through the kernel keyboard class driver stack. Synthetic keystrokes
injected via Win32 `SendInput` / `keybd_event` (voice-to-keyboard tools,
on-screen keyboards, accessibility software, in-game scripted keystrokes)
**bypass that path entirely** — they appear at the user-mode message pump
level only.

To accommodate users whose primary input is synthetic, Mouse2Joy splits
capture by device class:

- **Mouse** → `InterceptionInputBackend` (kernel-level, full swallow control).
- **Keyboard** → `LowLevelKeyboardBackend` using `WH_KEYBOARD_LL` (sees both
  hardware and synthetic keystrokes).

These are multiplexed by `CompositeInputBackend`, so the engine sees a single
`IInputBackend`. The hotkey path runs in the engine after this multiplexing,
so hotkeys work uniformly for both physical and synthetic keystrokes.

## Architecture highlights

- **Engine lifecycle:** `StartCapture()` runs at app launch and stays running
  for the app's whole lifetime. `EnableEmulation()` / `DisableEmulation()`
  toggle the virtual pad + binding suppression. Three modes:
  `Off` (capture on, pad disconnected), `Active` (full emulation),
  `SoftMuted` (pad connected and idle, real input passes through). The
  always-on capture is what lets toggle hotkeys work as a safety net even
  before the user activates a profile.
- **Tick loop** runs at the profile's `TickRateHz` (default 250). Hotkey
  detection runs in the capture thread (synchronous with `OnRawEvent`), so
  toggles are independent of the tick rate.
- **Snapshot pump** for the overlay: engine writes an immutable
  `EngineStateSnapshot` per tick via `Volatile.Write`; the overlay reads at
  60 Hz on a `DispatcherTimer`. One small allocation per tick — gen0,
  inconsequential.
- **Panic hotkey** (`Ctrl+Shift+F12`, fixed) is registered through Win32
  `RegisterHotKey` on a hidden message-only window in `App`. Independent of
  Interception and the engine — fires even if the engine has crashed.
- **Per-binding `SuppressInput` flag.** When true, the matching real input
  is swallowed (mouse cursor doesn't move; key doesn't reach the focused
  app). The binding editor defaults this to `true` for mouse-axis bindings
  (otherwise the cursor fights the stick) and `false` for everything else.

## Build and run

```powershell
# From repo root, in an elevated PowerShell (admin needed for Interception capture):
dotnet build Mouse2Joy.sln
# Run the host project (the WPF exe):
dotnet run --project src/Mouse2Joy.App
# Or, after a Release publish:
dotnet publish src/Mouse2Joy.App -c Release
.\src\Mouse2Joy.App\bin\Release\net8.0-windows\win-x64\publish\Mouse2Joy.exe
```

The app must run as Administrator for Interception to attach. Without admin,
the UI still opens and the Setup tab tells the user what's missing.

`dotnet test Mouse2Joy.sln` runs all unit tests.

## Where things live

| Need to … | File |
| --- | --- |
| Change the curve formula | `src/Mouse2Joy.Engine/Mapping/CurveEvaluator.cs` |
| Change a stick model's behavior | `src/Mouse2Joy.Engine/StickModels/{Velocity,Accumulator,Persistent}StickProcessor.cs` |
| Add a new gamepad output target | `src/Mouse2Joy.Persistence/Models/OutputTarget.cs` + `Engine/Mapping/{BindingResolver,ReportBuilder}.cs` |
| Add a new input source kind | `src/Mouse2Joy.Persistence/Models/InputSource.cs` + `Engine/Mapping/BindingResolver.cs` + `UI/Views/BindingEditorWindow.xaml(.cs)` |
| Tweak the keyboard hook | `src/Mouse2Joy.Input/LowLevelKeyboardBackend.cs` |
| Change Interception P/Invoke | `src/Mouse2Joy.Input/Native/InterceptionNative.cs` |
| Change ViGEm interaction | `src/Mouse2Joy.VirtualPad/ViGEmVirtualPad.cs` |
| Add an overlay widget | `src/Mouse2Joy.UI/Overlay/Widgets/` + register in `Overlay/OverlayWidgetHost.cs` |
| Tray menu / single-instance / panic hotkey | `src/Mouse2Joy.App/{App.xaml.cs,PanicHotkey.cs,SingleInstanceGuard.cs}` |

## Storage

Profiles + settings live in `%APPDATA%\Mouse2Joy\`:

- `profiles\<sanitized-name>.json` — one file per profile. Display name lives
  inside the JSON; rename = move-then-rewrite (atomic).
- `settings.json` — hotkeys, last active profile, overlay layout.
- `logs\mouse2joy-YYYYMMDD.log` — Serilog rolling daily, 7-day retention.

Schema is versioned (`schemaVersion: 1` on every top-level document) for
future migrations.

## Out of scope for v1

Explicitly deferred:
- Spline curves (current model: sensitivity + deadzone + saturation + exponent).
- Per-app auto-profile-switching by foreground exe.
- DLL-injection overlay for exclusive-fullscreen DirectX games (current
  overlay covers borderless-windowed, which is the modern norm).
- Force feedback / rumble feedback to mouse haptics.
- Anti-cheat-trippy techniques.

## Plan reference

The original implementation plan (decisions made interactively) lives at
`C:\Users\zange\.claude\plans\this-is-a-completely-synthetic-brooks.md` if
you need to reread the rationale for specific choices. Several decisions
evolved during implementation (collapsing `Disabled`+`HardOff` modes into
`Off`, splitting keyboard capture from Interception). This doc reflects the
current state, not the original plan.
