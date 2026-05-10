# Overlay tab wiring fixes + per-widget editor

## Context

The overlay feature (transparent topmost window with widgets that render live engine state at 60 Hz) was already built end-to-end — the [OverlayWindow](../../src/Mouse2Joy.UI/Views/OverlayWindow.xaml) renders, the six widgets draw, drag-to-reposition works, and the click-through toggle via `WS_EX_TRANSPARENT` is correct. But the entry points on the **Overlay** tab were broken in four ways that, together, made it look like the feature didn't exist:

1. *Show overlay* checkbox wrote `Settings.Overlay.Enabled` but never called `Show()` / `Hide()` on the live window — toggling it appeared to do nothing until the next app launch.
2. *Configure overlay layout* button entered configure mode silently (the overlay is mostly transparent) with no visible affordance, no Done button, no Esc handler, and no way to persist drags. Once in, you couldn't tell you were in, and once out, your moves were gone.
3. The *Widgets* `ItemsControl` bound `IsChecked="{Binding Visible}"` directly to a `WidgetConfig` record's init-only `Visible` property — the binding silently no-opped, so the visibility checkboxes did nothing.
4. The data model already supported per-widget `Scale` and an `Options` `Dictionary<string, JsonElement>`, but no widget actually *read* its `Options`, and no UI ever wrote to them — the per-widget customization surface existed in storage but was unreachable.

This change fixes all four and adds a full per-widget editor (Visible / Scale / X / Y / typed options) that streams every edit live into the running overlay.

## What changed

