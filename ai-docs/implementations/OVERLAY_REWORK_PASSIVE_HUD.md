# Overlay rework: passive HUD + per-monitor windows + main-window config tab

## Context

[OVERLAY_TAB_FIXES.md](OVERLAY_TAB_FIXES.md) wired the existing configure-mode UX correctly, but the user — playing on a multi-monitor setup — couldn't *see* the configure-mode toolbar: the overlay window spanned the full virtual screen and the toolbar at `HorizontalAlignment="Center"` landed on the seam between monitors. More fundamentally, "drag a transparent thing to position another transparent thing" is a poor UX even on a single monitor. This change pivots the overlay's role:

- **The live overlay is a passive HUD only.** No clicks, no drag, no configure mode. Permanently click-through. Pure presentation.
- **All configuration moves to the main window's Overlay tab.** A widget tree on top, an *Add widget* button, a per-row *Edit…* and *Remove*. The Edit dialog is modal and handles both Add and Edit.
- **One overlay window per monitor.** Each widget picks its monitor explicitly. Coordinates are monitor-local DIPs. Per-monitor DPI is honored.
- **Widgets are now instances, not types.** Multiple `LeftStick`s, multiple `ProfileStatus`es, etc. Each instance has a `Guid` `Id`.
- **Widgets can be grouped.** A widget can declare a parent. Children store offsets from the parent's resolved position; moving a parent moves its whole subtree on screen. Children inherit the parent's monitor; the editor disables the Monitor field when a Parent is set.
- **No backwards compatibility.** Mouse2Joy is pre-release; the user wipes their settings on first launch with the new code.

## What changed

