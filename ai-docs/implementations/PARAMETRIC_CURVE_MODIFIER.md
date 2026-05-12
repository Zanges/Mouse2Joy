# Parametric Curve modifier

## Context

The user spent several iterations on `SegmentedResponseCurveModifier` trying to get a stick response curve that matched a hand-drawn sketch. The Segmented Response Curve's conceptual model — "linear segment + curved segment meeting at a threshold" — wasn't right: the user's sketch was a smooth S-curve shape that didn't have a clean linear/curved boundary. The conclusion: a different *kind* of modifier was needed, one where the user defines the curve shape *directly* via control points rather than picking from a parameterized family of shapes.

The aspirational goal stated by the user: "this modifier should allow pretty much any shape of response curve a user wants to achieve." Two modifiers were planned to deliver this:

- **Phase 1 (this implementation): `ParametricCurveModifier`** with a numeric editor — per-point X/Y sliders and textboxes.
- **Phase 2 (deferred): interactive canvas curve editor** — same underlying math but a drag-the-points canvas UI.

Phase 1 ships with sliders only. The live curve preview at the bottom of the binding editor (which already renders any modifier chain) shows the resulting curve visually as the user adjusts sliders.

## What changed

- New `CurvePoint` record (X, Y).
- New `ParametricCurveModifier` record: holds `IReadOnlyList<CurvePoint> Points` and `bool Symmetric`. JSON discriminator `"parametricCurve"`. Default: 3-point identity curve (0,0), (0.5, 0.5), (1, 1), symmetric mode.
- New `ParametricCurveEvaluator` implementing monotone cubic Hermite interpolation (Fritsch-Carlson). Sorts points by X, snaps near-duplicate X values to avoid division-by-zero, computes monotonicity-preserving tangents at construction, then per-tick does segment lookup + Hermite basis evaluation.
- New `ParametricCurveProxy` with an `ObservableCollection<CurvePointRow>` for the row repeater. Row edits feed back to the modifier via per-row `INotifyPropertyChanged` subscribed by the proxy. A `_suppressRowEvents` flag prevents feedback loops during sync-from-mod.
- New XAML data template with a "Symmetric" checkbox, "Number of points" slider (2–7), and an `ItemsControl` row repeater with X-slider, X-textbox, Y-slider, Y-textbox per point.
- Catalog entry placed after "Segmented Response Curve."
- Schema bumped 5 → 6 with [V5ToV6.cs](../../src/Mouse2Joy.Persistence/Migration/V5ToV6.cs) (version-stamp migration; no existing profiles can have the new kind).
- 19 evaluator unit tests + 4 V5→V6 migration tests + an entry in `ModifierSerializationTests.Every_modifier_kind_roundtrips_with_kind_discriminator`.

## Key decisions

