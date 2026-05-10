# Bindings table restyle

## Context

The Bindings table on the Profiles tab was rendering raw record `ToString()`
output for every column — `MouseAxisSource { Axis = X }`,
`StickAxisTarget { Stick = Left, Component = X }`, `Curve { Sensitivity = 1, … }`,
`PersistentStickModel { CountsPerF…`. Hard to read at a glance, and visually
inconsistent with the polished Widgets table on the Overlay tab. This change
brings the Bindings table to the same look-and-feel and replaces the four
text columns with a much smaller, scannable layout.

## What changed

- **Columns reduced to four:** `On · Label · Source · Target`. The previous
  `Curve` and `StickModel` columns were removed; both are tuned in the editor
  dialog and rarely need at-a-glance scanning.
- **New optional `Binding.Label` field.** Users can name a binding (e.g.
  "Steering", "Throttle"). When unset, the row shows
  `Source → Target` in dim italic so the column still reads as identity but
  visually signals "auto".
- **Container switched from `DataGrid` to `ListView`** with the same custom
  header `Border` + `DataTemplate` pattern as the Widgets table — flat
  bottom-only row dividers, transparent background, hand cursor,
  `MouseLeftButtonUp` row-click hook.
- **Row click opens the editor**, matching Widgets. The `On` checkbox eats the
  click via standard hit-testing so toggling enabled does not open the dialog.
- **`Add binding` / `Edit` / `Delete` header buttons stay** — no inline action
  buttons per row (deliberate choice, see decisions below).
- **Source/Target rendered via `BindingDisplay`** — a static formatter that
  pattern-matches each subtype to a friendly string. Includes a Win32
  `GetKeyNameTextW` lookup for `KeySource` so users see "A", "F5",
  "Right Arrow" instead of "Sc:1E".
- **Editor dialog grew a Label textbox** at the top. Empty/whitespace input is
  stored as `null`; trim is applied before save.

## Key decisions

- **Why drop Curve and StickModel from the table at all?** They each have 3-4
  numbers and are tuned in the editor anyway. Showing them in the row added
  noise without giving users a useful at-a-glance signal — by the time you
  care about the deadzone you're going to open the editor. Trade-off
  consciously made: power users lose a small amount of information density,
  but every other user gets a much cleaner table.
- **Why `Label` is optional with auto-fallback rather than required.**
  Required would be friction at "Add binding" for users who don't care.
  Optional + auto means existing profiles keep working with zero migration,
  and the table still shows something identifying for unlabelled rows. The
  auto-label is rendered dim+italic so users see at a glance which rows they
  named themselves.
- **Why a nullable field on `Binding`, not a separate UI-only computed
  property.** Persisting the user's label means it survives across sessions
  and is the kind of thing users will absolutely want to keep when they go to
  the trouble of typing it.
- **Why no `schemaVersion` bump.** `string?` defaulting to `null` is a strict
  additive JSON change — old profiles deserialize cleanly with `Label = null`,
  and new profiles with no label set don't write the property out at all
  (System.Text.Json's default behavior). Persistence roundtrip tests still pass.
- **Why `GetKeyNameTextW` for keys, not a hardcoded scancode→name table.**
  Locale-aware: UK / DE / etc. keyboards render the right name for non-letter
  keys. The hardcoded table in `KeyCaptureBox.MapScancode` is for the reverse
  direction (WPF Key → scancode for capture) and only covers ~60 keys; that's
  the wrong shape for display lookup. Falls back to `Sc:XX` if the API returns
  empty so the cell never goes blank.
- **Why keep the header `Add binding` / `Edit` / `Delete` buttons instead of
  moving Edit/Remove inline like Widgets.** User explicitly preferred the
  cleaner row layout (just data, no controls per row) over the Widgets
  pattern. Row click + dedicated header buttons gives both quick-access and
  keyboard-only paths without crowding rows.
- **Why a write-through delegate for `Enabled` instead of two-way binding to
  the underlying `Binding`.** The `Binding` model is an immutable record —
  toggling `Enabled` requires a `with` expression and an index swap in the
  list, plus an autosave + rollback on failure. The delegate keeps that
  discipline in MainWindow code-behind and mirrors how Widgets handles
  `Visible`. `BindingRowViewModel` exposes `SetEnabledSilently` for the
  rollback path.

## Files touched

- [src/Mouse2Joy.Persistence/Models/Binding.cs](../../src/Mouse2Joy.Persistence/Models/Binding.cs)
  — added `string? Label` field.
- [src/Mouse2Joy.UI/ViewModels/BindingDisplay.cs](../../src/Mouse2Joy.UI/ViewModels/BindingDisplay.cs)
  — new file. Static `FormatSource` / `FormatTarget` / `FormatAuto` plus the
  `GetKeyNameTextW` P/Invoke.
- [src/Mouse2Joy.UI/ViewModels/BindingRowViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/BindingRowViewModel.cs)
  — new file. Mirrors `WidgetRowViewModel`'s structure: read-only display
  fields plus a single `[ObservableProperty] Enabled` with a write-through
  delegate.
- [src/Mouse2Joy.UI/Views/MainWindow.xaml](../../src/Mouse2Joy.UI/Views/MainWindow.xaml)
  — Profiles tab right-pane row count grew from 7 to 8; `DataGrid` replaced
  with header `Border` + `ListView` + `DataTemplate`. Column widths chosen to
  match Widgets (`60` for the leftmost toggle, `*` for Label, `200` each for
  Source and Target).
- [src/Mouse2Joy.UI/Views/MainWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/MainWindow.xaml.cs)
  — `_bindingRows` ObservableCollection, `RebuildBindingRows()`,
  `OnBindingRowClicked`, `EditBindingById`, `OnRowEnabledChanged` write-through
  with rollback. `OnAddBinding` / `OnEditBinding` / `OnDeleteBinding` updated
  to operate on row VMs and call `RebuildBindingRows` after each mutation.
  `MainViewModel.PropertyChanged` is now subscribed so a profile switch
  triggers the rebuild.
- [src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml](../../src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml)
  — added a `LabelTb` row at the top of the dialog with a sectioned tooltip
  (Description + Typical) explaining the auto-label fallback.
- [src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml.cs)
  — `LoadFrom` populates `LabelTb`; `OnOk` writes a trimmed non-empty label
  back into the result `Binding`.

## Follow-ups

- **No unit tests for `BindingDisplay` yet.** It's pure (modulo the P/Invoke,
  which is fine to skip in tests since `GetKeyNameTextW` failure already has
  a fallback path). Worth adding a small test project later if the formatter
  grows more cases.
- **`DataGrid` / `BindingsGrid` references are gone**, but if any later work
  reintroduces a list of bindings somewhere else, prefer the `ListView` +
  custom header pattern over `DataGrid` to stay consistent with the rest of
  the UI.
- **Label is not surfaced in the editor's window title.** Could show
  `Edit Binding — "{label}"` once the field is in widespread use; deferred
  to keep this change tight.