- **Schema** — [OverlayLayout.cs](../../src/Mouse2Joy.Persistence/Models/OverlayLayout.cs): `WidgetConfig` gains `Id` (Guid string, default-generated), `ParentId` (nullable id string), `MonitorIndex` (int, 0=primary, ignored when parented). Position semantics documented inline: absolute-monitor-local-DIPs when root, parent-relative offset when child.
- **Per-monitor enumeration** — new [MonitorInfo.cs](../../src/Mouse2Joy.UI/Interop/MonitorInfo.cs): `MonitorInfo` record + `MonitorEnumerator.Enumerate()` over `EnumDisplayMonitors`/`GetMonitorInfo`/`GetDpiForMonitor`. Primary is always at index 0; remaining monitors sorted by left/top edge for stable ordering.
- **Per-monitor overlay** — new [OverlayCoordinator.cs](../../src/Mouse2Joy.UI/Overlay/OverlayCoordinator.cs): owns one [OverlayWindow](../../src/Mouse2Joy.UI/Views/OverlayWindow.xaml) per monitor, resolves parent offsets into absolute (X, Y) plus effective monitor index, buckets widgets by monitor, and pushes each bucket to the right window. Listens for `SystemEvents.DisplaySettingsChanged` and reapplies on display changes; widgets pointing at a vanished monitor fall back to monitor 0 without rewriting settings.
- **OverlayWindow simplified** — [OverlayWindow.xaml](../../src/Mouse2Joy.UI/Views/OverlayWindow.xaml) is now just a transparent click-through window over a single monitor; the configure-mode toolbar, wash, Esc handler, drag wiring, and `LayoutChanged` event are gone. Construct with a `MonitorInfo`; sizes itself to that monitor's DIP bounds.
- **OverlayWidgetHost simplified** — [OverlayWidgetHost.cs](../../src/Mouse2Joy.UI/Overlay/OverlayWidgetHost.cs): drops `ConfigureMode` / `LayoutChanged` / mouse handlers. New `LoadResolved((WidgetConfig, double absX, double absY)[])` takes already-resolved positions from the coordinator.
- **NumericUpDown** — new [NumericUpDown.xaml](../../src/Mouse2Joy.UI/Controls/NumericUpDown.xaml) + [.cs](../../src/Mouse2Joy.UI/Controls/NumericUpDown.xaml.cs): a small in-tree user control with `▲`/`▼` `RepeatButton`s (200/50 ms hold-to-repeat), mouse wheel = step, Up/Down arrows = step, Enter = commit, soft-bounded by `Min`/`Max` (manual entry can exceed; buttons clamp). `ValueChanged` event for downstream subscribers.
- **WidgetEditorWindow** — [.xaml](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml) + [.cs](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs) is a redesign: modal, takes `(WidgetConfig? existing, IReadOnlyList<WidgetConfig> siblings, IReadOnlyList<MonitorInfo> monitors)`, returns the saved config in `Result` on Save. Top-down layout: Type / Parent / Monitor / Visible / X / Y / Scale / per-widget Options / Save+Cancel. Type changes convert in place (preserve Id/Position/Scale/Parent/Monitor/Visible, reset Options to new defaults). Parent picker excludes self and descendants (cycle prevention). Scale has a numeric + slider duo with bidirectional sync; the slider's range (0.4–2.5) is a soft bound — manual entry above 2.5 is accepted, the slider just pegs.
- **Main window Overlay tab** — [MainWindow.xaml](../../src/Mouse2Joy.UI/Views/MainWindow.xaml): replaced the old `ItemsControl` with a `TreeView` of `WidgetTreeRowViewModel`s, with per-row Edit and Remove buttons. Toolbar at the top: *Show overlay* / *Add widget* / *Reset to defaults*.
- **WidgetTreeRowViewModel** — new [.cs](../../src/Mouse2Joy.UI/ViewModels/WidgetTreeRowViewModel.cs): hierarchical row exposing `Id` / `Type` / `ShortLabel` (auto-numbered `#N` when there are duplicates of a type within a sibling group) / `Visible` (mutable, write-throughs to settings via parent delegate) / `Children` (ObservableCollection). Replaces the old flat `WidgetRowViewModel`, which is deleted.
- **Main window code-behind** — [MainWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/MainWindow.xaml.cs): new handlers for *Add* (modal editor with `null`), *Edit* (modal editor with the existing config), *Remove* (with three-way prompt — *Yes*=delete subtree, *No*=detach children at their absolute on-screen positions, *Cancel*=do nothing), *Reset to defaults*, *Visibility toggle*. `RebuildWidgetTree` walks the flat list to a tree by `ParentId`. The detach path uses an in-file `ResolveAbsolute` mirror of the coordinator's resolver so it doesn't depend on the live coordinator state.
- **App-side wiring** — [App.xaml.cs](../../src/Mouse2Joy.App/App.xaml.cs): `_overlay` field replaced with `_overlayCoordinator`; coordinator is constructed once, `Apply(layout)` is called whenever settings change, `Show()`/`Hide()` toggles all per-monitor windows. The old `PersistOverlayLayout` and `SetOverlayConfigureMode` methods are gone.
- **Cleanup** — [AppServices.cs](../../src/Mouse2Joy.UI/ViewModels/AppServices.cs) drops `SetOverlayConfigureMode`. [MainViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/MainViewModel.cs) drops `ConfigureOverlay` command. [WindowStyles.cs](../../src/Mouse2Joy.UI/Interop/WindowStyles.cs) drops the now-unused `SetClickThrough` helper.
- **Test update** — [ProfileSerializationRoundtripTests.cs](../../tests/Mouse2Joy.Persistence.Tests/ProfileSerializationRoundtripTests.cs)'s `AppSettings_roundtrips` now asserts that `Id`, `ParentId`, and `MonitorIndex` round-trip cleanly. All 48 tests still pass.

## Key decisions