- **Monotone cubic Hermite (Fritsch-Carlson) interpolation.** Three properties matter for a response curve: smoothness (C¹), exact interpolation (passes through user's points), and monotonicity (output never decreases as input increases). Plain Catmull-Rom gives the first two but not monotonicity — it can overshoot when adjacent segments have very different slopes. Natural cubic splines give C² smoothness but no monotonicity guarantee and require solving a tridiagonal system globally. Fritsch-Carlson is the sweet spot: cheap local computation, C¹ smooth, monotonicity guaranteed. ~30 lines of code. The algorithm is from [Fritsch & Carlson 1980](https://en.wikipedia.org/wiki/Monotone_cubic_interpolation), well-documented and widely used.

- **List-field equality requires explicit override.** The Modifier base class doc says "value-equality reliable for the engine's 'preserve state when chain unchanged' cache eviction." Records with `IReadOnlyList<T>` fields get reference-equality for the list field by default — two modifiers with identical-content but distinct lists would compare unequal, triggering unnecessary evaluator rebuilds. Overriding `Equals(ParametricCurveModifier?)` and `GetHashCode()` to compare list contents by sequence preserves the cache invariant. **This is the first modifier with a list field — future modifiers with list fields should follow this pattern.**

- **Sort and snap-X inside the evaluator, not the data record.** The user might edit X values in any order (the UI is row-by-row, not X-sorted). The data record stores points exactly as written. The evaluator defensively sorts at construction. Near-duplicate X (within 1e-6) is snapped apart to prevent divide-by-zero in the Fritsch-Carlson math. This keeps the data faithful to user input while making the evaluator robust.

- **Tangents computed once at construction, not per-tick.** The evaluator is rebuilt whenever the modifier record changes (which is when the user edits a point). So `_tangents` is essentially a derived cache. Per-tick cost is just segment lookup + Hermite basis = constant time, no allocation.

- **Symmetric mode = take |x|, evaluate, apply sign(x).** Cleanest semantic for a stick axis where left and right should feel identical. The user defines the curve once in [0, 1] and the negative side mirrors automatically.

- **Linear extrapolation outside the user's X range.** Only relevant in full-range (non-symmetric) mode where the user might place all points in a sub-range. Inside the user's range we interpolate; outside we extrapolate at the endpoint tangent. Alternatives (clamp at endpoint, return endpoint Y) would have created surprising "dead zones" outside the user's defined range.

- **At least 2 points required; <2 falls back to passthrough.** Defensive guard. The UI shouldn't let this happen (min point count = 2), but the evaluator handles it without throwing.

- **Resampling on point-count change uses linear interpolation** (in the proxy), not Fritsch-Carlson. The proxy lives in `Mouse2Joy.UI` which doesn't reference the Engine, and importing the Fritsch-Carlson math into UI would invert the layering. Linear is fine for resampling — the result is close enough to the original shape that the user sees a smooth grow/shrink, and they can fine-tune from there.

- **Two modifiers (Phase 1 numeric + Phase 2 canvas), not one with a UI toggle.** Per user request. Different mental models: numeric for precise/reproducible setup, canvas for exploratory dialing. Both share the same record type and evaluator; only the proxy + XAML differ. Phase 2 will add a new modifier kind (e.g. `CurveEditorModifier`) with the same `IReadOnlyList<CurvePoint>` data shape so users could potentially convert between them. Phase 2 is a separate session.

- **Schema bump for a new modifier kind.** Per the project's bump-on-any-wire-format-change rule. V5ToV6 is a version-stamp migration; no existing profiles contain `parametricCurve` so there's no content to rewrite.

## Files touched

- [src/Mouse2Joy.Persistence/Models/Modifier.cs](../../src/Mouse2Joy.Persistence/Models/Modifier.cs) — `CurvePoint` record, `ParametricCurveModifier` record (with `Equals`/`GetHashCode` overrides), `[JsonDerivedType]` for `"parametricCurve"`.
- [src/Mouse2Joy.Persistence/Models/Profile.cs](../../src/Mouse2Joy.Persistence/Models/Profile.cs) — `CurrentSchemaVersion = 6`.
- [src/Mouse2Joy.Persistence/ModifierTypes.cs](../../src/Mouse2Joy.Persistence/ModifierTypes.cs) — `GetIO` (Scalar → Scalar) and `GetDisplayName` cases.
- [src/Mouse2Joy.Persistence/Migration/V5ToV6.cs](../../src/Mouse2Joy.Persistence/Migration/V5ToV6.cs) — **new file**, version-stamp migration.
- [src/Mouse2Joy.Persistence/ProfileStore.cs](../../src/Mouse2Joy.Persistence/ProfileStore.cs) — chain V5→V6.
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/ParametricCurveEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/ParametricCurveEvaluator.cs) — **new file**, Fritsch-Carlson math.
- [src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs](../../src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs) — factory dispatch.
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs) — `CurvePointRow` + `ParametricCurveProxy`.
- [src/Mouse2Joy.UI/ViewModels/Editor/BindingEditorViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/BindingEditorViewModel.cs) — `BuildProxy` case.
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierCatalog.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierCatalog.cs) — catalog entry.
- [src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml](../../src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml) — DataTemplate with point row repeater.
- [tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs](../../tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs) — `ParametricCurveEvaluatorTests` class (19 tests).
- [tests/Mouse2Joy.Persistence.Tests/V5ToV6MigrationTests.cs](../../tests/Mouse2Joy.Persistence.Tests/V5ToV6MigrationTests.cs) — **new file**.
- [tests/Mouse2Joy.Persistence.Tests/ModifierSerializationTests.cs](../../tests/Mouse2Joy.Persistence.Tests/ModifierSerializationTests.cs) — added the new modifier to the round-trip roster.

## Phase 2: Curve Editor (interactive canvas modifier)

After Phase 1 shipped, the user accepted the numeric editor as good-enough but wanted the originally-planned canvas editor delivered as a *separate* modifier kind. Phase 2 adds `CurveEditorModifier` alongside `ParametricCurveModifier`. Both implement a shared `ICurveData` interface so the math is reused with zero duplication.