- **Show overlay** ([MainWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/MainWindow.xaml.cs)): `OnToggleOverlay` now routes through `_vm.ToggleOverlayCommand`, which calls `AppServices.SetOverlayVisible` — that path actually shows/hides the live window and persists the setting.
- **Configure overlay layout** has a real exit affordance:
  - [OverlayWindow.xaml](../../src/Mouse2Joy.UI/Views/OverlayWindow.xaml) gained a `ConfigureToolbar` (top-center, *Drag widgets to reposition · Esc or [Done]*) and a faint dark `ConfigureWash` rectangle so the user *sees* they're in configure mode.
  - [OverlayWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/OverlayWindow.xaml.cs) raises `ConfigureModeExitRequested` from the Done click and from `OnPreviewKeyDown` when Esc is pressed; [App.xaml.cs](../../src/Mouse2Joy.App/App.xaml.cs) subscribes and calls `SetOverlayConfigureMode(false)`.
  - Drags now persist: [OverlayWidgetHost](../../src/Mouse2Joy.UI/Overlay/OverlayWidgetHost.cs) raises a new `LayoutChanged` event on `MouseUp`; [OverlayWindow](../../src/Mouse2Joy.UI/Views/OverlayWindow.xaml.cs) forwards it; [App.xaml.cs](../../src/Mouse2Joy.App/App.xaml.cs) subscribes via `PersistOverlayLayout()` which snapshots `_overlay.SaveLayout()` (preserving the user's `Enabled` preference) into settings. `SetOverlayConfigureMode(false)` also calls `PersistOverlayLayout()` so positions are saved on exit even without an explicit drag.
- **Widget visibility & per-widget editor**:
  - New [WidgetRowViewModel](../../src/Mouse2Joy.UI/ViewModels/WidgetRowViewModel.cs) — `ObservableObject` wrapper around a `WidgetConfig` with mutable `Visible` / `Scale` / `X` / `Y` / `Options` and a `SetOption*` API. Every change calls a parent delegate that writes through to `AppSettings` and triggers a live overlay reload.
  - New [WidgetEditorWindow](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml) — modeless editor with a *Visible* checkbox, *Scale* slider (0.4–2.5, snap 0.05), numeric *X* / *Y* boxes, a dynamic *Options* panel built from the widget's `OptionSchema`, and *Reset widget defaults* / *Close* buttons. Edits stream live; no Apply.
  - [MainWindow.xaml](../../src/Mouse2Joy.UI/Views/MainWindow.xaml) Widgets row template gains an *Edit…* button that opens the editor for that row. [MainWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/MainWindow.xaml.cs) builds a `WidgetRowViewModel` per persisted widget on load, replaces them on Reset (and closes any open editors), and manages an `_openEditors` dictionary so clicking *Edit…* on the same widget twice brings the existing window forward instead of duplicating.
- **Widgets actually consume their Options now**:
  - New [OptionDescriptor.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/OptionDescriptor.cs) — `OptionKind` enum (Bool/Color/String/Int/Enum), `OptionDescriptor` record, and a `WidgetSchemas.For(string type)` dispatch.
  - [OverlayWidget.cs](../../src/Mouse2Joy.UI/Overlay/OverlayWidget.cs) gains `ReadBool` / `ReadString` / `ReadInt` / `ReadColorBrush` / `ReadColorPen` helpers that coerce `JsonElement` values from `Config.Options` with a fallback when absent or unparseable.
  - Each widget now declares `public static IReadOnlyList<OptionDescriptor> OptionSchema` and reads its options in `OnRender`:
    - [StickWidget](../../src/Mouse2Joy.UI/Overlay/Widgets/StickWidget.cs): `accentColor`, `showLabel`.
    - [TriggerBarsWidget](../../src/Mouse2Joy.UI/Overlay/Widgets/TriggerBarsWidget.cs): `accentColor`, `orientation` (`horizontal` | `vertical`).
    - [ButtonGridWidget](../../src/Mouse2Joy.UI/Overlay/Widgets/ButtonGridWidget.cs): `accentColor`, `compact` (drops button labels).
    - [ProfileStatusWidget](../../src/Mouse2Joy.UI/Overlay/Widgets/ProfileStatusWidget.cs): `showMode`, `showProfileName`.
    - [MouseActivityWidget](../../src/Mouse2Joy.UI/Overlay/Widgets/MouseActivityWidget.cs): `accentColor`, `trailLength` (0–10, persisted but unused — placeholder for a future trail effect).
- **Live reload plumbing**: [AppServices](../../src/Mouse2Joy.UI/ViewModels/AppServices.cs) gains `Action ReloadOverlay`; [App.xaml.cs](../../src/Mouse2Joy.App/App.xaml.cs)'s `ReloadOverlayLayout()` re-reads `settings.Overlay` and calls `_overlay.LoadLayout(...)`. [MainViewModel](../../src/Mouse2Joy.UI/ViewModels/MainViewModel.cs) exposes thin `ReloadSettings()` and `ReloadOverlay()` passthroughs so MainWindow doesn't reach into `_svc` directly.
- **Reset to default layout** ([MainWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/MainWindow.xaml.cs)) now closes any open editors, rebuilds the row VMs, and calls `_vm.ReloadOverlay()` so the live overlay snaps back without requiring a hide/show cycle.

## Key decisions

- **Routing toggles through the existing `ToggleOverlayCommand` rather than rewriting the path.** `App.SetOverlayVisible` already does both Show/Hide and persistence ([App.xaml.cs:231](../../src/Mouse2Joy.App/App.xaml.cs)). The bug was that `OnToggleOverlay` bypassed it. Fixing the call site instead of duplicating logic keeps a single source of truth for "what does it mean to show the overlay."
- **`WidgetRowViewModel` as a UI-only mutable bridge over the immutable `WidgetConfig` record.** `WidgetConfig` is intentionally a `record` with init-only properties so the persisted shape stays immutable and JSON round-trips cleanly. Adding a UI-side observable wrapper preserves that boundary; the alternative — converting `WidgetConfig` to a class with mutable properties — would have rippled through the persistence layer for no real benefit.
- **Live edits, no Apply button.** Editor mutations stream into the row VM, which write-through-saves AppSettings *and* calls `ReloadOverlay`. The user wanted a tight feedback loop ("change scale, see the widget shrink"); an Apply button would force them to hit it after every micro-tweak. Settings writes are cheap (one small JSON file) and `_overlay.LoadLayout(...)` rebuilds the widget tree in microseconds.
- **`ReloadOverlay` rebuilds, doesn't surgically patch.** It calls `_overlay.LoadLayout(...)` which clears and re-creates the widgets. Simpler than tracking which widget changed; the cost is one render frame's worth of work per edit, which is invisible at 60 Hz. If we needed to preserve in-flight drag state during reload we'd patch surgically — we don't.
- **`OptionDescriptor` schemas live on the widget classes, not in a central registry file.** Each widget owns the contract for "what's editable about me." `WidgetSchemas.For(type)` is a thin dispatch over the widget classes, not a parallel definition of the same data. New widget = static schema + `ReadXxx` calls in `OnRender` + a case in `WidgetSchemas.For`. The editor reflects the schema automatically.
- **`SetOptionString` for colors instead of a `Color` type.** Storing colors as hex strings (`#FF8800`) means the persisted JSON stays human-readable and we don't need a custom `Color`-vs-`JsonElement` converter. The widget's `ReadColorBrush` parses on render; the editor's `TryParseColorBrush` validates as the user types and only commits when it parses cleanly (so a half-typed `#FF` doesn't blow away the previous valid color). Cost: typing a hex string is slightly less ergonomic than a color picker. Discussed and deferred — the editor uses a `TextBox` + live swatch preview rather than pulling in `Xceed.Wpf.Toolkit` just for `ColorPicker`. Per [feedback_third_party_source](../../../.claude/projects/.../memory/feedback_third_party_source.md), the user prefers thin in-tree implementations to vendoring.
- **Configure-mode toolbar lives inside the overlay window, not as a separate dialog.** Floats with the overlay you're configuring rather than introducing a second window the user has to track. The wash + toolbar give configure mode an unmistakable visual identity.
- **Esc in configure mode requires an explicit window activation.** The overlay sets `WS_EX_NOACTIVATE`, so it won't grab keyboard focus on its own. `SetConfigureMode(true)` calls `Activate()` and `Focus()`; without that, `OnPreviewKeyDown` would never fire because nothing in the overlay has focus. This is the price of a non-activating click-through window — fine for configure mode (you're only there for a few seconds) but worth knowing.
- **`PersistOverlayLayout()` preserves the persisted `Enabled` flag.** `_overlay.SaveLayout()` derives `Enabled` from `IsVisible`, which is fine in normal flow but wrong if you happened to hide the overlay during a drag. We keep the user's preference rather than letting a momentary visibility state flip it.
- **`trailLength` shipped without rendering.** The data model and editor support it; `MouseActivityWidget.OnRender` doesn't read it yet. Documented as a follow-up. Calling it out now instead of removing it because the schema-driven editor makes it nearly free to surface, and the value persists so a future render pass picks it up automatically.
- **Open editor windows are tracked in a `Dictionary<WidgetRowViewModel, WidgetEditorWindow>`.** Two reasons: (a) clicking *Edit…* on a row that already has an editor open should bring the existing window forward, not spawn a duplicate, (b) *Reset to default layout* throws away all the row VMs and the editors bound to those VMs would leak — `OnResetOverlay` closes them all before rebuilding rows.

