# Overlay Widgets Table Styling

## Context
The Overlay tab's widgets table rendered each row as a card-like block — off-white background (`#FAFAFA`), a full 1px border on all four sides, and a 6px vertical gap between rows. This produced a heavy, "boxed" look that didn't match the user's preference for a flat, dense list. The "Edit" and "Remove" column headers also added visual noise — the buttons in those columns are self-describing.

## What changed
- Per-row background removed (was `#FAFAFA`, now `Transparent`).
- Vertical inter-row gap removed (`Margin` was `0,3,0,3`, now `0`).
- Per-row border reduced from a full 1px frame to a 1px bottom-only divider in `#E0E0E0` — rows now butt against each other separated only by a hairline.
- Header cells for the Edit and Remove columns are now blank; only `Visible`, `Label`, `Type`, and `Parent` carry text. Column widths are unchanged so header and data row layouts stay aligned.
- Outer ListView frame (`BorderThickness="1,0,1,1"`, `#D0D0D0`) is intentionally kept — it still visually ties the table to the header row above it.
- All column headers are horizontally centered (`Visible`, `Label`, `Type`, `Parent`; the `Edit` and `Remove` header cells are blank).
- Data cells are centered for the fixed-width columns (`Visible` checkbox, `Edit…` button, `Remove` button, `Type` text, `Parent` text). The `Label` data cell stays **left-aligned** even though its header is centered, because it's the free-text growable column — centering the value would cause symmetric ellipsis trimming on long widget names.

## Key decisions
- **Bottom-only border on the row itself, not a separator element.** Adding a `Separator` or a 1px `Border` inside the `DataTemplate` would have worked but adds an extra visual element that needs its own margin tuning. Setting `BorderThickness="0,0,0,1"` on the `ListViewItem` style is the simplest mechanism and keeps the existing click-handling and layout untouched.
- **Inner Grid `Margin="6,6"` kept.** With the outer per-row margin gone, that inner padding is the only vertical breathing room between the divider and the row's text/buttons. Removing it would make dividers visually collide with text.
- **Outer ListView border kept (not removed).** The user explicitly chose to retain the outer frame so the table stays visually contained under its header. The last row's bottom divider (`#E0E0E0`) sits just above the ListView's bottom border (`#D0D0D0`); they're different shades so the doubling reads as intentional rather than as a glitch.
- **No changes to default WPF `ListViewItem` selection/hover visuals.** The locally-set `Background="Transparent"` suppresses the default trigger paint, which matches existing behaviour — there are no `IsSelected` styling needs in this read-only-ish list.

## Files touched
- [src/Mouse2Joy.UI/Views/MainWindow.xaml](../../src/Mouse2Joy.UI/Views/MainWindow.xaml) — header cell labels removed for columns 1 and 2; `ListViewItem` style switched to transparent background, zero margin, and bottom-only border.

Deliberately unchanged:
- [src/Mouse2Joy.UI/ViewModels/WidgetRowViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/WidgetRowViewModel.cs) — no data shape change.
- [src/Mouse2Joy.UI/Views/MainWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/MainWindow.xaml.cs) — row click handler unaffected; the buttons still mark events handled to suppress the row click.
- [src/Mouse2Joy.App/App.xaml](../../src/Mouse2Joy.App/App.xaml) — no global styles touched.

## Follow-ups
None. Self-contained visual tweak.
