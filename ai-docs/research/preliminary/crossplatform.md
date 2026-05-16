# Cross-platform port — preliminary feasibility

## Context

Mouse2Joy is today a Windows-only .NET 8 / WPF app (`net8.0-windows`, x64).
This is a *preliminary* feasibility pass: can it run on Linux, and is macOS
even possible? Goal is a go/no-go-shaped picture and a rough sense of effort
and risk, not a port plan. No code has been changed.

Verdict up front:

- **Linux: feasible, medium-large effort.** Every Windows dependency has a
  credible native equivalent. The biggest cost is the UI (WPF → Avalonia),
  not the input/output plumbing.
- **macOS: technically possible, but with a hard functional limitation.**
  There is no clean, unprivileged way to present a system-wide virtual Xbox
  controller that games actually consume. The output side — the whole point
  of the app — is the blocker, not the UI.

## What's already in our favor

The codebase is better positioned than a typical WPF app:

- **Clean seams already exist.** The engine talks to the outside world
  through two interfaces: `IVirtualPad` (output) and `IInputBackend`
  (capture). Both are tiny and OS-agnostic. A Linux backend is a new
  implementation of these, not a rewrite of the engine.
  - [src/Mouse2Joy.Engine/IVirtualPad.cs](src/Mouse2Joy.Engine/IVirtualPad.cs)
  - [src/Mouse2Joy.Engine/IInputBackend.cs](src/Mouse2Joy.Engine/IInputBackend.cs)
- **Keys are stored as hardware scancodes, not Windows VK enums.**
  `VirtualKey(ushort Scancode, bool Extended)` in
  [src/Mouse2Joy.Persistence/Models/Enums.cs](src/Mouse2Joy.Persistence/Models/Enums.cs).
  Scancodes map cleanly to Linux evdev key codes (and to macOS HID usages)
  with a translation table — no fragile Windows-keymap leakage into the
  persisted profile format. This is a big deal: **profiles stay portable.**
- **Persistence is already cross-platform.**
  [Mouse2Joy.Persistence.csproj](src/Mouse2Joy.Persistence/Mouse2Joy.Persistence.csproj)
  targets plain `net8.0`. POCOs + JSON, no Windows API. The `%APPDATA%`
  path logic (`AppPaths`) is the only thing to generalize (→ XDG on Linux,
  `~/Library/Application Support` on macOS).
- **Engine is logic-only except one timer.** The only Windows P/Invoke in
  the engine is the high-resolution waitable timer in
  [src/Mouse2Joy.Engine/Threading/WaitableTickTimer.cs](src/Mouse2Joy.Engine/Threading/WaitableTickTimer.cs)
  (`kernel32` `CreateWaitableTimerEx`). Easily abstracted behind an
  `ITickTimer` with a platform-specific implementation; Linux/macOS can use
  `timerfd` / a high-res sleep loop. The engine TFM (`net8.0-windows`) is
  almost certainly a cosmetic over-restriction here, not a real dependency.

So the portable core (Persistence + Engine + mapping pipeline + curve/stick
math) is roughly the *majority* of the interesting code and needs little to
no change. The port cost concentrates in three Windows-bound modules.

## The three Windows-bound modules

### 1. Output — `Mouse2Joy.VirtualPad` (the macOS blocker)

Today: `Nefarius.ViGEm.Client` over the **ViGEmBus** kernel driver,
presenting an Xbox 360 / XInput pad. See
[ViGEmVirtualPad.cs](src/Mouse2Joy.VirtualPad/ViGEmVirtualPad.cs).

- **Linux:** Replace with **`uinput`** (`/dev/uinput`). The kernel can
  create a virtual input device that exposes an Xbox-style gamepad via
  evdev; SDL/Proton/most native Linux games consume this exactly as a real
  controller. Mature, well-trodden path (this is how `xboxdrv`,
  `evsieve`-style tools, Steam Input, and many emulator helpers work).
  Needs the user in the `input` group or a udev rule — comparable in
  spirit to today's "install the driver" step, but lighter. **No new kernel
  driver to ship.** This is the single most encouraging finding for Linux.
- **macOS:** This is the hard part. macOS has **no supported public API to
  publish a system-wide virtual HID gamepad** that arbitrary games pick up.
  Options all have serious caveats:
  - A **DriverKit/`IOKit` virtual-HID driver** (à la the Karabiner
    VirtualHIDDevice). Technically the right answer, but means writing,
    notarizing, and distributing a system extension the user must approve;
    Gatekeeper/SIP friction; Apple-developer-account territory. Large effort,
    ongoing maintenance against macOS releases.
  - Game-controller frameworks (`GameController.framework`) are
    *consumption* APIs, not a way to *publish* a synthetic controller.
  - Net: the app's core value prop (a virtual pad games actually see) is
    **not cleanly achievable on macOS** without shipping a notarized kernel
    extension. Treat macOS output as a research spike, not a checkbox.