## Files touched

**New:**
- [src/Mouse2Joy.UI/Overlay/Widgets/OptionDescriptor.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/OptionDescriptor.cs)
- [src/Mouse2Joy.UI/ViewModels/WidgetRowViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/WidgetRowViewModel.cs)
- [src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml)
- [src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs)

**Modified:**
- [src/Mouse2Joy.UI/Overlay/OverlayWidget.cs](../../src/Mouse2Joy.UI/Overlay/OverlayWidget.cs) — `Read*` helpers.
- [src/Mouse2Joy.UI/Overlay/OverlayWidgetHost.cs](../../src/Mouse2Joy.UI/Overlay/OverlayWidgetHost.cs) — `LayoutChanged` event; respect `_configureMode` opacity in `Load`.
- [src/Mouse2Joy.UI/Overlay/Widgets/StickWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/StickWidget.cs)
- [src/Mouse2Joy.UI/Overlay/Widgets/TriggerBarsWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/TriggerBarsWidget.cs)
- [src/Mouse2Joy.UI/Overlay/Widgets/ButtonGridWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/ButtonGridWidget.cs)
- [src/Mouse2Joy.UI/Overlay/Widgets/ProfileStatusWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/ProfileStatusWidget.cs)
- [src/Mouse2Joy.UI/Overlay/Widgets/MouseActivityWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/MouseActivityWidget.cs)
- [src/Mouse2Joy.UI/Views/OverlayWindow.xaml](../../src/Mouse2Joy.UI/Views/OverlayWindow.xaml) — toolbar, wash, root `Grid`.
- [src/Mouse2Joy.UI/Views/OverlayWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/OverlayWindow.xaml.cs) — events, Esc handler, focus on configure-mode entry.
- [src/Mouse2Joy.UI/ViewModels/AppServices.cs](../../src/Mouse2Joy.UI/ViewModels/AppServices.cs) — `ReloadOverlay`.
- [src/Mouse2Joy.UI/ViewModels/MainViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/MainViewModel.cs) — `ReloadSettings()`, `ReloadOverlay()` passthroughs.
- [src/Mouse2Joy.UI/Views/MainWindow.xaml](../../src/Mouse2Joy.UI/Views/MainWindow.xaml) — Widgets row template (Edit… button).
- [src/Mouse2Joy.UI/Views/MainWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/MainWindow.xaml.cs) — row VM management, OnEditWidget, write-through plumbing.
- [src/Mouse2Joy.App/App.xaml.cs](../../src/Mouse2Joy.App/App.xaml.cs) — subscribe to `LayoutChanged` / `ConfigureModeExitRequested`, `PersistOverlayLayout`, `ReloadOverlayLayout`, wire `ReloadOverlay` into both `AppServices` initializers.

