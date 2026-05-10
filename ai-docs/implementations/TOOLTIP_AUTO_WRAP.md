# App-wide auto-wrapping & sectioned tooltips

## Context

After [STICK_MODES_UPDATE.md](STICK_MODES_UPDATE.md) added multi-sentence guidance to the Edit Binding window's stick-model parameter tooltips, the long strings rendered as a single line spanning roughly half the screen — WPF's default `ToolTip` has no `MaxWidth` and no text wrapping.

A first pass added app-wide wrapping. The wrapped tooltips were readable, but a wall of prose still buried the most-needed answer ("what value should I put here?") at the end of the paragraph. A second pass added optional structured sectioning so the typical-value range can sit at the top, closest to the input field being hovered, with subtle styling to set it apart from the descriptive prose.

Two paths now coexist:

1. **Plain-string tooltips** (`ToolTip="some text"` or `.ToolTip = "some text"`) — get wrapping, nothing else. Right for short single-thought tooltips.
2. **`TooltipContent` tooltips** — three optional sections (Typical, Description, Advice) rendered in that visual order, with the Typical line italic + dim. Right for tooltips that combine "what it does," "when to change it," and a typical-range hint.

## What changed

- [TooltipContent.cs](../../src/Mouse2Joy.UI/Tooltips/TooltipContent.cs) — new POCO with `Description` (required), `Advice` (optional), `Typical` (optional). Constructable from C# (positional/named args) and from XAML (property setters via `<tt:TooltipContent .../>`).
- [TooltipTemplateSelector.cs](../../src/Mouse2Joy.UI/Tooltips/TooltipTemplateSelector.cs) — `DataTemplateSelector` that picks the wrapping plain-text template for strings and falls through to WPF's `DataType`-keyed lookup for `TooltipContent` (which then matches the typed `DataTemplate` in `App.xaml`).
- [App.xaml](../../src/Mouse2Joy.App/App.xaml) —
  - `<DataTemplate DataType="{x:Type tt:TooltipContent}">` renders the three sections via a `StackPanel`. Typical is rendered first as `Typical {0}` (italic, `Opacity="0.7"`) with a `DataTrigger` that collapses it when null. Description is always rendered. Advice is rendered last with the same null-collapse trigger.
  - `<DataTemplate x:Key="StringTooltipTemplate">` is the plain-string wrapping template (`<TextBlock Text="{Binding}" TextWrapping="Wrap"/>`).
  - The implicit `Style TargetType="ToolTip"` sets `MaxWidth="320"` and `ContentTemplateSelector="{StaticResource TooltipTemplateSelector}"`.