### What changed (Phase 2)

- New `ICurveData` interface in `Mouse2Joy.Persistence.Models`. Both modifiers implement it.
- New `CurveEditorModifier` record with the same data shape (`IReadOnlyList<CurvePoint> Points`, `bool Symmetric`) as `ParametricCurveModifier`. JSON discriminator `"curveEditor"`. Catalog default: 3-point identity, symmetric.
- `ParametricCurveEvaluator` generalized to take `(Modifier, ICurveData)`. Convenience constructors for both concrete modifier types preserve the existing call sites and tests.
- `ChainBuilder` dispatches `CurveEditorModifier` to the same evaluator (`new ParametricCurveEvaluator(ce)`).
- New `CurveEditorCanvas` — a `FrameworkElement` with mouse handling. Same rendering style as `ChainPreviewControl`. Two-way `Points` dependency property, one-way `Symmetric`. Hit-tests in screen-pixel space (12px radius) so points stay easy to grab regardless of canvas size. Renders the curve via a transient `ParametricCurveEvaluator` so the canvas's depiction matches runtime evaluation exactly.
- New `CurveEditorWindow` — a modal popout (`Window` with `ShowDialog`). 640×560 default size, resizable. Contains the canvas, a Symmetric checkbox, a point-count slider, an info hint, and a Close button. The window's view-model is a `CurveEditorWindowViewModel` that wraps the `ModifierCardViewModel` and writes back on every edit (`Card.Update(Mod with ...)`).
- `CurveEditorProxy` (param panel): just an "Edit Curve..." button + hint text. Clicking the button opens the window. A small inline `RelayCommand` was added to `ModifierParamProxies.cs` to bind the button click.
- Schema bumped 6 → 7 with `V6ToV7.cs` (version-stamp migration).
- 4 new evaluator tests (shared math, default, equality, ICurveData interface), 4 migration tests, plus the new modifier added to the serialization round-trip roster.

### Interactions on the canvas

| Action | Effect |
|---|---|
| Left-drag a point | Move it in 2D. Snapped to 0.05 grid if Shift is held. Points auto-reorder by X after each move so the curve stays sorted. |
| Left-click empty area | Insert a new point at the click position; immediately begin dragging it so the user can place + position in one motion. Capped at 7 total points. |
| Right-click a point | Remove it. Capped at minimum 2 points; right-clicking when at the floor does nothing. |
| Hold Shift while interacting | Snap X and Y values to nearest 0.05. |

Hovering a point highlights it; the dragged point gets a different highlight color and slightly larger radius for visual feedback.

### Key decisions (Phase 2)

- **Separate modifier kind, not "two views of one modifier."** The user explicitly asked for two separate modifiers. Two reasons this works well: (1) the catalog shows two entries that make it clear what kind of editing experience to expect, and (2) the modifier's `$kind` preserves authoring intent across save/load (a profile saved with `curveEditor` reloads as `curveEditor`, not as `parametricCurve`). The data shape is identical between the two — a future "convert between" action could swap them in place if anyone asks.

- **`ICurveData` interface for math sharing.** Single evaluator implementation, two modifier kinds. The evaluator takes the interface, not the concrete type. Convenience constructors `(ParametricCurveModifier)` and `(CurveEditorModifier)` preserve the existing API so all existing tests pass unchanged.

- **Modal popout window, not inline canvas.** The param panel inside the binding editor is narrow (~half the editor width). A useful canvas needs ~400×400 pixels. The popout gives that space without restructuring the binding editor. Modal-ness is appropriate because the user is focused on one specific modifier while editing the curve; non-modal would invite confusion about "which curve am I editing?"

- **Edit commit happens on every interaction**, not on close. Consistent with the rest of the binding editor's commit-on-every-edit pattern (every slider drag writes a new modifier to the card immediately). The user sees the live chain preview at the bottom of the binding editor update as they drag points. The popout has no Cancel button — to abandon changes, use the binding editor window's Cancel.

- **Drag-past-X auto-reorders.** When the user drags point #2 past point #3's X, points re-sort on every drag tick. The "currently dragged" index updates to track the same logical point through reorders. Visually smooth, matches parametric-EQ tools.

- **Hit-test in screen-pixel space** (12px radius) rather than curve-coord space. Makes point selection feel consistent regardless of canvas size or zoom (relevant for resizable popout).

