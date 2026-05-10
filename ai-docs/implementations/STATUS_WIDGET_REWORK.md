# Status widget rework + Engine status indicator widget

## Context

The original `Status` overlay widget bundled three responsibilities: a colored engine-mode dot, the engine-mode text, and the active profile name. It also exposed a Width/Height Size section in the editor and used a fixed Segoe UI 11pt font — neither of which makes sense for what is fundamentally a text readout.

The rework splits the widget cleanly:

- **`Status`** is now a **pure text widget** that displays one of: engine mode, profile name, a button's pressed/released text, or an axis numeric value. Full font / color / rotation / vertical-stack styling. Auto-sized to its rendered text.
- **`EngineStatusIndicator`** is a new widget that renders **only the dot** (color tracks `Snapshot.Mode`).

This makes both pieces independently composable and gives text the styling it deserves. Per CLAUDE.md the user wipes settings on upgrade (schema v1 has no migrations), so no conversion logic is shipped — old `Status` widgets in saved JSON simply fall through to new defaults.

## What changed

- New widget `EngineStatusIndicator`: square widget rendering a centered ellipse colored by the current engine mode (Active/SoftMuted/Off, all colors configurable).
- `StatusWidget` rewritten end-to-end. The dot is gone from this widget. The new options:
  - `sourceKind`: Enum — `Text` / `Mode` / `Profile` / `Button` / `Axis`. `Text` reads `sourceName` as a literal string for static HUD labels.
  - `sourceName`: String — button name (e.g. `"A"`) or axis name (e.g. `"LeftTrigger"`); ignored for Mode/Profile.
  - `label`: String — optional inline prefix (e.g. `"LT:"` → `"LT: 0.42"`).
  - `pressedText` / `releasedText`: Strings shown when the button is pressed / released. Default `"Pressed"` / `""`.
  - `axisFormat`: Enum — `Decimal` (signed `+0.42` style) or `Percent`. `axisDecimals`: Int 0–4.
  - `fontFamily` / `fontSize` / `bold` / `italic` / `underline`: standard text styling.
  - `rotation`: Int 0–359 — rotates the layout's stride axis from horizontal. The glyphs are always laid out along this rotated axis with their natural advance widths.
  - `verticalStack` (editor label "Upright letters"): Bool — controls *per-glyph* counter-rotation only, never the stride direction. When off, each glyph rotates with the stride (rotation=90° + off → text written sideways). When on, each glyph stays upright in screen space (rotation=90° + on → top-to-bottom vertical text with readable letters; rotation=45° + on → diagonal text with each letter still readable). JSON key kept as `verticalStack` for continuity within the dev branch.
  - `letterSpacing`: Int −10..+40 — pixel offset added between glyphs along the stride axis. 0 leaves the fast horizontal path intact; non-zero forces per-glyph rendering.
  - `textColor`, `showBackground`, `backgroundColor`.
- `StatusWidget` auto-sizes by setting its own `FrameworkElement.Width`/`Height` from the measured `FormattedText`. The hosting `OverlayWidgetHost` only positions widgets (via `Canvas.SetLeft/Top`); it doesn't size them, which is what makes auto-sizing safe.
- `WidgetEditorWindow`:
  - Adds `EngineStatusIndicator` to the type list and to `SquareOnlyTypes`.
  - New `AutoSizedTypes` set (`{"Status"}`) drives a new `UpdateSectionsForType` helper that hides Size and shows the Font section in its place.
  - Bespoke options panel for `Status` (`BuildStatusOptionsPanel`): the secondary picker (`sourceName`) and per-source fields are gated by `sourceKind` and rebuild when it changes. Other widget types continue using the schema-driven generic panel.
  - New Font section with: family ComboBox (populated from `Fonts.SystemFontFamilies`, cached), size NumericUpDown, B / I / U toggle row, rotation NumericUpDown with reset, vertical-text checkbox, text-color picker.
  - B / I / U toggles use a programmatically-built `Style` (`StyleToggleHighlight`) that mirrors the lock-aspect button's `:Checked` look so the "is the toggle on?" answer is consistent across the editor.
- `MainWindow.OnResetOverlay` now seeds an `EngineStatusIndicator` paired with two `Status` text widgets (mode + profile, the latter with label `"Profile:"`), demonstrating the split. The mode-text widget is anchored to the right side of the indicator via the existing parent-anchor system.

## Key decisions

- **Status is text-only; the dot is its own widget.** Cleaner mental model: "what to display" (text) and "engine state at a glance" (dot) are independent. Future indicators (recording status, profile-pending, etc.) can join the indicator family without bloating Status.
- **Auto-size, not user-sized.** `Width`/`Height` on `WidgetConfig` are unused by the Status widget. The footprint follows the rendered glyph block (plus a small padding) and the rotated AABB. The editor hides the Size section for Status to avoid showing fields the widget ignores.
  - Implementation note: the widget *does not* mutate `Config.Width`/`Config.Height` (that would dirty persistence). It sets `FrameworkElement.Width`/`Height` directly on each render. The Canvas reads those for layout, the host's positioning still works, and `MeasureOverride` agrees with `OnRender` because both call the same `BuildPlan` plan.