- [BindingEditorWindow.xaml](../../src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml) — Suppress real input checkbox tooltip moved to a `Window.Resources` `TooltipContent` with Description + Advice (defaults note).
- [BindingEditorWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml.cs) — six stick-model parameter tooltips converted to `TooltipContent`. The four with a typical-range get a `Typical` field. The two scale-parameter tooltips (Velocity's `Counts/sec → full`, Accumulator's `Counts → full`, Persistent's `Counts → full`) also get an `Advice` field carrying the shared "leave at default, tune Sensitivity instead" guidance, extracted to a `const string ScaleAdvice` to keep the three usages consistent.
- [MainWindow.xaml](../../src/Mouse2Joy.UI/Views/MainWindow.xaml) — the duplicated "Tick rate Hz" tooltip (used twice, once on the label and once on the textbox) consolidated into a single `Window.Resources` `TooltipContent` with Description + Typical, both call sites now reference it via `{StaticResource TickRateTooltip}`.

Tooltips that remained plain strings: the three short single-thought tooltips in `MainWindow.xaml` (start-minimized, close-to-tray, auto-start). They benefit from wrapping but don't need sectioning.

## Key decisions

- **Implicit style, not a helper function.** The user originally suggested a "utility we use for every tooltip," but a `Tooltips.Wrap()` helper would have to be remembered at every call site and would require touching every existing assignment to opt in. An app-wide implicit style is the idiomatic WPF answer to "I want behavior X on every instance of control Y" — zero per-call-site friction, can't be forgotten on new code, and there's no second path to keep in sync.
- **Both `MaxWidth` *and* a wrapping `ContentTemplate` are required.** Load-bearing detail: `MaxWidth` alone caps the frame but doesn't wrap text — the default content presenter lays a long string out on a single line. The wrapping `TextBlock` inside the template is what actually breaks lines.
- **`ContentTemplateSelector`, not a single template.** Once `TooltipContent` was introduced, a single `ContentTemplate` would clobber WPF's `DataType`-keyed lookup, leaving `TooltipContent` instances rendered as `.ToString()` text. The selector returns the wrapping template for strings and `null` for everything else (so WPF falls back to the `DataType`-keyed `TooltipContent` template). One small C# class, no per-call-site code.
- **POCO, not `record`.** Originally written as a `record TooltipContent(string Description, string? Advice, string? Typical)`. WPF's XAML loader couldn't construct it via property setters because records' positional parameters become init-only properties without parameterless constructors. Switched to a regular class with public mutable properties + a convenience positional constructor — usable from both XAML (`<tt:TooltipContent Description="..." Typical="..."/>`) and C# (`new TooltipContent(description: "...", typical: "...")`).
- **Typical at the top, not the bottom.** The user's call. Reasoning: the typical-value range is the most-needed-on-the-spot answer ("what value should I put here?"), and the tooltip pops up near the input field — putting Typical first means it lands closest to where the user's eye already is. Description and Advice follow for those who want more.
- **Italic + 70% opacity for Typical, no other styling.** Sets the section apart visually as metadata without competing with the descriptive prose. Description and Advice share the same default font weight and color. The user explicitly chose to apply distinct styling only to the Typical line — keeping the rest visually uniform avoids the "everything is emphasized so nothing is" problem.
- **Vertical spacing via `Margin`, not blank `TextBlock`s.** The Description gets `Margin="0,4,0,0"`, Advice gets `Margin="0,8,0,0"` — a slightly larger gap before Advice signals a topic shift ("here's a heads-up about when to change this"). Cleaner than empty filler elements and the spacing scales with the system font.
- **Plain-string fallback retained.** Short tooltips don't gain anything from `TooltipContent` — they have no Typical or Advice to surface. Forcing every tooltip through the structured path would be ceremony for no benefit. The selector lets both coexist.

## Files touched

- New: [TooltipContent.cs](../../src/Mouse2Joy.UI/Tooltips/TooltipContent.cs)
- New: [TooltipTemplateSelector.cs](../../src/Mouse2Joy.UI/Tooltips/TooltipTemplateSelector.cs)
- Modified: [App.xaml](../../src/Mouse2Joy.App/App.xaml) — both templates, selector, implicit style.
- Modified: [BindingEditorWindow.xaml](../../src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml) — Suppress checkbox tooltip via window resource.
- Modified: [BindingEditorWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml.cs) — six stick-model tooltips converted to `TooltipContent`.
- Modified: [MainWindow.xaml](../../src/Mouse2Joy.UI/Views/MainWindow.xaml) — Tick rate tooltip consolidated to a window resource.

Deliberately unchanged:

- The three short single-thought tooltips in `MainWindow.xaml` (start-minimized, close-to-tray, auto-start) — sectioning would add ceremony without helping readability.

## Follow-ups

- If a future tooltip needs to be unbounded (e.g. a tooltip displaying a code snippet that should not wrap), set `MaxWidth="{x:Static sys:Double.PositiveInfinity}"` on that specific tooltip via local style or attribute. Hasn't come up.
- If the `Description` itself needs internal paragraph breaks for very long tooltips, the cleanest extension is to either split into multiple `Description` paragraphs (e.g., a `Description1` / `Description2` field pair, or a `Description` array) or treat `\n\n` inside `Description` as a paragraph break. Defer until a tooltip actually needs it.
- If the app switches to a custom WPF theme that overrides `ToolTip`, this style and templates would need to merge into that theme's resource dictionary rather than living in `App.xaml`.