- **Canvas renders via the actual evaluator**, not a separate "preview" math path. The canvas constructs a transient `CurveEditorModifier` from its current `Points` + `Symmetric` and runs `ParametricCurveEvaluator` to plot. Guarantees the canvas depiction matches runtime behavior exactly.

- **No automated WPF interaction tests.** WPF mouse-event automation requires UI thread setup and is generally tested manually. The math is already covered through the shared evaluator; the canvas is interaction routing on top. Acceptable per project test conventions.

- **Linear (not Fritsch-Carlson) resampling on point-count change inside the popout.** The popout view-model lives in the UI assembly, which doesn't reference the Engine. Importing Fritsch-Carlson would create a layering inversion. Linear resampling is fine — the user sees the result and can adjust. Same approach as `ParametricCurveProxy`.

### Files touched (Phase 2)

- `src/Mouse2Joy.Persistence/Models/ICurveData.cs` — **new file**.
- [src/Mouse2Joy.Persistence/Models/Modifier.cs](../../src/Mouse2Joy.Persistence/Models/Modifier.cs) — `CurveEditorModifier` record, `[JsonDerivedType]`, `ICurveData` implementation on both modifiers.
- [src/Mouse2Joy.Persistence/Models/Profile.cs](../../src/Mouse2Joy.Persistence/Models/Profile.cs) — `CurrentSchemaVersion = 7`.
- [src/Mouse2Joy.Persistence/ModifierTypes.cs](../../src/Mouse2Joy.Persistence/ModifierTypes.cs) — `GetIO` + `GetDisplayName` cases.
- [src/Mouse2Joy.Persistence/Migration/V6ToV7.cs](../../src/Mouse2Joy.Persistence/Migration/V6ToV7.cs) — **new file**.
- [src/Mouse2Joy.Persistence/ProfileStore.cs](../../src/Mouse2Joy.Persistence/ProfileStore.cs) — chain V6→V7.
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/ParametricCurveEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/ParametricCurveEvaluator.cs) — generalized to take `(Modifier, ICurveData)` with convenience constructors.
- [src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs](../../src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs) — dispatch `CurveEditorModifier`.
- [src/Mouse2Joy.UI/Controls/CurveEditorCanvas.cs](../../src/Mouse2Joy.UI/Controls/CurveEditorCanvas.cs) — **new file**, interactive canvas.
- `src/Mouse2Joy.UI/Views/Editor/CurveEditorWindow.xaml` + `.xaml.cs` — **new files**, popout window.
- [src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml](../../src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml) — `CurveEditorProxy` template (button + hint only).
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs) — `CurveEditorProxy` + a small inline `RelayCommand`.
- [src/Mouse2Joy.UI/ViewModels/Editor/BindingEditorViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/BindingEditorViewModel.cs) — `BuildProxy` case.
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierCatalog.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierCatalog.cs) — catalog entry.
- [tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs](../../tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs) — 4 new tests for the shared evaluator and ICurveData.
- `tests/Mouse2Joy.Persistence.Tests/V6ToV7MigrationTests.cs` — **new file**.
- [tests/Mouse2Joy.Persistence.Tests/ModifierSerializationTests.cs](../../tests/Mouse2Joy.Persistence.Tests/ModifierSerializationTests.cs) — added `CurveEditorModifier` to the round-trip roster.

## Follow-ups

- **Curve presets** ("Linear," "Soft S," "Aggressive," etc.) as a "+ Preset" dropdown that fills in points. Useful for first-time users.
- **Curve import/export** (paste JSON, share between profiles).
- **Convert between Parametric and Curve Editor**. A "Switch to Canvas Editor" / "Switch to Numeric Editor" button on each proxy that swaps the modifier kind in place (data is identical between them).
- **Double-click point to type exact values.** A precise-edit fallback inside the canvas for when sliders aren't accurate enough.
- **Symmetric mode UI: clamp X-slider range to [0, 1]** (Phase 1 row-repeater UI). Currently the X-slider in symmetric mode allows -1 to 1 even though the evaluator ignores the negative half. Doesn't cause bugs but is visually misleading.
- **Per-point delete buttons** in the Phase 1 row repeater. Currently the point count is changed only via the global slider, which redistributes X values evenly. A "delete this point" button would let the user remove a specific point while keeping others' X values intact.
- **Canvas keyboard interactions** (Tab through points, arrow keys to nudge, Delete to remove). Future polish.
