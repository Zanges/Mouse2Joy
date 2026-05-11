# Segmented Response Curve

## Context

The existing `ResponseCurveModifier` applies a power curve (`sign(x) * |x|^Exponent`) across the **entire** input range. That's the right tool when you want to reshape the full response, but it's the wrong tool when the user only wants to reshape *part* of the range — for example, keep the first 30% of input linear (for fine, low-deflection precision) and only apply an exponential curve to the remaining 70% (for more aggressive far-deflection response). The user asked for a modifier that captures exactly that "curve only this segment" idea while leaving the other segment as linear passthrough.

This modifier ships as an addition to the catalog; the existing `ResponseCurveModifier` is unchanged.

## What changed

- New `SegmentedCurveRegion` enum (`AboveThreshold`, `BelowThreshold`) and `SegmentedResponseCurveModifier` record in [Modifier.cs](../../src/Mouse2Joy.Persistence/Models/Modifier.cs). JSON discriminator: `segmentedResponseCurve`. Default: `Threshold = 0.3`, `Exponent = 2.0`, `Region = AboveThreshold`.
- `ModifierTypes.GetIO` declares it as Scalar→Scalar; `GetDisplayName` returns `"Segmented Response Curve (AboveThreshold)"` / `"... (BelowThreshold)"` so multiple instances in a chain are distinguishable at a glance (same pattern as `StickDynamicsModifier` and `MultiTapModifier`).
- New `SegmentedResponseCurveEvaluator` in the Engine, dispatched from `ChainBuilder.BuildEvaluator`.
- UI: catalog entry directly after "Response Curve"; `SegmentedResponseCurveProxy` with `Threshold` / `Exponent` / `Region` two-way bindings; `BuildProxy` factory case in `BindingEditorViewModel`; XAML data template with a region dropdown, threshold slider, exponent slider, and an explanatory hint line.

## Key decisions

- **New modifier rather than extending `ResponseCurveModifier`.** Adding `Threshold` and `Region` fields to the existing record would muddy its semantics (a "pure curve" suddenly has a threshold and region mode) and require thinking about backward-compatible defaults across every existing serialized profile. A new modifier keeps both shapes simple: `ResponseCurve` is a one-parameter pure curve; `SegmentedResponseCurve` is the three-parameter segmented variant. Profiles without it deserialize unchanged.

- **The curved segment is remapped to its own [0, 1] sub-range — continuity is mandatory.** Applying `|x|^n` raw above a threshold would create a discontinuous output at the boundary (output jumps from `t` to `t^n`) unless `n == 1`. That's almost never what someone configuring a "curve only the upper part" control expects, and a discontinuity in stick output translates directly into a feel-bad snap during play. So the curved segment is treated as its own [0, 1] sub-range: in `AboveThreshold` mode, `u = (a - t) / (1 - t)`, `v = u^n`, `out = t + v * (1 - t)`. Both segments meet smoothly at `a == t`. `BelowThreshold` mode is the symmetric remap on the lower segment.

- **One modifier with a `Region` enum vs. two separate modifiers.** The math for the two directions is symmetric, and a single configurable modifier costs one extra enum field and one extra UI control. Two modifiers would have doubled the catalog noise for what's clearly one concept ("curve only one segment").

- **Threshold is clamped strictly inside `(1e-6, 1 - 1e-6)` inside the evaluator only, not on the record.** This prevents divide-by-zero at the boundary while letting the user's persisted value round-trip through JSON unchanged. Same philosophy as `ResponseCurveEvaluator`'s `Exponent <= 0 → identity` guard: defensive math lives in the evaluator, not in the data record.

- **Defaults: `Threshold = 0.3`, `Exponent = 2.0`, `Region = AboveThreshold`.** Matches the example shape the user described ("first 30% linear, then exponential"). `Exponent = 2.0` gives a visibly non-linear shape out of the box so the modifier does something obvious when added with defaults — same spirit as `RampUp.Default = 0.5` and `Smoothing.Default = 0.05` picking a non-trivial value.

- **Display name includes the region.** "Segmented Response Curve (AboveThreshold)" is verbose but worth it: in a long chain, two instances of the same modifier with different regions need to be distinguishable in the chain list without expanding the card. Follows the existing convention used by `StickDynamicsModifier` and `MultiTapModifier`.

- **No `TooltipContent` on the new template.** The existing modifier param templates use a plain italic `TextBlock` hint at the bottom, not the structured tooltip system from [TOOLTIP_AUTO_WRAP.md](TOOLTIP_AUTO_WRAP.md) (that system is for surfaces *outside* the param panel). Match the established style to keep visual consistency across the modifier editor.

## Files touched

- [src/Mouse2Joy.Persistence/Models/Modifier.cs](../../src/Mouse2Joy.Persistence/Models/Modifier.cs) — added `SegmentedCurveRegion` enum, `SegmentedResponseCurveModifier` record, and the `[JsonDerivedType]` attribute for the polymorphic discriminator.
- [src/Mouse2Joy.Persistence/ModifierTypes.cs](../../src/Mouse2Joy.Persistence/ModifierTypes.cs) — `GetIO` + `GetDisplayName` cases.
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/SegmentedResponseCurveEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/SegmentedResponseCurveEvaluator.cs) — new evaluator, stateless, mirrors `ResponseCurveEvaluator` structure.
- [src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs](../../src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs) — factory dispatch.
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierCatalog.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierCatalog.cs) — catalog entry.
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs) — `SegmentedResponseCurveProxy`.
- [src/Mouse2Joy.UI/ViewModels/Editor/BindingEditorViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/BindingEditorViewModel.cs) — `BuildProxy` factory case.
- [src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml](../../src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml) — DataTemplate with region combo + threshold slider + exponent slider + hint.

Deliberately unchanged:
- [ResponseCurveModifier / ResponseCurveEvaluator](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/ResponseCurveEvaluator.cs) — kept simple and untouched per the "new modifier rather than extend" decision above.

## Tests

16 unit tests added in [tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs](../../tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs) (`SegmentedResponseCurveEvaluatorTests`), covering both regions' linear/curved segments, continuity at the threshold, endpoint preservation at ±1, sign symmetry, `NaN → 0`, `Exponent <= 0 → identity`, input magnitude clamping, threshold guard at both boundaries, `Reset()` being a no-op (stateless), and `Config` exposure. Mirror this coverage shape for any future modifier evaluator added to the catalog.

## Follow-ups

- **Visual preview of the curve shape.** The editor doesn't currently render a curve preview for `ResponseCurve` either; if a preview is ever added, this modifier should render two segments — linear + curved — drawn from the same `Threshold` / `Exponent` / `Region` values.