**Deliberately unchanged:**
- [src/Mouse2Joy.Persistence/Models/OverlayLayout.cs](../../src/Mouse2Joy.Persistence/Models/OverlayLayout.cs) — the persisted shape (`OverlayLayout`, `WidgetConfig`) is fine. We added a UI-side mutable wrapper rather than mutating the record.
- The engine, capture, and emulation layers — none of this touches input or the gamepad.

## Follow-ups

- **Real color picker UX.** Today the user types or pastes a hex code. A small custom popup with a saturation/value square + hue strip would be friendlier; deferred so we don't pull in a NuGet color picker for one field. If it comes up again, the place to hook it is `WidgetEditorWindow.BuildColorEditor`.
- **MouseActivity trail rendering.** `trailLength` is in the schema, persists through saves, and shows a numeric editor — but `MouseActivityWidget.OnRender` doesn't draw a trail yet. The widget would need to keep a small ring buffer of recent `(dx, dy)` snapshots and stroke them with decreasing alpha. Out of scope here; the schema field documents the intent so the work is well-defined when it lands.
- **Per-widget custom positions in *Reset widget defaults*.** Currently `Reset widget defaults` keeps the widget's current `X`/`Y` and resets only Visible/Scale/Options. *Reset to default layout* (the bigger button on the Overlay tab) is what restores positions. Probably correct — confirm with the user if anyone asks.
- **`WS_EX_NOACTIVATE` + Esc handler.** We `Activate()` the window on entering configure mode so KeyDown fires. If the user clicks back into Mouse2Joy's main window during configure, Esc will route to that window instead. Acceptable for now (the Done button still works); a global hook would be the proper fix if this turns out to be confusing.
- **Drag bounds.** Nothing currently prevents dragging a widget off-screen. `Canvas.SetLeft` / `SetTop` just commit whatever you give it. Worth clamping to the virtual screen rect, but easy to add later.
