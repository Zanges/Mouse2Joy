# Mouse2Joy

> Emulate an Xbox 360 controller with mouse + keyboard on Windows.

Mouse2Joy is a Windows desktop app that exposes a virtual Xbox 360 / XInput
gamepad and lets you map mouse motion, mouse buttons, scroll, and keyboard
keys to its sticks, triggers, buttons, and d-pad. Use case: playing
controller-only games (or controller-preferred mechanics) with a mouse and
keyboard.

## What it does

- **Mouse → sticks** with three switchable algorithms: velocity-with-decay,
  accumulator-with-spring, and persistent (no auto-recenter).
- **Per-binding curves**: deadzone, sensitivity, exponent / shaping.
- **Mouse buttons, scroll wheel, and keyboard keys → buttons / triggers /
  d-pad** (any input to any output).
- **Profiles** with hotkey-driven activation and a configurable global
  toggle.
- **Panic hotkey** (`Ctrl+Shift+F12`, always-on, registered independently of
  the engine) — guarantees you can stop emulation even if the engine is
  wedged.
- **Overlay HUD** showing live stick / trigger / button state.

## Status

v1 functional end-to-end. Solo project. .NET 8 / WPF, Windows x64 only.

## Requirements

- Windows 10 or 11, x64
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
  (only for end users running a published build — building from source uses
  the SDK)
- [ViGEmBus driver](https://github.com/nefarius/ViGEmBus/releases) (free,
  MIT) — provides the virtual gamepad
- [Interception driver](http://www.oblita.com/interception) (free, LGPL,
  non-commercial use) — provides kernel-level mouse capture
- The app **must run as Administrator** (Interception requires it to attach)

See [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) for full licensing of
bundled and linked components.

## Install (end users)

1. **Install ViGEmBus**: download the latest `ViGEmBus_*.msi` from the
   [releases page](https://github.com/nefarius/ViGEmBus/releases) and run
   it. No reboot needed.
2. **Install Interception**: download from
   [oblita.com/interception](http://www.oblita.com/interception), extract,
   then from an **elevated** command prompt run:
   ```
   install-interception.exe /install
   ```
   **Reboot** after install — the kernel driver is not active until you
   reboot.
3. Download the latest Mouse2Joy release (or build from source — see below).
4. Run `Mouse2Joy.App.exe` **as Administrator**.
5. Open the **Setup** tab in-app — it confirms the drivers are detected and
   admin rights are present, and shows what's missing if not.

## Usage (quick start)

1. Open the **Profiles** tab and create or pick a profile.
2. Add bindings (input → virtual pad output) in the binding editor. Each
   binding has its own curve / shaping settings.
3. Toggle emulation on/off with the configurable hotkey, or panic-stop with
   `Ctrl+Shift+F12` at any time.
4. Optional: open the overlay window for a live HUD of stick / trigger /
   button state.

User data lives under `%APPDATA%\Mouse2Joy\`:

- `profiles\<name>.json` — one file per profile
- `settings.json` — app-level settings
- `logs\mouse2joy-YYYYMMDD.log` — Serilog rolling log, 7-day retention

## Building from source (developers)

### Prerequisites

- .NET SDK **8.0.303** (pinned in [`global.json`](global.json))
- Visual Studio 2022, JetBrains Rider, or VS Code with the C# Dev Kit
- Windows x64

### Commands

```powershell
# Build the whole solution
dotnet build Mouse2Joy.sln

# Run the app — must be in an elevated shell for Interception to attach
dotnet run --project src/Mouse2Joy.App

# Run all tests
dotnet test Mouse2Joy.sln

# Run a single test project / filter
dotnet test tests/Mouse2Joy.Engine.Tests
dotnet test --filter "FullyQualifiedName~CurveEvaluatorTests"

# Release publish
dotnet publish src/Mouse2Joy.App -c Release
```

The UI runs unprivileged for development — driver attach fails gracefully
and the **Setup** tab reports what's missing. This is useful when iterating
on UI without needing capture working.

## Architecture

Six libraries plus the host app, plus three test projects:

| Project | Role |
| --- | --- |
| `Mouse2Joy.Persistence` | Data POCOs + JSON store under `%APPDATA%\Mouse2Joy\`. |
| `Mouse2Joy.Engine` | Tick loop, mapping pipeline, hotkey matcher, stick processors, curve evaluator. |
| `Mouse2Joy.Input` | Mouse capture (Interception P/Invoke) + keyboard capture (`WH_KEYBOARD_LL`). |
| `Mouse2Joy.VirtualPad` | Xbox 360 virtual pad wrapper over `Nefarius.ViGEm.Client`. |
| `Mouse2Joy.UI` | WPF main window, overlay, binding editor, key-capture textbox. |
| `Mouse2Joy.App` | Host: tray icon, single-instance guard, panic hotkey, manifest, native DLLs, Serilog. |

Two kernel drivers, two distinct roles:

- **ViGEmBus** = output (virtual pad)
- **Interception** = mouse capture only

Keyboard capture is intentionally **user-mode** (`WH_KEYBOARD_LL`), not
Interception. This is load-bearing: synthetic keystrokes (voice input,
on-screen keyboard, accessibility tools) bypass kernel-level keyboard hooks
entirely, so the keyboard backend has to stay user-mode to see them.

For the full architecture write-up, including lifecycle, where things live,
and the rationale for each major design choice, see
[`ai-docs/implementations/INITIALWORK.md`](ai-docs/implementations/INITIALWORK.md).
Per-feature decision write-downs live alongside it in
[`ai-docs/implementations/`](ai-docs/implementations/).

## License

Mouse2Joy is licensed under the **GNU General Public License v3.0** — see
[`LICENSE`](LICENSE).

Copyright © 2026 Zanges (Dominik Peukert).