- **`MonitorIndex` is positional, not a stable id.** Primary is always index 0; secondary monitors sorted by left edge. The user's mental model is "monitor 2 is the one on the right", and positional captures that — a stable `DeviceName`-keyed identifier would be more robust to display-arrangement changes but doesn't match how users think. If unplugging+replugging-in-different-order ever becomes a real problem, we can switch to keying by `DeviceName` without changing the public WidgetConfig shape (just `MonitorIndex`'s interpretation).
- **Children inherit the parent's monitor; the field is greyed out, not hidden.** Greying out the disabled state (rather than hiding) makes it visible *why* it can't be edited. The user explicitly asked for this for any field that doesn't apply.
- **Per-monitor DPI is honored at window-construction time.** `MonitorInfo.BoundsDip` divides physical pixels by the monitor's effective DPI; the overlay window uses that. WPF's automatic per-monitor DPI handling kicks in for the canvas contents. Mixed-DPI setups (4K + HD) work without the user noticing.
- **The coordinator owns the windows; the windows don't know about each other.** Single point of truth for "which widgets go where" is the coordinator's `Render()` method. `OverlayWindow` is a dumb presenter — no resolver, no bucketing, no parent-chain math. Simpler model and means a window can be torn down and rebuilt (e.g. on display change) without losing layout state.
- **Cycle prevention is enforced at edit time AND defensively in the resolver.** `WidgetEditorWindow` excludes self+descendants from the parent picker. The coordinator's `ResolveInto` also memoizes early so a stale or hand-edited cyclic file resolves to *something* rather than stack-overflowing. Both layers; cheap.
- **Detaching children preserves their on-screen position.** When the user removes a parent and chooses *No, keep children*, each child's offset is converted to absolute (parent's resolved X+Y added) and `ParentId` cleared. Visually nothing moves; logically the tree flattens. The alternative (leave child X/Y as-is, just clear `ParentId`) would teleport them to (childOffsetX, childOffsetY) on screen — almost certainly not what the user wants.
- **Modal editor, no live preview.** The editor stages all edits in memory; nothing commits until Save. With per-monitor windows showing the live HUD, the user can park the main window on monitor 2 and watch the HUD on monitor 1 — Save/Cancel on a modal dialog is fast enough, and live edits would either need to skip persistence (so Cancel can revert) or write-and-rollback (more complex than it earns). The previous live-edit `WidgetRowViewModel` is deleted.
- **Type change converts in place rather than spawning a new id.** Children parented to this widget stay attached. Position/scale/parent/monitor/visible all carry over. Only `Options` resets. The user explicitly chose this — pragmatic for "I tried `LeftStick` here, switching to `MouseActivity` in the same slot."
- **Soft bounds on Scale.** Slider 0.4–2.5; numeric input accepts anything. Widget rendering still clamps internally (each widget's `OnRender` does `Math.Clamp(scale, 0.4, 2.5)` for layout sanity), so going to `5.0` doesn't render at 5×, but the persisted value is honored if we later relax the render clamp.
- **`ShortLabel` is auto-numbered, not user-editable.** "Stick #1", "Stick #2" disambiguates duplicates without adding a `Name` field to `WidgetConfig`. Easy to upgrade later if it becomes useful — separate field, no migration needed.
- **No drag-anywhere.** All position changes go through the editor's `NumericUpDown`s. The user explicitly rejected drag-on-overlay (multi-mon UX), and a draggable preview canvas in the main window was rejected too in favor of "just numeric inputs". Keystroke-friendly, predictable, and works the same on any monitor count.
- **Positions stored in monitor-local DIPs, not virtual-screen pixels.** Each overlay window is positioned at `monitor.BoundsDip.X/Y`, so `Canvas.SetLeft(widget, cfg.X)` places the widget at `cfg.X` DIPs from the monitor's top-left. This means moving a monitor in display arrangement (without changing its index) leaves the layout intact — widgets stay in the same monitor-local position. Virtual-screen coordinates would have shifted them.
- **Schema version stays at 1.** The user is wiping settings, so there's no migration to write. Bumping to v2 with no migration code would mislead any future reader. If a future change *does* need migration, that's the time to bump.

## Files touched

**New:**
- [src/Mouse2Joy.UI/Interop/MonitorInfo.cs](../../src/Mouse2Joy.UI/Interop/MonitorInfo.cs)
- [src/Mouse2Joy.UI/Overlay/OverlayCoordinator.cs](../../src/Mouse2Joy.UI/Overlay/OverlayCoordinator.cs)
- [src/Mouse2Joy.UI/Controls/NumericUpDown.xaml](../../src/Mouse2Joy.UI/Controls/NumericUpDown.xaml) + [.cs](../../src/Mouse2Joy.UI/Controls/NumericUpDown.xaml.cs)
- [src/Mouse2Joy.UI/ViewModels/WidgetTreeRowViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/WidgetTreeRowViewModel.cs)

**Modified:**
- [src/Mouse2Joy.Persistence/Models/OverlayLayout.cs](../../src/Mouse2Joy.Persistence/Models/OverlayLayout.cs) — schema additions.
- [src/Mouse2Joy.UI/Overlay/OverlayWidgetHost.cs](../../src/Mouse2Joy.UI/Overlay/OverlayWidgetHost.cs) — strip configure/drag plumbing; new `LoadResolved`.
- [src/Mouse2Joy.UI/Views/OverlayWindow.xaml](../../src/Mouse2Joy.UI/Views/OverlayWindow.xaml) + [.cs](../../src/Mouse2Joy.UI/Views/OverlayWindow.xaml.cs) — minimal canvas, takes a `MonitorInfo`, permanently click-through.
- [src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml) + [.cs](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs) — modal Add/Edit, Type/Parent/Monitor/Visible/X/Y/Scale + dynamic Options, returns `WidgetConfig`.
- [src/Mouse2Joy.UI/Views/MainWindow.xaml](../../src/Mouse2Joy.UI/Views/MainWindow.xaml) + [.cs](../../src/Mouse2Joy.UI/Views/MainWindow.xaml.cs) — Overlay tab is now toolbar + TreeView; Add/Edit/Remove handlers replace the row-bound editor flow.
- [src/Mouse2Joy.UI/ViewModels/AppServices.cs](../../src/Mouse2Joy.UI/ViewModels/AppServices.cs) — drop `SetOverlayConfigureMode`.
- [src/Mouse2Joy.UI/ViewModels/MainViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/MainViewModel.cs) — drop `ConfigureOverlay` command.
- [src/Mouse2Joy.UI/Interop/WindowStyles.cs](../../src/Mouse2Joy.UI/Interop/WindowStyles.cs) — drop unused `SetClickThrough`.
- [src/Mouse2Joy.App/App.xaml.cs](../../src/Mouse2Joy.App/App.xaml.cs) — `_overlay` → `_overlayCoordinator`; remove configure-mode and persist-on-drag plumbing.
- [tests/Mouse2Joy.Persistence.Tests/ProfileSerializationRoundtripTests.cs](../../tests/Mouse2Joy.Persistence.Tests/ProfileSerializationRoundtripTests.cs) — assert new fields round-trip.

**Deleted:**
- `src/Mouse2Joy.UI/ViewModels/WidgetRowViewModel.cs` — the live-edit flat row VM is replaced by the modal editor.

**Deliberately unchanged:**
- The widget classes (StickWidget etc.) and their `OptionSchema`. The schema-driven editor still works the same way.
- The engine, capture, and emulation layers. Nothing about input or the gamepad changes.
- `AppSettings.SchemaVersion`. Stays at 1 because the user wipes settings on first launch with the new code; bumping it would imply migration code that doesn't exist.

## Follow-ups

- **Per-monitor offsetting with non-overlapping monitors.** If a child widget is parented to a widget on monitor A but the offset pushes it past A's bounds, it will be clipped by A's window — there's no spillover into monitor B even if the desktop arrangement makes that visually adjacent. Today this is fine: groups should fit on one monitor. If we ever want a group to span monitors, we'd revisit per-monitor windows vs. one big virtual-screen window.
- **`MonitorIndex` reordering.** If the user reorders monitors in display settings, persisted indices repoint. Track by `DeviceName` if this becomes a problem; the WidgetConfig contract is stable.
- **Drag-to-reposition on the overlay.** Deliberately omitted — the user wants numeric-only because it works the same on every monitor. If a future user asks for it, the cleanest place to add it is the Edit Widget dialog (a small per-monitor preview pane with drag handles), not the live HUD.
- **`ShortLabel` becoming user-editable.** Auto-numbered `#N` is fine for v1. A user-supplied `Name` field on `WidgetConfig` would be a separate field, no breaking change.
- **Widget editor live preview.** Modal-with-Save matches the user's stated preference, but a small "Apply" button (commit without closing) would be useful for tuning a Color or Scale while watching the HUD. Two-line addition.
- **MouseActivity trail rendering.** Still unimplemented — see the [previous doc](OVERLAY_TAB_FIXES.md#follow-ups). The schema field is preserved through the rework.