- **Auto-size also runs at layout time, not just render time.** `OverlayCoordinator.Resolve` needs the widget's footprint to compute anchor math (e.g. `SelfAnchor=Bottom` subtracts the widget's height from the anchor point). If the resolver used `Config.Width/Height`, an auto-sized widget would resolve to the wrong absolute position — anchored as if it were 8×8 while actually rendering at 50px tall, hanging off the bottom of the screen.
  - The widget exposes a static `StatusWidget.MeasureFootprint(WidgetConfig)` that computes the same footprint the renderer will produce. `OverlayCoordinator.MeasureForLayout` dispatches by type: Status calls the static measurer, every other type keeps the existing `Config.Width/Height` path. `BuildPlan` was made static to support this — it accepts a nullable snapshot and substitutes layout-stable stand-ins (e.g., the wider of pressedText/releasedText) when called for measurement, so the box doesn't visually grow or shrink as values change.
- **Text is centred against ink bounds, not the FormattedText line box.** WPF's `FormattedText.Height` is the typeface's full line box (ascender + descender + leading) — it reserves descender space even when the rendered glyphs (e.g. `"0%"`, `"42"`) don't use it. Drawing inside that line box looks top-heavy because the visible ink sits in the upper portion. Instead, `BuildPlan` calls `FormattedText.BuildGeometry(...).Bounds` to get the actual ink bounding rectangle and uses *its* size as the content. The drawing origin is offset by `-inkBounds.Top` / `-inkBounds.Left + Padding` so the ink is flush with — and visually centred in — the padded background rect. Vertical-stack mode still uses the per-glyph line box (per-glyph geometry would be disproportionate complexity for marginal gain).
- **Rotation drives the stride axis; the upright toggle only counter-rotates glyphs.** This is a deliberately orthogonal split:
  - Rotation alone (toggle off): every glyph rotates with the stride. rotation=90° gives sideways text (the whole word reads turned 90°). Rendered fast as a single `FormattedText` with a `RotateTransform` when letter spacing is zero, otherwise per-glyph with each glyph rotating around its stride point.
  - Rotation + upright (toggle on): glyphs follow the rotated stride axis but stay upright in screen space. rotation=90° gives top-to-bottom vertical text with readable letters. rotation=45° gives diagonal text where each letter is still readable. Rendered glyph-by-glyph (no per-glyph rotation transform applied).
  - Earlier iteration of this rework had the toggle implicitly add 90° to the stride axis — that conflated layout direction with glyph orientation and made "rotation=90 + toggle on" produce horizontal text instead of vertical. Toggle now ONLY toggles glyph counter-rotation; the user gets vertical text by setting rotation=90 themselves.
- **Per-glyph layout when needed; single FormattedText otherwise.** `BuildPlan` chooses between two paths: the cheap single-FormattedText path (no letter spacing) and the per-glyph path (`upright` true OR `letterSpacing != 0`). Per-glyph is unavoidable for either feature — `FormattedText` has no letter-spacing API and rotates as a unit. The decision is internal; user sees consistent behaviour.
- **Letter spacing as a pixel offset, not a multiplier.** Range −10..+40 pixels added per stride. Pixel offsets are font-size-relative-feeling enough at typical HUD sizes (12–34pt) and avoid the "0.95 vs 1.05" guessing-game multipliers like `Typography.SetLetterSpacing` use. Negative values let the user tighten cramped fonts; positive values let them spread out HUD-style "S T A T U S" labels.
- **User-configurable button text strings.** Empty `releasedText` is the common HUD pattern (label appears only while the button is held, e.g. "TURBO"). `pressedText` defaults to `"Pressed"` so the widget is self-explaining out of the box.
- **Configurable axis format.** Decimal default matches `AxisWidget`'s inline label (signed, 2 decimals); Percent is more readable for HUD-style readouts. Decimals 0–4 covers both extremes.
- **`label` is per-widget, not the editor `Name` field.** The `Name` field on `WidgetConfig` exists to disambiguate rows in the editor's widgets table; the `label` option is what actually renders in the overlay. Conflating them would reuse the wrong field for the wrong purpose.
- **Status's options panel is bespoke; other widgets stay schema-driven.** A `sourceKind` that gates which sub-fields appear can't be expressed in the flat `OptionSchema` list. Rather than complicating `OptionDescriptor` with conditional rules, `BuildOptionsPanel` checks for `Type == "Status"` and routes to a hand-built UI. Other widget types continue to use the generic schema-driven panel — no regression.
- **No migration.** Per CLAUDE.md the user wipes settings on upgrade (schema v1, no migrations). Old `Status` widgets in stored JSON deserialise into the new shape with default options (sourceKind=Mode), losing the dot. Acceptable because users wipe.
- **Font family list is cached statically.** `Fonts.SystemFontFamilies` enumeration is moderately expensive; the list rarely changes during the editor's lifetime. The cache is a single static field on the editor class.

## Files touched

