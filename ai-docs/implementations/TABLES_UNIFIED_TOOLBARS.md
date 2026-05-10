# Bindings & Widgets table polish

## Context

The bindings tab and overlay-widgets tab had grown apart visually and behaviourally:

- The bindings table had its action buttons in a toolbar above the table; the widgets table had per-row Edit/Remove buttons plus a "Reset to defaults" button the user no longer found useful.
- Both editors mentioned the auto-generated label only via tooltip text — there was no inline preview so the user couldn't see what label the row would actually show if they left the field blank.
- The widget editor's anchor-parent dropdown showed a 6-character GUID prefix when a parent had no user-set name, instead of the same `Type #N` auto-label the widget table uses.

This change unifies the two tabs: same toolbar layout, same auto-label preview pattern, same selection-gating model. It also adds Duplicate to both tables.

## What changed

- **Both editors** show the auto-generated label as italic, dim placeholder text inside the Label TextBox. The placeholder vanishes on first character typed and reappears when the field is cleared. It updates live as the user changes inputs that feed into the auto-label (Source/Target on bindings; Type on widgets).
- **Widget editor's anchor-parent dropdown** now uses the same `WidgetDisplay.ResolveDisplayName` helper as the widget table — unnamed widgets show as `Type` (or `Type #N` when ambiguous), never a raw GUID prefix.
- **Widgets table**: per-row Edit / Remove buttons removed; toolbar above the table now mirrors the bindings tab — `[Add widget] [Edit] [Delete] [Duplicate]`.
- **"Reset to defaults" button removed** from the widgets toolbar (and the corresponding `OnResetOverlay` handler deleted).
- **Both tables get a Duplicate button**, gated on a single row being selected.
- **Bindings table's Edit and Delete are now also gated** on a row being selected — matches the new Duplicate behaviour.
- Duplicate clones with a fresh Id, copies all other fields verbatim (no `(copy)` suffix), and immediately opens the editor on the clone. Cancelling the editor leaves the duplicate as-is.

## Key decisions

- **Placeholder is a reusable attached behaviour, not a custom control** — `controls:PlaceholderText.Text="…"` works on any `TextBox`. Implementation: an italic `TextBlock` hosted inside a one-shot `Adorner` attached to the textbox; toggled visible whenever the textbox text is empty. Hit-test is disabled so the placeholder never steals clicks. Focus does not toggle visibility — the hint stays while the field is empty so the user can still read the auto-label after clicking in.
- **Duplicate keeps the source's user-set Label as-is, no `(copy)` suffix.** If the source has no label, the clone also has no label and the table shows the auto-label for both rows. The editor opens immediately so the user can give the clone a unique name if they want one — that's the cheaper UX than mutating the label silently. Confirmed by user during planning.
- **Duplicate persists before opening the editor.** If the user cancels the editor, the duplicate remains in the list (positioned at the end, with a fresh Id). Persisting up-front means a Save failure rolls back cleanly via the same path the Add flow uses; mid-edit cancellation isn't a "did I really mean to duplicate?" moment.
- **`ResolveDisplayName` hoisted into `WidgetDisplay`**, mirroring the existing `BindingDisplay`. The widget table, the parent dropdown, and the editor's placeholder all share one helper now — they cannot drift.
- **Auto-label "#N" math counts the staged widget itself.** When the user is editing/adding a widget whose Type collides with a sibling, the placeholder shows the index the row will have *if the user keeps the type and leaves the label blank*. Achieved by appending a synthetic `WidgetConfig` (with the staged Type and Id) to `_siblings` before resolving.
- **`KeyBox` placeholder updates on `LostFocus` rather than on `CapturedKey` change.** Hooking the DependencyProperty would refresh on every keystroke during capture — overkill for a placeholder hint. LostFocus catches the user moving on, which is when the placeholder text becomes informative.
- **The selection-gating handlers are wired to `ListView.SelectionChanged`**, not to the row click handler, so click-on-empty-area-deselection also re-disables the buttons consistently.

## Files touched

- [src/Mouse2Joy.UI/Controls/PlaceholderText.cs](../../src/Mouse2Joy.UI/Controls/PlaceholderText.cs) *(new — attached behaviour)*
- [src/Mouse2Joy.UI/ViewModels/WidgetDisplay.cs](../../src/Mouse2Joy.UI/ViewModels/WidgetDisplay.cs) *(new — hoisted `ResolveDisplayName`)*
- [src/Mouse2Joy.UI/Views/MainWindow.xaml](../../src/Mouse2Joy.UI/Views/MainWindow.xaml) — bindings toolbar gains Duplicate; widgets tab restructured (toolbar above the table, header trimmed to 4 columns, per-row buttons removed); SelectionChanged wired on both tables
- [src/Mouse2Joy.UI/Views/MainWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/MainWindow.xaml.cs) — `OnDuplicateBinding`, `OnDuplicateWidget`, `OnBindingsSelectionChanged`, `OnWidgetsSelectionChanged`; `OnEditWidget`/`OnRemoveWidget` refactored as `OnEditWidgetSelected` / `OnDeleteWidgetSelected` reading from `SelectedItem`; `OnResetOverlay` deleted; `ResolveDisplayName` removed (callers now use `WidgetDisplay`)
- [src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml](../../src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml) — placeholder attached prop on `LabelTb`
- [src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml.cs) — `UpdateAutoLabelPlaceholder` recomputes the placeholder from current Source/Target selectors
- [src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml) — placeholder attached prop on `LabelTb`
- [src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs) — `ShortLabelFor` deleted; `PopulateParentCb` uses `WidgetDisplay.ResolveDisplayName`; `UpdateAutoLabelPlaceholder` called on init and on Type change

Deliberately unchanged: the per-row click handler on the widgets table is kept — clicking anywhere on a row still opens the editor. The toolbar buttons are an additional entry point, not a replacement.

## Follow-ups

- The Duplicate button does not handle multi-select (the tables aren't `MultiSelect`-enabled, so this is a non-issue today, but a "duplicate selected widgets" mass action would be a natural extension if multi-select is added).
- The placeholder behaviour could be reused on other forms (e.g. profile name) — the attached property is ready for it; just add the `controls:PlaceholderText.Text` attribute.
- "Reset to defaults" was removed without a replacement. If users ever need to recover the seed widget set, that flow would need to come back as a separate menu entry — not as a button next to the table.
