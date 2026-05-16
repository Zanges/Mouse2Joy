# Cross-platform port — Plan 1 (decouple + de-risking spikes)

## Status & scope

**Plan 1** of a multi-plan effort. Deliberately **spike-first**: it details
the OS-decoupling refactor (Phase 1A/1B) and the two de-risking spikes
(Phase 2), and sketches everything past the spike gates only **coarsely**
(Phase 3+). **Plan 2** is written *after* Phase 2 resolves — spike outcomes
(esp. the overlay fork) materially change the post-spike shape.

Prerequisite reading — settled higher-level context, do not re-litigate:
[ai-docs/research/preliminary/crossplatform.md](../research/preliminary/crossplatform.md)
("Decisions", 2026-05-16): Linux-first / macOS design-aware only; single
codebase; Linux scope = native + Proton/Steam, X11 & Wayland; main UI →
Avalonia big-bang; overlay gated. Plan 1 **refines** that doc's Decision #2
structure (the port now layers through dedicated `Contracts` +
`Platform.Abstractions` assemblies — see below).

### Decisions baked into this plan (Q&A 2026-05-16)

- **Full OS-decoupling of the engine**, and a **separate
  `Mouse2Joy.Platform.Abstractions`** for the ports. Because the port DTOs
  move out of the engine (below), `Platform.Windows` ends up **not
  referencing `Mouse2Joy.Engine` at all** — the clean layering the earlier
  draft thought impossible is now real, *because* of the full decouple.
- **New `Mouse2Joy.Contracts`** holds primitives shared across the port
  boundary; both `Persistence` and `Platform.Abstractions` reference it.
- **`XInputReport`/`XInputButtons` → `GamepadReport`/`GamepadButtons`**;
  button values become engine-internal ordinals; the Windows ViGEm adapter
  does an *explicit* XInput mapping table (no blind cast).
- **Persisted key identity migrates to USB HID usage IDs**;
  `VirtualKey` → **`PhysicalKey`**. This is a versioned-schema migration on
  **two** documents.