### 2. Input capture — `Mouse2Joy.Input`

Today, two distinct mechanisms (the split is load-bearing — see
INITIALWORK.md "Critical insight"):

- Mouse → **Interception** kernel driver (P/Invoke over vendored
  `interception.dll`),
  [InterceptionNative.cs](src/Mouse2Joy.Input/Native/InterceptionNative.cs).
- Keyboard → **`WH_KEYBOARD_LL`** user-mode hook,
  [LowLevelKeyboardBackend.cs](src/Mouse2Joy.Input/LowLevelKeyboardBackend.cs).
  Chosen so synthetic keystrokes (voice input, OSK) are still seen.

The reason for the split is a Windows-specific kernel-stack detail. On other
OSes the calculus changes:

- **Linux:** Read devices via **evdev** (`/dev/input/event*`) and, crucially,
  use an **`EVIOCGRAB`** exclusive grab to *suppress* the real input from
  reaching other apps (this is the equivalent of Interception's "swallow").
  `libevdev` + `uinput` is the standard capture-grab-and-re-emit pattern.
  One mechanism covers both mouse and keyboard, so the Windows two-backend
  split likely **collapses to one evdev backend** on Linux. The "synthetic
  keystroke" concern is Windows-kernel-stack-specific and largely doesn't
  transfer. Requires read access to `/dev/input/*` (input group / udev /
  often root) — again, a permissions step rather than a driver install.
  Wayland vs X11 does **not** matter here because evdev is below the display
  server; that's an advantage over doing this at the X/Wayland layer.
- **macOS:** **`CGEventTap`** at the HID/session level can observe *and*
  suppress mouse + keyboard system-wide, including for games (with caveats
  for some full-screen/anti-cheat titles). Requires the user to grant
  **Accessibility** (and possibly Input Monitoring) permission in System
  Settings — a one-time consent dialog. Capture on macOS is the *tractable*
  half; it's the output side that isn't.

### 3. UI + host — `Mouse2Joy.UI` and `Mouse2Joy.App`

This is the largest *raw* chunk of porting work (8 XAML files + view models
+ overlay + tray + interop), all WPF, which is Windows-only.

- **Framework:** **Avalonia UI** is the realistic target. It's the closest
  XAML/MVVM analogue to WPF, runs on Linux + macOS + Windows, and the
  existing `CommunityToolkit.Mvvm` view models largely carry over. This is
  still a substantial re-author of every `.xaml` and the code-behind, plus
  the custom controls (`CurveEditorCanvas`, `KeyCaptureBox`,
  `ChainPreviewControl`, `NumericUpDown`).
- **Overlay window** ([OverlayWindow](src/Mouse2Joy.UI/Views/OverlayWindow.xaml.cs),
  click-through via [WindowStyles.cs](src/Mouse2Joy.UI/Interop/WindowStyles.cs),
  multi-monitor via [MonitorInfo.cs](src/Mouse2Joy.UI/Interop/MonitorInfo.cs)):
  click-through transparent always-on-top windows are doable in Avalonia but
  the per-OS behavior (especially click-through + global positioning over a
  full-screen game) needs validation per platform. Medium risk.
- **Tray icon** (`Hardcodet.NotifyIcon.Wpf`): WPF-only package. Avalonia has
  its own tray API; Linux tray support is desktop-environment-dependent
  (StatusNotifierItem / AppIndicator) and historically finicky.
- **Panic hotkey** ([PanicHotkey.cs](src/Mouse2Joy.App/PanicHotkey.cs),
  Win32 `RegisterHotKey` on a message-only window): needs a per-OS global
  hotkey implementation (Linux: X11/`evdev`-level since we already grab
  input; macOS: `CGEventTap` / Carbon hotkey). Conceptually small but must
  stay independent of the engine, as it is today.
- **Single-instance guard** ([SingleInstanceGuard.cs](src/Mouse2Joy.App/SingleInstanceGuard.cs)):
  trivial to re-do with a lock file / named socket.
- **Elevation/manifest** ([app.manifest](src/Mouse2Joy.App/app.manifest)):
  replaced by the Linux permissions model (groups/udev) and macOS
  consent dialogs — different shape, not necessarily harder for the user.

## Rough effort & risk shape (preliminary, not estimates)

| Area | Linux | macOS |
| --- | --- | --- |
| Persistence / paths | Trivial (XDG) | Trivial |
| Engine + tick timer abstraction | Small | Small |
| Input capture + suppression | Medium (evdev/uinput, well-trodden) | Medium (`CGEventTap`, consent) |
| Virtual gamepad output | **Medium, low-risk** (`uinput`) | **High / possibly blocking** (needs notarized virtual-HID kext) |
| UI (WPF → Avalonia) | Large | Large (shared with Linux) |
| Tray / overlay / global hotkey | Medium (DE-dependent) | Medium |
| Permissions/onboarding UX | Medium | Medium |

The dominant cost for **Linux** is the UI rewrite, which is shared with any
future macOS port. The dominant *risk* for **macOS** is output, which no
amount of UI work solves.

## Decisions (settled with the user 2026-05-16)

These were open questions in the preliminary pass; the user has now decided.
Recorded here so a future port plan starts from settled ground.

1. **Sequencing: Linux-first, macOS-aware design.** Implement Linux only.
   Shape the platform interfaces so a macOS implementation *could* slot in
   later, but do **not** promise a macOS system-wide virtual pad until a
   dedicated DriverKit spike proves it viable. Rationale: the macOS output
   blocker is a different class of work (notarized system extension); don't
   let it gate or distort the Linux effort, but don't paint macOS into a
   corner either.
2. **Single codebase with platform implementations.** Keep
   Persistence/Engine shared; introduce per-OS implementations behind
   interfaces selected at startup (`IVirtualPad`, `IInputBackend`, plus new
   `ITickTimer`, `ITrayIcon`, `IGlobalHotkey`, and — see #4 — `IOverlay`).
   No fork. Rationale: the shared core is the majority of the interesting
   code and where bugs live; authoring it once outweighs the CI-matrix cost.
3. **Linux scope (first cut): native + Proton/Steam, X11 and Wayland.**
   evdev sits below the display server so X11/Wayland are equivalent. Ship
   a udev rule for `/dev/uinput` + `/dev/input/*` access. **Explicitly
   deferred to follow-ups, not v1:** anti-cheat full-screen titles, and
   packaging formats (Flatpak/AppImage/deb).
4. **UI: unify general UI on Avalonia for all OSes; overlay is gated.**
   The general/main UI moves to Avalonia on Windows + Linux (one codebase,
   no parallel WPF+Avalonia for the main app). The **overlay is treated
   separately** because the click-through, always-on-top, transparent,
   correctly-positioned-over-a-full-screen-game behavior is the riskiest
   piece to reproduce. Decision rule:
   - First, a **gating spike**: prove whether Avalonia can match today's
     WPF overlay behavior over a full-screen game *on Windows* (current
     impl: [WindowStyles.cs](src/Mouse2Joy.UI/Interop/WindowStyles.cs) +
     [MonitorInfo.cs](src/Mouse2Joy.UI/Interop/MonitorInfo.cs)).
   - **If the spike succeeds:** migrate the overlay to Avalonia too —
     full unification, no WPF left.
   - **If it fails:** keep the existing **WPF overlay on Windows**, and
     write a **separate native overlay for Linux** — both behind an
     `IOverlay` abstraction so the rest of the (Avalonia) UI is agnostic.
   Either branch requires the overlay to sit behind `IOverlay` from the
   start; that is not optional and should land before the spike's outcome
   is known.

## Files / boundaries that matter for a port (reference)

- Portable as-is: everything in `Mouse2Joy.Persistence`, the mapping/curve/
  stick logic in `Mouse2Joy.Engine` (`Mapping/`, `Modifiers/`, stick
  processors, `RawEvent`, `HotkeyMatcher`).
- Needs an abstraction extracted:
  [WaitableTickTimer.cs](src/Mouse2Joy.Engine/Threading/WaitableTickTimer.cs)
  (only Windows P/Invoke in the engine).
- Needs a per-OS implementation behind the existing interface:
  [ViGEmVirtualPad.cs](src/Mouse2Joy.VirtualPad/ViGEmVirtualPad.cs),
  [InterceptionInputBackend / LowLevelKeyboardBackend / CompositeInputBackend](src/Mouse2Joy.Input/).
- Full re-author: all of `Mouse2Joy.UI` (8 `.xaml` + code-behind +
  `Interop/`) and the host wiring in `Mouse2Joy.App`.

## Follow-ups

Still preliminary — no spike code written. Two de-risking spikes now gate
the real plan, in priority order:

1. **Non-UI path spike (Linux):** throwaway evdev-grab → engine → `uinput`
   for a single binding. De-risks the core platform abstraction before the
   larger UI rewrite. Lowest-risk per the analysis, but proves the seam.
2. **Overlay gating spike (Windows, per Decision #4):** Avalonia
   click-through / always-on-top / transparent overlay over a full-screen
   game on Windows. Its outcome forks the overlay plan (full Avalonia
   migration vs. retained WPF overlay + separate Linux overlay behind
   `IOverlay`). Run before committing the overlay direction.

Deferred / not v1 (per Decision #3): anti-cheat full-screen titles, Linux
packaging formats (Flatpak/AppImage/deb).

macOS virtual-HID output needs its own dedicated research spike (DriverKit
feasibility, notarization burden) before macOS is ever promised — not
scheduled (per Decision #1, macOS is design-aware only for now).