**Modified:**
- [src/Mouse2Joy.UI/Overlay/Widgets/StatusWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/StatusWidget.cs) — full rewrite as text widget; dot rendering removed. Exposes `MeasureFootprint(WidgetConfig)` for layout-time auto-sizing.
- [src/Mouse2Joy.UI/Overlay/OverlayCoordinator.cs](../../src/Mouse2Joy.UI/Overlay/OverlayCoordinator.cs) — `Resolve` now dispatches through `MeasureForLayout` so auto-sized widgets resolve against their actual rendered size.
- [src/Mouse2Joy.UI/Overlay/Widgets/OptionDescriptor.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/OptionDescriptor.cs) — register `EngineStatusIndicator` schema.
- [src/Mouse2Joy.UI/Overlay/OverlayWidgetHost.cs](../../src/Mouse2Joy.UI/Overlay/OverlayWidgetHost.cs) — register `EngineStatusIndicator` factory case.
- [src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml) — name Size-section elements (`SizeHeader`, `WidthLabel`, `LockSwapRow`, `HeightLabel`); add new Font section (`FontHeader` + `FontHost`) sharing the Size rows so the same screen real estate is reused.
- [src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs) — add `AutoSizedTypes`, `UpdateSectionsForType`, custom Status options panel (`BuildStatusOptionsPanel` + `BuildStatusDynamicSection`), Font panel (`BuildFontPanel` + helpers), shared row builders (`BuildStringRow` / `BuildBoolRow` / `BuildColorRow` / `BuildIntRow` / `BuildEnumRow`), `StyleToggleHighlight` for the B/I/U toggles, and the cached system-font list.
- [src/Mouse2Joy.UI/Views/MainWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/MainWindow.xaml.cs) — `OnResetOverlay` seeds the new indicator + text-widget pair instead of the old bundled Status.

**New:**
- [src/Mouse2Joy.UI/Overlay/Widgets/EngineStatusIndicatorWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/EngineStatusIndicatorWidget.cs) — dot-only widget.

**Deliberately unchanged:**
- [src/Mouse2Joy.Persistence/Models/OverlayLayout.cs](../../src/Mouse2Joy.Persistence/Models/OverlayLayout.cs) — schema unchanged. `Width`/`Height` stay on `WidgetConfig` (used by other widgets); `Status` simply ignores them.
- Tests under `tests/` — round-trip serialization is type-agnostic, so new options keys round-trip naturally via the `JsonElement` dict.

## Subsequent changes

- **Removed `showLabel` from Button/Axis/TwoAxis widgets.** Labels-on-bars/buttons/sticks were a hold-over from the bundled era. With the new Status text widget the user composes their own labels: place a Status widget anchored to the bar/button/stick, set `sourceKind=Text` (or any source they want), and they have full font / color / rotation control instead of the cramped, hardcoded-Consolas inline label. ButtonGrid keeps its internal per-cell labels because those are layout-driven, not user-configurable.
- **`Text` source kind for static text.** No new option key — the always-visible `label` field is the content. For dynamic sources the label is an optional prefix; for `Text` the resolver returns "" so the composed string is just the label. An earlier iteration had a separate "Text" input in the dynamic section that aliased to the same JSON key; the duplicate input dropped typed-but-not-yet-committed text when the user switched source kinds, because `BuildStringRow` commits on `LostFocus` and the source-kind change rebuilds the dynamic section without that focus loss firing. One field, one storage key — simpler and bug-free.
- **Text color is RGB-only (no alpha).** `BuildColorRow` gained an `allowAlpha` flag; the Status widget's text color row passes `false`. Translucent text reads poorly on overlay backdrops; if a user wants subtlety they can drop the whole widget's opacity. Background colors keep alpha (translucent backdrops are the common HUD case). Stale `#AARRGGBB` values from older sessions are stripped to `#RRGGBB` on load and on every keystroke for the no-alpha rows.

## Follow-ups

- **Color picker UX.** The Color editor remains a hex text box + swatch (matching the existing pattern in `BuildColorEditor`). A real popup color picker would be a good general improvement and would benefit every widget with color options, not just Status.
- **Font preview in the editor.** The Font section currently lists family names without showing a preview glyph in the chosen face. A custom ItemTemplate that renders each name in its own font would make picking faces much easier.
- **Snapshot reactivity for label-only widgets.** A Status widget configured as just a static `label` (no source resolved) still re-renders on every engine tick because `RenderState` calls `InvalidateVisual`. Cheap in practice, but a future optimisation could short-circuit when nothing the widget depends on has changed.
- **Per-axis label heuristics.** `AxisWidget` uses short labels (`"LT"`, `"LX"` …); the new Status widget shows the raw value with no implicit prefix. Users can set `label` themselves; an automatic short-label option (`autoLabel: bool`) could be added if it's a common ask.
- **Migration for old `Status` JSON.** If at some point we decide to honor pre-rework profiles, the heuristic would be: a `Status` widget whose Options contain `showMode`/`showProfileName`/`showBackground` (and only those) is "old shape" — split into an indicator + one or two text widgets at the same anchor. Not implemented because users wipe.