- **General settings-migration pipeline** built (mirrors
  `ProfileStore`'s peek-and-chain) — `AppSettings` has a `SchemaVersion`
  field but no migration infra exists today; this is the first settings
  schema change ever.
- **Contracts extraction + key re-representation + both migrations = one
  atomic Phase 1A unit**, before the broader platform split.
- **TFM retarget + Windows-code relocation = one atomic change** (CA1416
  would otherwise red `main` mid-refactor).
- **Supervisor/watchdog boundary is designed in Plan 1** so Plan 2's Linux
  panic-key + capture-grab ownership isn't boxed in.
- **Generic `ISetupProbe` seam extracted in Phase 1B** (Windows content
  now; Linux content Plan 2).
- **Single-instance contract = silent-exit** (matches today; focus-existing
  is a deferred enhancement, not Plan 1).
- **`FakeTickTimer` virtual-clock** added with Phase 1B tests.

---

## Target project graph (end of Phase 1B)

```
Mouse2Joy.Contracts            net8.0  — no deps. Primitives shared across
                                          the port boundary: PhysicalKey
                                          (HID usage), MouseButton,
                                          ScrollDirection, KeyModifiers.
                                          (Persistence-only enums stay in
                                          Persistence.Models.)
Mouse2Joy.Persistence          net8.0  → Contracts
Mouse2Joy.Platform.Abstractions net8.0 → Contracts. Ports + port DTOs:
                                          IVirtualPad, IInputBackend,
                                          ITickTimer, IGlobalHotkey,
                                          ISingleInstanceGuard, ITrayIcon,
                                          IAppPaths, ISetupProbe, IOverlay,
                                          IEngineStateSource, GamepadReport,
                                          RawEvent, EngineStateSnapshot,
                                          EngineMode.
Mouse2Joy.Engine               net8.0  → Abstractions, Contracts.
                                          Pure mapping/curve/stick logic.
                                          NO OS code, NO Win32, NO WPF.
Mouse2Joy.Platform.Windows     net8.0-windows → Abstractions, Contracts,
                                          Persistence. The ONLY assembly
                                          with [DllImport] / ViGEm /
                                          Microsoft.Win32 / WPF-tray.
Mouse2Joy.Platform.Linux       net8.0  → Abstractions, Contracts. Phase 1B:
                                          NotSupported stubs only.
Mouse2Joy.UI / Mouse2Joy.App   net8.0-windows (WPF, replaced in Phase 3)
```

Engine references Abstractions+Contracts; nothing references Engine except
UI/App. Platform projects never see the engine.

**Resolved (2026-05-16):** `EngineStateSnapshot` + `EngineMode` +
`IEngineStateSource` move into `Platform.Abstractions` as part of the port
surface (alongside `GamepadReport`/`RawEvent`). `EngineStateSnapshot` is
already a pure data record and already references `GamepadButtons` (in
Abstractions), so this introduces no new coupling and no per-tick mapping
cost; the engine produces the snapshot type directly. No remaining flagged
sub-decisions in Plan 1.

---

## Phase 1A — Contracts + HID-usage key migration (atomic, schema-touching)

OS-neutral, fully Windows-testable, no platform split yet. One coordinated
change (avoids migrating/moving the key type twice).

1. **Create `Mouse2Joy.Contracts` (`net8.0`, no deps).** Move the
   cross-boundary primitives in (`MouseButton`, `ScrollDirection`,
   `KeyModifiers`, and the key type). Persistence-only enums (`Stick`,
   `Trigger`, `DPadDirection`, `GamepadButton`, `MouseAxis`,
   `AxisComponent`) stay in `Persistence.Models`. `Persistence` references
   `Contracts`. **Risk to verify:** moved types are serialized;
   `InputSource` is polymorphic via `$kind`. Plain value types are
   namespace-agnostic in `System.Text.Json`; keep `$kind` discriminator
   strings **unchanged** so only field shapes change. Verify the
   polymorphic resolver path explicitly.
2. **`VirtualKey` → `PhysicalKey`, re-represented as a USB HID usage**
   (Usage Page 0x07). Rename type + all call sites
   ([HotkeyBinding](../../src/Mouse2Joy.Persistence/Models/HotkeyBinding.cs),
   [KeySource](../../src/Mouse2Joy.Persistence/Models/InputSource.cs),
   `RawEvent`, `HotkeyMatcher`, etc.).
3. **Scancode(set 1)+E0 ↔ HID Usage bidirectional table** — pure logic.
   **Mandatory unit tests** (repo convention): both directions, round-trip,
   extended/E0 cases, unmapped-code behavior.
4. **Profile schema migration** (`Profile.CurrentSchemaVersion` bump +
   registered migration per [MIGRATION_CONVENTIONS.md](../MIGRATION_CONVENTIONS.md)):
   JSON-node rewrite of `keySource.key` `{scancode,extended}` →
   `{hidUsage}`. Idempotent, tolerant of missing fields. Dedicated migration
   test per convention (prior-version migrates, no-op profile still loads,
   version stamp updated). Do **not** pin version numbers in tests/docs.
5. **Build a general settings-migration pipeline**: an `AppSettings`
   analogue of `ProfileStore.DeserializeProfile`'s peek-`schemaVersion`-and-
   chain. Then `AppSettings.CurrentSchemaVersion` bump + migration rewriting
   `hotkeyBinding.key`. Tests mirror the profile-migration test shape.
6. `dotnet test Mouse2Joy.sln` green on Windows is the 1A gate.

**1A exit:** profiles + settings transparently migrate; key identity is HID
usage end-to-end; `Contracts` extracted; behavior unchanged on Windows.

## Phase 1B — Platform decoupling (no behavior change on Windows)

Introduce every seam and the project skeleton while still on Windows, app
fully working, tests green throughout.

1. **`Mouse2Joy.Platform.Abstractions` (`net8.0`)** — ports + port DTOs
   (graph above). `IVirtualPad`/`IInputBackend`/`IEngineStateSource` move
   here from Engine; `GamepadReport`/`RawEvent`/`EngineStateSnapshot`/
   `EngineMode` move here. Engine references Abstractions and produces the
   snapshot type directly (no parallel type, per-tick path untouched).
2. **Extract remaining seams** behind interfaces, wrapping today's Windows
   code unchanged: `ITickTimer` (was
   [WaitableTickTimer](../../src/Mouse2Joy.Engine/Threading/WaitableTickTimer.cs)),
   `IGlobalHotkey` (panic — see supervisor note), `ISingleInstanceGuard`
   (**silent-exit contract**, matching
   [App.xaml.cs:42](../../src/Mouse2Joy.App/App.xaml.cs)), `ITrayIcon`,
   `IAppPaths`, `ISetupProbe` (generic `{requirement,status,remediation}`;
   Windows reports ViGEmBus/Interception/admin via the relocated
   `ViGEmHealth`/`DriverHealth`), `IOverlay` (WPF overlay wrapped unchanged;
   see flagged snapshot sub-decision).
3. **Retarget + relocate (ONE atomic change, O2):** Engine/Input/VirtualPad
   `net8.0-windows` → `net8.0`; simultaneously relocate **all** Win32 /
   ViGEm / `Microsoft.Win32` / WPF-tray code into a new
   `Mouse2Joy.Platform.Windows` (`net8.0-windows`). Splitting these red-CIs
   `main` on CA1416. `Microsoft.Win32.Registry` gets an explicit package
   ref there. UI/App stay WPF for now (replaced in Phase 3).
4. **Scaffold `Mouse2Joy.Platform.Linux` (`net8.0`)** — every seam a
   `NotSupportedException` stub. Filled in Plan 2.
5. **Supervisor/watchdog boundary (design, O3):** on Linux *we* own the
   evdev grab; an engine crash while grabbing = user's input captured with
   no escape, and a dead panic key. Plan 1 carves an explicit
   supervisor-process boundary in the seam design so the grab lifetime +
   panic key can live independent of the engine on Linux. Windows keeps its
   OS-level `RegisterHotKey` independence. Implementation is Plan 2; the
   *boundary/contract* is fixed here so Plan 2 isn't boxed in.
6. **Startup platform selector** picks the impl set via
   `RuntimeInformation.IsOSPlatform`, feeds DI.
7. **Tests (convention):** all existing tests green post-move on Windows
   (primary gate). Add a **virtual-time `FakeTickTimer`** (O6) and make
   engine tick-loop tests deterministic against it. Unit-test the
   platform-selector branching and `IAppPaths` path computation (pure,
   per-OS table-testable now). P/Invoke/tray/overlay/hotkey stay untested
   (Win32/visual — explicitly called out per convention).

**1B exit:** byte-identical Windows behavior; no OS type reachable except
through a port; Linux project compiles (stubs); Windows still shippable.

---

## Phase 2 — De-risking spikes (throwaway, not merged)

Throwaway harnesses; each produces a findings write-up under
`ai-docs/research/`. Outcomes feed Plan 2.

### Spike S1 — Linux non-UI path (priority 1)

Console harness on Linux: open an evdev device, `EVIOCGRAB` (suppression
equivalent), translate evdev `KEY_*`/rel axes ↔ `PhysicalKey`(HID)/`RawEvent`,
feed the **real unmodified** `Mouse2Joy.Engine`, emit an Xbox-style pad via
`/dev/uinput`.

**Success criteria (all required):**
- `evtest`/`jstest` (and ideally one real Linux/Proton title) sees pad
  output from the binding; grabbed real input is suppressed.
- **Real-profile round-trip (O9):** one of the user's *existing* profiles,
  migrated through Phase 1A to HID `PhysicalKey`, drives correctly on Linux
  — exercises scancode→HID→evdev end-to-end on real data, not a synthetic
  binding.

**Proves:** ports sufficient; HID key model genuinely portable; suppression
holds on Linux; engine needs zero changes.

**Environment:** user-owned bare-metal Linux. *Out of plan scope — the
user owns provisioning; this plan does not track it.*

### Spike S2 — Windows Avalonia-overlay gating (priority 2, forks Plan 2)

Minimal standalone Avalonia app: borderless, transparent, top-most,
**click-through**, correct multi-monitor placement, above a real
**borderless-fullscreen** game on Windows (O8 — borderless only; exclusive-
fullscreen is out of scope per INITIALWORK and not tested here). Baseline:
[WindowStyles.cs](../../src/Mouse2Joy.UI/Interop/WindowStyles.cs) +
[MonitorInfo.cs](../../src/Mouse2Joy.UI/Interop/MonitorInfo.cs).

**Success (all required):** click-through verified (game gets the clicks),
overlay stays above a real borderless-fullscreen game, correct multi-monitor
placement, acceptable redraw cost.

**Outcome forks Plan 2's overlay track:**
- **Pass →** overlay migrates to Avalonia; full unification, WPF dropped.
- **Fail →** keep WPF overlay on Windows behind `IOverlay` + author a
  separate native Linux overlay; rest of UI is Avalonia, overlay-agnostic.

**2 exit:** both spikes run; pass/fail recorded with evidence; overlay fork
resolved; no spike code merged.

---

## Phase 3+ — Coarse roadmap (detailed in Plan 2)

Shape depends on Phase 2 outcomes.

- **Linux platform impls** (harden S1): real evdev backend (mouse+keyboard
  likely unified — the Windows split is Windows-kernel-specific), `/dev/uinput`
  pad, `ITickTimer` via `timerfd`, XDG `IAppPaths`, `IGlobalHotkey` +
  evdev-grab ownership inside the **supervisor process** designed in 1B,
  `ITrayIcon` (StatusNotifierItem/AppIndicator — DE-dependent, risky),
  Linux `ISetupProbe` content (`/dev/uinput` access, `input` group), shipped
  udev rule + first-run onboarding UX, native↔HID translation tables in the
  Linux capture adapter.
- **Main UI → Avalonia, big-bang cutover:** re-author all 8 `.xaml` +
  code-behind + custom controls against the unchanged engine/VM layer;
  single switch; WPF dropped for the main app.
- **Overlay track:** per the S2 fork.
- **Cross-platform CI matrix** (Windows + Linux build/test).
- **Still deferred (not Plan 2 either, per feasibility Decision #3):**
  anti-cheat full-screen titles; Linux packaging (Flatpak/AppImage/deb);
  focus-existing single-instance UX; all macOS work (design-aware only).

## Risks / watch-items

- **Polymorphic-serialization relocation (1A.1):** keep `$kind` strings
  stable; verify the `InputSource` resolver after the type move.
- **Two schema migrations in 1A** (Profile + the first-ever settings
  migration) — settings pipeline is net-new infra, the largest hidden cost.
- **`Nefarius.ViGEm.Client`** is confined to `Platform.Windows`; verify it
  still restores cleanly post-retarget.
- **Big-bang UI cutover** means a long window where `main` UI is still WPF;
  mitigated by 1B keeping WPF fully working until Phase 3 lands.
- **Linux tray** is DE-dependent — a Plan 2 risk, not solved here.

## Follow-ups

- This plan changes no code. All flagged sub-decisions are now resolved;
  **Phase 1A is the first executable unit** and Phase 1B is unblocked.
- Plan 2 (`ai-docs/plans/CROSSPLATFORM_PORT_PLAN_2.md`) authored after Phase
  2; details Phase 3+ from spike outcomes.
- Per repo convention, an `ai-docs/implementations/` write-down is produced
  when Phase 1A/1B actually land (not now — nothing implemented yet).
