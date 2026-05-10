# Widget editor: table list, anchor positioning, and editor reorg

## Context

After [OVERLAY_REWORK_PASSIVE_HUD.md](OVERLAY_REWORK_PASSIVE_HUD.md) landed, the overlay tab and editor went through three rounds of follow-up tightening — all about making the configuration surface more usable now that the live overlay is purely passive and config is the only way to position widgets.

1. **Table over tree.** The original `TreeView` row layout was ambiguous as a list (children indented under parents, expanders, rows not visually separated). Switched to a flat table with a clearly-visible header, gridded rows, and a Parent column carrying what the indentation used to encode. Also added a per-widget *Label* field, click-anywhere-on-row to open the editor, and visual gaps between rows.
2. **Editor field grouping.** *Parent* and *Monitor* are conceptually part of where a widget is positioned, not standalone metadata. Moved them under the *Position* header, above *X*/*Y*. Monitor sits first because it's the broader reference frame; *Anchor parent* (renamed from "Parent" to reinforce its role in the anchor system) sits below it. The Monitor combobox shows a tooltip when greyed out (parent overrides it) so the user knows *why* it isn't editable. The "(none)" entry in the Anchor parent dropdown is now labelled **Monitor** — when no widget parent is set, the monitor's bounds are the reference frame, so the dropdown's wording matches what's actually happening.
3. **Anchor-based positioning.** Plain X/Y offsets from a hardcoded top-left were hard to use for "5-px gap to the right of widget A" or "16-px from the bottom-right of the screen". Replaced the offset interpretation with a unified anchor system: each widget picks an *Anchor to* point on its reference frame (parent or monitor) and a *Self anchor* on its own bounding box; the persisted X/Y is now the offset between those two points.

## What changed

### 1. Overlay tab table

- [MainWindow.xaml](../../src/Mouse2Joy.UI/Views/MainWindow.xaml) Overlay tab — replaced the `TreeView` with a `ListView` styled as a table:
  - Fixed header band (light grey background, bordered) with column titles: **Visible · Edit · Remove · Label · Type · Parent**.
  - Rows have `Margin="0,3,0,3"` (gap between rows), `Background="#FAFAFA"`, `BorderBrush="#E0E0E0"`, `Cursor="Hand"`, and stretch to fill the column widths defined on the header.
  - Each row's content is wrapped in a `Grid` with `Tag="{Binding}"` and `MouseLeftButtonUp="OnRowClicked"` so clicking anywhere outside the toggle/buttons opens the editor.
- New [WidgetRowViewModel](../../src/Mouse2Joy.UI/ViewModels/WidgetRowViewModel.cs) — flat row VM. Replaces `WidgetTreeRowViewModel` from the previous iteration. Carries `Id`, `Type`, `Name` (display), `ParentLabel`, and a two-way `Visible`. `Visible` change goes through a parent-supplied delegate (`OnRowVisibleChanged`) that write-throughs to settings and triggers `ReloadOverlay`.
- [WidgetConfig](../../src/Mouse2Joy.Persistence/Models/OverlayLayout.cs) gained a `Name` field (`string`, default `""`). Edited via a new *Label* `TextBox` at the top of [WidgetEditorWindow](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml). Empty `Name` falls back to `Type #N` when there are duplicates of a type, otherwise the bare type name.
- [MainWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/MainWindow.xaml.cs) — handlers `OnRowClicked` and `OnEditWidget` both delegate to a shared `OpenEditorForRow(WidgetRowViewModel)`. The Visible toggle, Edit, and Remove buttons rely on default WPF button click semantics (events marked handled) so the row click handler doesn't fire for them.
- The editor's parent picker also uses the user's `Name` when set — entries display as `"Label  —  Type"` (label first, type second) so disambiguation is at-a-glance.

### 2. Position section reorganization

- [WidgetEditorWindow.xaml](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml) — *Monitor* and *Anchor parent* moved under the *Position* header, in that order (Monitor first as the broader reference frame), before X/Y. The Monitor `ComboBox` gets `ToolTipService.ShowOnDisabled="True"` so its tooltip surfaces when the field is disabled (the existing "Inherits monitor from parent." text was already wired in the code-behind via `UpdateMonitorEnabled`).
- The Anchor parent dropdown's "no parent" entry is labelled **Monitor** rather than "(none)" so the wording matches the actual reference frame at runtime. The persisted shape is unchanged — `WidgetConfig.ParentId` is still nullable and null still means "no widget parent"; only the UI label changed. The Overlay tab's table also follows: a row's *Parent* column shows "Monitor" instead of "—" when the widget is standalone.
- The X/Y row labels rename to *Offset X* and *Offset Y* once the anchor system landed (they're always offsets now — no parent/no-parent flip).

### 3. Anchor system

- [OverlayLayout.cs](../../src/Mouse2Joy.Persistence/Models/OverlayLayout.cs) — added `Anchor` enum (`TopLeft`, `Top`, `TopRight`, `Left`, `Center`, `Right`, `BottomLeft`, `Bottom`, `BottomRight`) and two `WidgetConfig` fields:
  - `AnchorPoint` — which point on the reference frame the widget anchors to.
  - `SelfAnchor` — which point on the widget itself lands at the anchor + offset.
  - Both default to `TopLeft`, which exactly preserves the previous "X/Y is offset from frame's top-left" semantics for any newly-created widget.
  - `X`/`Y` semantics tightened: now always *offset in monitor-local DIPs* from the resolved anchor point. (Previously `X`/`Y` was "absolute when root, offset-from-parent-top-left when child" — the unified anchor system replaces both branches.)
- New [WidgetSizes](../../src/Mouse2Joy.UI/Overlay/Widgets/WidgetSizes.cs) registry — maps `Type` → base unscaled `Size`, plus `RenderedFor(type, scale)` that multiplies by clamped scale. The resolver needs the rendered widget size to subtract its self-anchor offset; this avoids constructing a real `FrameworkElement` and running a measure pass on the hot reload path.
- [OverlayCoordinator.cs](../../src/Mouse2Joy.UI/Overlay/OverlayCoordinator.cs) resolver rewritten:
  - For each widget, compute its `Rect` reference frame: the parent's rendered rect (recursively resolved) when parented, else `(0, 0, monitor.Width, monitor.Height)` for the monitor.
  - `anchorPx = framePoint(frame, AnchorPoint) + (X, Y)`.
  - Render top-left = `anchorPx - widgetPoint(size, SelfAnchor)`.
  - Memoized; cycle defense pre-seeds `memo` so a malformed cyclic file resolves to a top-left placement instead of stack-overflowing.
- [WidgetEditorWindow.xaml](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml) gained two `ComboBox`es under the Position section, between Monitor and Offset X: *Anchor to* and *Self anchor*. Both populated from the same nine-entry list, displayed in 3×3 reading order so the user can map "top-left … center … bottom-right" mentally.
- [WidgetEditorWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs) — new `PopulateAnchorCombos`, `OnAnchorPointChanged`, `OnSelfAnchorChanged`, and an internal `AnchorChoice` record. The old `UpdatePositionLabels` (which switched X/Y between "X" and "X (offset)") was removed since the labels are always *Offset* now.
- [MainWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/MainWindow.xaml.cs) detach-children flow updated: when removing a parent, each child is resolved through the new anchor math (mirrored locally as `ResolveRenderTopLeft` + `AnchorPointOnRect`), then re-pinned with `(AnchorPoint=TopLeft, SelfAnchor=TopLeft, X=resolvedX, Y=resolvedY, MonitorIndex=resolvedMon, ParentId=null)`. The child stays at the same on-screen position.

## Key decisions

- **Flat table beats tree for a config list this small.** Six default widgets, maybe a dozen with duplicates and groups. A `TreeView`'s expander chrome and indentation overhead doesn't earn its complexity at that scale. A flat table with a Parent column makes the relationship visible without imposing a navigation pattern.
- **Click anywhere on the row, except actionable controls.** This is the intuitive default for any "list of editable items" UI. It's free with WPF's button click semantics — buttons mark the routed event handled, so the row's `MouseLeftButtonUp` doesn't fire when the user clicks Edit/Remove. The Visible `CheckBox` does the same.
- **`Name` is a separate stored field, not a recomputation of `ShortLabel`.** ShortLabel was always auto-generated. The user wanted a label they can set themselves, and falling back to the auto-numbered hint when empty keeps the table readable for anyone who hasn't bothered to label their widgets.
- **`Anchor`-keyed combobox values, displayed in reading order.** Some users think of anchors as "compass points" (N/S/E/W/center), others as "corners + edges". The 3×3 reading order — top-left, top, top-right, left, center, right, bottom-left, bottom, bottom-right — works for both mental models and matches how a user would draw the grid on a napkin.
- **Anchor change does NOT auto-translate X/Y.** Asked the user explicitly. Switching from `TopLeft`/`TopLeft` to `BottomRight`/`BottomRight` keeps the typed offset; the widget visibly jumps. Reasoning: the user is *specifying a new positioning intent*, not asking us to re-express the same on-screen position. Auto-translation would make the field feel like it's lying about what it stores.
- **Self-anchor + reference-anchor instead of just one anchor field.** Lets the user say "place my widget's bottom-right at the parent's top-left, with no offset" — a common pattern for placing a widget *just outside* a corner. With a single anchor (positioning the widget's top-left), they'd have to compute the widget's width as a negative offset, which couples the position to the widget's size. With two anchors, the math stays independent of widget size.
- **`WidgetSizes` is a hardcoded registry, not a measure pass.** Each widget already has `MeasureOverride` returning a fixed unscaled size. Building a fresh `FrameworkElement` and calling `Measure` just to read a number that's hardcoded in `MeasureOverride` is wasteful. The registry stays in sync with the widget classes by convention; widgets are few and stable. Documented in `WidgetSizes.cs`.
- **Resolver computes a rectangle per widget; widgets render at the absolute top-left.** The `OverlayWidgetHost` no longer participates in any layout math beyond `Canvas.SetLeft/SetTop`. All hierarchy/anchor logic lives in the coordinator. Single point of truth for "where does this widget render".
- **Detach-children snaps to TopLeft/TopLeft.** When the user detaches a subtree, each child becomes a root with the simplest anchor configuration that produces the same on-screen position. Alternative would have been to preserve the original anchors and recompute the offset; that would be more clever but harder to reason about ("why are my anchors `BottomRight`/`Center` now?"). Snapping to TopLeft/TopLeft is the obviously-correct rebase.
- **Disabled tooltips need `ToolTipService.ShowOnDisabled="True"`.** WPF's default is to suppress tooltips on disabled controls. The Monitor combobox already had its tooltip text wired in code (`UpdateMonitorEnabled`); the XAML attribute is what makes it actually surface to the user.
- **No migration on schema additions.** The user is pre-release and explicitly chose "wipe settings on first launch with the new code" each time. New fields all have safe defaults (`Name=""`, `AnchorPoint=TopLeft`, `SelfAnchor=TopLeft`) so old layouts technically still load with semantics that match what they used to mean — but the wipe stays the documented expectation.

## Files touched

**New:**
- [src/Mouse2Joy.UI/Overlay/Widgets/WidgetSizes.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/WidgetSizes.cs) — per-type unscaled size registry.

**Modified:**
- [src/Mouse2Joy.Persistence/Models/OverlayLayout.cs](../../src/Mouse2Joy.Persistence/Models/OverlayLayout.cs) — added `Anchor` enum; `WidgetConfig` gained `Name`, `AnchorPoint`, `SelfAnchor`; updated `X`/`Y` doc comments to reflect unified offset semantics.
- [src/Mouse2Joy.UI/Overlay/OverlayCoordinator.cs](../../src/Mouse2Joy.UI/Overlay/OverlayCoordinator.cs) — resolver rewritten to anchor + self-anchor math; new `ResolvedRect` record, `LookupMonitorSize`, and `AnchorPoint(Rect, Anchor)` helper.
- [src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml) — added Label, Anchor to, Self anchor rows; moved Parent and Monitor under Position; renamed X/Y to Offset X/Y; added `ToolTipService.ShowOnDisabled` on Monitor.
- [src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs) — Label sync + Save commit; `PopulateAnchorCombos`; anchor change handlers; removed `UpdatePositionLabels`; updated `ShortLabelFor` to put Name first.
- [src/Mouse2Joy.UI/ViewModels/WidgetRowViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/WidgetRowViewModel.cs) — flat row VM with `Name` and `ParentLabel`. Replaces the deleted `WidgetTreeRowViewModel`.
- [src/Mouse2Joy.UI/Views/MainWindow.xaml](../../src/Mouse2Joy.UI/Views/MainWindow.xaml) — Overlay tab is now a table with header + gridded rows.
- [src/Mouse2Joy.UI/Views/MainWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/MainWindow.xaml.cs) — flat row builder, `OnRowClicked`, `OpenEditorForRow`; detach-children math rewritten with anchor-aware `ResolveRenderTopLeft`.

**Deleted:**
- `src/Mouse2Joy.UI/ViewModels/WidgetTreeRowViewModel.cs` — replaced by the flat `WidgetRowViewModel`.

**Deliberately unchanged:**
- The widget classes themselves and their `OptionSchema`s. Anchors are a positioning concern handled entirely in the coordinator and editor; widgets just receive their render top-left like before.
- The persistence schema version. Stays at 1; user wipes settings on schema additions.

## Follow-ups

- **Visual anchor picker.** A 3×3 grid of clickable cells would be friendlier than a verbal `ComboBox` ("Top-left", "Top", …). Defer; the verbal list is functional and the labels disambiguate even without a visual.
- **Per-widget previews of the rendered rect when editing.** A small preview pane in the editor showing the resolved on-screen rect would help users grasp the anchor model. Out of scope for this round.
- **`WidgetSizes` drift risk.** If someone changes a widget's `MeasureOverride` without updating `WidgetSizes.BaseFor`, anchor math goes off by however many pixels the widget's actual size diverged. Today the values are duplicated by hand. A unit test asserting the registry matches each widget's `MeasureOverride` would catch drift; haven't written it because the tests project doesn't currently load WPF UI types.
- **Anchor-aware bounds checking.** Nothing prevents typing offsets that push a widget entirely off its monitor / parent. Mostly harmless (the widget just renders out of view, and the user notices and corrects), but a clamp-to-frame option is a future nicety.
- **Stick-widget label height.** `StickWidget` extends its height by 16 px when `showLabel` is true, but `WidgetSizes.BaseFor` returns the with-label height unconditionally. If a user turns off the label, the self-anchor's "bottom" point will be 16 px below where the widget actually ends. Acceptable for now (16 px is small), but worth knowing.

---

## Round 4: category-based widget types, per-source options, opt-in background, W/H sizing

### Context

`Round 4` consolidated several long-standing awkward bits in the widget catalogue:

- The *Type* dropdown listed implementation-specific names (`LeftStick`, `Triggers`, `Buttons`, …) which coupled the user-visible model to one rendering per widget kind. Want a vertical right trigger? You had to add the "Triggers" widget that rendered both LT and RT.
- Every widget hardcoded a translucent black background panel into its `OnRender`. Users wanted backgrounds to be a deliberate choice rather than a built-in default.
- A single `Scale` multiplier on a hardcoded base size made it hard to dial in a specific widget size; W and H were coupled and there was no way to ask for, say, a 16-px-tall horizontal trigger bar.

### What changed

- **`WidgetConfig` schema**: `Scale` removed. Added `Width` (DIPs), `Height` (DIPs), and `LockAspect` (bool, default true). Editor enforces lock-on for square-only types; otherwise the user controls it via a checkbox between the W and H fields.
- **`WidgetSizes.cs` deleted.** Its only consumers (the anchor resolvers in `OverlayCoordinator` and `MainWindow.ResolveRenderTopLeft`) now read `Width`/`Height` directly from the config.
- **Widget catalogue replaced**: the per-widget classes were renamed/rewritten to expose categories the user picks from in the editor:
  - `Status` (was `ProfileStatus`) — engine mode + profile name. No source.
  - `Axis` (was `Triggers`) — a single horizontal-or-vertical bar driven by one of `LeftTrigger`, `RightTrigger`, `LeftStickX/Y`, `RightStickX/Y`. Bipolar stick sources fill centre-out; unipolar trigger sources fill from the start.
  - `TwoAxis` (was `LeftStick`/`RightStick`) — one circle-with-dot, source picks `LeftStick` or `RightStick`. Square-only.
  - `Button` — single-button indicator. Source picks one of the 15 XInput buttons. Pressed = filled with accent, otherwise outlined.
  - `MouseActivity` — unchanged behaviour; sized via W/H, square-only.
  - `Background` — new utility. Filled rounded rect; options are `color` (with alpha, e.g. `#90000000`) and `cornerRadius` (0–24 px). Sized via W/H; intended as a parent for grouping or as a standalone backdrop.
  - `ButtonGrid` (was `Buttons`) — kept under Utility for users who want the 15-button grid in one panel.
- **`showBackground` opt-in option**: every non-Background widget gained a `showBackground: bool` (default false). When true, the renderer draws the same translucent dark rounded rect that used to be unconditional. Default off matches the user's "no default background" stance.
- **`WidgetSchemas.For(type)`** ([OptionDescriptor.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/OptionDescriptor.cs)) updated to dispatch to the new TypeIds and per-category schemas. Each schema starts with `source` (where applicable) and ends with `showBackground` (where applicable).
- **Editor**:
  - The Scale row is gone. New **Size** section between Position and Options: a *Width* `NumericUpDown`, a "🔗 Lock aspect ratio" checkbox between the two, and a *Height* `NumericUpDown`. When the lock is on, editing one updates the other to preserve a captured ratio (`_lockedAspect`, captured at editor open and re-captured each time the lock transitions off→on).
  - Square-only widget types (`TwoAxis`, `MouseActivity`) force `LockAspect = true` and disable the toggle, with a tooltip ("This widget must stay square.") that surfaces via `ToolTipService.ShowOnDisabled`.
  - Type changes preserve W/H if the user customised them (i.e. the staged values diverge from the previous type's defaults), otherwise re-seed to the new type's defaults. Switching INTO a square-only type snaps H to W.
  - Per-category default sizes live in `WidgetEditorWindow.DefaultSizeFor(type)`: Status 220×28, Axis 120×16, TwoAxis 80×80, Button 32×32, MouseActivity 80×80, Background 200×120, ButtonGrid 180×60.
- **`OverlayWidgetHost.Create(type)`** dispatch updated to the new TypeIds.
- **`OverlayCoordinator.Resolve` / `MainWindow.ResolveRenderTopLeft`** read the resolved size from `cfg.Width`/`cfg.Height` directly, replacing the previous `WidgetSizes.RenderedFor(type, scale)` call.
- **`MainWindow.OnResetOverlay`** seeds a representative default layout under the new schema: `Status`, two `TwoAxis` widgets (LeftStick/RightStick), two `Axis` widgets (LeftTrigger/RightTrigger), `MouseActivity`. Each carries the per-category default W/H and (where applicable) a `source` option.
- **Round-trip test** ([ProfileSerializationRoundtripTests.cs](../../tests/Mouse2Joy.Persistence.Tests/ProfileSerializationRoundtripTests.cs)) updated to use the new `Width`/`Height`/`LockAspect` fields and new TypeIds.

### Key decisions

- **Single category dropdown, source as a regular Option.** The alternative — keep specific Type names like `LeftStick` and just *group* them in the dropdown — keeps the rendering tightly coupled to the catalogue. Going to a category + source split lets the user make any axis vertical, any single button big or small, etc., without code changes per combination. The cost is one more click when adding a widget (pick category, then pick source); we considered that a fair trade.
- **`source` lives in `Options`, not as a top-level `WidgetConfig` field.** `Options` is already widget-defined and serialised as raw `JsonElement`s. Adding a top-level `Source` would have created two parallel typed-vs-loose paths through the editor and the resolver. Using the same Options dictionary keeps a single ingestion path; the per-category enum is just declared in the schema.
- **`InputButton` category dropped; ButtonGrid stays under Utility.** The original plan had a separate "InputButton" category for the all-in-one button mask. The user's preferred shape: keep the historical grid as a Utility widget for users who want the all-in-one view, while individual `Button` widgets cover everything else. Avoids two categories that share an audience.
- **`Background` widget rather than per-widget "background widget" toggles.** The Background utility lets the user place a backdrop anywhere (including as a parent for grouping). Keeping it a real widget (not a per-widget `useBackground` field that points at a colour) makes it composable with the anchor system.
- **`showBackground` exists too** — a per-widget bool — because some users will want a translucent panel behind a single widget without the bookkeeping of a parent Background. Default off so the cleaner look ships out of the box.
- **W/H replaces Scale entirely** rather than coexisting. Two ways to size a widget would force the user to think about both — and forces every renderer to do `(W × scale, H × scale)` math. With explicit W/H, what you see in the editor is what you get on screen.
- **Aspect lock is a UI affordance, persisted on the config.** Storing `LockAspect` lets the user's intent survive between sessions; if they set up a 4:3 widget and want to keep it 4:3 forever, the lock stays engaged. Square-only widget types override the persisted value at edit time so a hand-edited file can't break the visual.
- **Square-only types use `Math.Min(W, H)` defensively in their renderers.** The editor enforces the lock, but a hand-edited JSON file could have e.g. `Width: 80, Height: 200` for a TwoAxis. Rather than hard-fail, the renderer picks the smaller axis as the working size. The widget renders correctly (just smaller than the rect) while the editor fixes the values on next open.
- **Default sizes match the previous base sizes.** Status 220×28, TwoAxis 80×80, etc. — the same numbers `WidgetSizes.BaseFor` returned. Means widgets render at the same default appearance as before, even though the path to that appearance is different. Lower friction for the wipe-and-restart flow.
- **Type-change preserves customised sizes.** If the user explicitly set a 200×40 Axis widget, switching its type to Button shouldn't snap to 32×32. The detection is "are W/H exactly the previous type's defaults" — if so, re-seed; otherwise preserve. Heuristic, but correct in the common cases.
- **Bipolar stick axes fill centre-out; unipolar trigger axes fill from the start.** The Axis widget dispatches on the source name. Sticks (-1..+1) fill from the centre toward the edge in the direction of the value; triggers (0..1) fill from the start (left edge horizontal, bottom edge vertical) toward the end. Matches what the user mentally expects from each.

### Files touched

**New / replaced widgets** in [src/Mouse2Joy.UI/Overlay/Widgets/](../../src/Mouse2Joy.UI/Overlay/Widgets/):

- [StatusWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/StatusWidget.cs) — new (replaces `ProfileStatusWidget.cs`).
- [AxisWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/AxisWidget.cs) — new (replaces `TriggerBarsWidget.cs`).
- [TwoAxisWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/TwoAxisWidget.cs) — new (replaces `StickWidget.cs`).
- [ButtonWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/ButtonWidget.cs) — new.
- [BackgroundWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/BackgroundWidget.cs) — new.
- [MouseActivityWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/MouseActivityWidget.cs) — modified (W/H sizing, `showBackground`).
- [ButtonGridWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/ButtonGridWidget.cs) — modified (W/H sizing, `showBackground`, TypeId renamed `Buttons` → `ButtonGrid`).
- [OptionDescriptor.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/OptionDescriptor.cs) — `WidgetSchemas.For` dispatch updated.
- `WidgetSizes.cs` — deleted.

**Schema:**
- [src/Mouse2Joy.Persistence/Models/OverlayLayout.cs](../../src/Mouse2Joy.Persistence/Models/OverlayLayout.cs) — `Scale` removed; `Width`, `Height`, `LockAspect` added.

**Resolver / host:**
- [src/Mouse2Joy.UI/Overlay/OverlayCoordinator.cs](../../src/Mouse2Joy.UI/Overlay/OverlayCoordinator.cs) — reads W/H from config; dropped `Mouse2Joy.UI.Overlay.Widgets` import (no longer needs `WidgetSizes`).
- [src/Mouse2Joy.UI/Overlay/OverlayWidgetHost.cs](../../src/Mouse2Joy.UI/Overlay/OverlayWidgetHost.cs) — `Create` switch updated for new TypeIds.

**Editor:**
- [src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml) — Scale row replaced with Size section (W/H + lock checkbox).
- [src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs) — `WidgetTypes` rewritten; `SquareOnlyTypes` set; `DefaultSizeFor` per-category lookup; `OnWidthChanged`/`OnHeightChanged`/`OnLockAspectClicked` handlers; `_lockedAspect` ratio capture; type-change re-seed/lock logic; `OnScaleSliderChanged` removed.

**Main window:**
- [src/Mouse2Joy.UI/Views/MainWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/MainWindow.xaml.cs) — `OnResetOverlay` seed list rewritten for new categories; `ResolveRenderTopLeft` reads W/H directly; dropped `Mouse2Joy.UI.Overlay.Widgets` import.

**Test:**
- [tests/Mouse2Joy.Persistence.Tests/ProfileSerializationRoundtripTests.cs](../../tests/Mouse2Joy.Persistence.Tests/ProfileSerializationRoundtripTests.cs) — `AppSettings_roundtrips` exercises the new fields and TypeIds; asserts `Width`/`Height`/`LockAspect` round-trip.

### Follow-ups

- **Migration code is not written.** Pre-release wipe is the documented expectation; a settings file from the previous round won't deserialise cleanly because of the `Scale`→`Width`/`Height` swap (and TypeId renames). If someone wants to keep their layouts across the upgrade, that's a small one-off migration to write.
- **Fonts and glyphs scale with widget height.** Each renderer derives font sizes from a height-based scale (`refScale = h / baseH`), so very small widgets clip text. The editor's `Min="8"` on W/H is a soft floor that prevents the worst of this; finer-grained handling (skip text when the widget is too small) would be nicer.
- **Bipolar stick rendering at extreme aspect ratios.** A super-wide horizontal Axis bar driven by a stick X axis fills centre-out, which is what the user wants. A super-tall vertical Axis bar driven by a stick Y axis does the same vertically. No issues observed in testing; calling out because the dual-mode behaviour is implicit in the source name.
- **Buttons could use a colour swap instead of fill-vs-outline.** Today: pressed = filled accent, unpressed = outline. Some users may prefer pressed = bright colour, unpressed = dim colour (no outline). Options field in `ButtonWidget.OptionSchema` could grow a `style: enum` later.
- **No type-grouping separator in the editor's Type combo.** The plan called for a Utility separator; implementing it cleanly with `ItemContainerStyle` adds complexity for visual-only gain. Deferred — the type names already convey the grouping in their order.

---

## Round 5: editor quality-of-life — zero-default offsets, reset/swap controls, orientation auto-swap

### Context

Three small follow-up affordances on the editor after Round 4 shipped:

- New widgets defaulted X/Y to 16 (a margin from the anchor); but with the new anchor system, the user almost always wants `0` so the widget lands flush at the chosen anchor point. They tweak from there.
- No way to clear an offset back to 0 short of typing `0`; small but worth a button.
- Swapping a horizontal Axis bar (e.g. 120×16) to a vertical one means the user has to manually swap W and H. Automating that — both via a Swap button and as a side effect of changing the *orientation* option — eliminates the pointless manual step.

### What changed

- **Add default X/Y → 0.** [WidgetEditorWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs) seeds new widgets with `X = 0, Y = 0`.
- **⟲ Reset buttons** next to *Offset X* and *Offset Y* in the editor; each clears its field to 0. Suppress flag bypasses the change handler so the reset is a deliberate snap, not a "user typed 0" event.
- **⇅ Swap W/H button** in the row between Width and Height (next to the aspect-lock toggle). Swaps `Width` and `Height`, recaptures the locked aspect ratio (so subsequent typed edits use the post-swap ratio), and refreshes the inputs. Disabled for `TwoAxis` and `MouseActivity` with the same "must stay square" tooltip as the lock toggle.
- **Lock aspect ratio became an icon-only `ToggleButton`** rather than a labelled `CheckBox`. Lives on the left of the swap button in the same row. Highlighted (light-blue background, dark-blue border) when on; default chrome when off. Tooltip "Lock aspect ratio" replaces the previous text label so the row stays compact.
- **Orientation auto-swap.** When the user changes an `orientation` option (today only on `AxisWidget`) from horizontal to vertical or vice versa, the editor auto-swaps W/H so a 120×16 horizontal bar becomes a 16×120 vertical bar without the user having to re-type the size. Implemented in [WidgetEditorWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs) `BuildEnumEditor`; the enum editor tracks its previous value and triggers `SwapWidthAndHeight()` on any change to the `orientation` key.

### Key decisions

- **`orientation` is a magic key** in `BuildEnumEditor`. Hardcoding the option key by name is the smallest possible plumbing — no schema flag like `OptionDescriptor.SwapWhOnChange`, no per-widget hook. If a second option ever needs the same behaviour, that's the time to generalise.
- **Reset buttons clear to 0, not "to the type's default offset".** Partly because "default offset" doesn't exist as a concept (per-type defaults are for *size*); partly because 0 is the most useful number under the anchor system — the widget sits exactly at its anchor point.
- **Swap button disabled for square-only types.** Swapping equal numbers is a no-op, but the disabled state with a tooltip is clearer than a button that quietly does nothing. Reuses the same tooltip as the lock checkbox.
- **Auto-swap fires on *any* `orientation` change, not just horizontal↔vertical.** If a future Axis variant adds a third orientation, the swap still fires. If that turns out to be wrong (e.g. a "diagonal" orientation that shouldn't swap), we can scope the trigger then.
