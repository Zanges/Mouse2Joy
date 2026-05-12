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

- **The curved segment is remapped to its own [0, 1] sub-range — value continuity is mandatory.** Applying `|x|^n` raw above a threshold would create a discontinuous *output* at the boundary (output jumps from `t` to `t^n`) unless `n == 1`. That's almost never what someone configuring a "curve only the upper part" control expects, and a value discontinuity in stick output translates directly into a feel-bad snap during play. So the curved segment is treated as its own [0, 1] sub-range: in `AboveThreshold` mode, `u = (a - t) / (1 - t)`, `v = u^n`, `out = t + v * (1 - t)`. Both segments meet on the value axis at `a == t`. `BelowThreshold` mode is the symmetric remap on the lower segment. *(Note: this preserves value-continuity but NOT slope-continuity — the latter is what produces a visible "kink" at the threshold. See the "Smooth transition styles" section below for the addressed-later fix.)*

- **One modifier with a `Region` enum vs. two separate modifiers.** The math for the two directions is symmetric, and a single configurable modifier costs one extra enum field and one extra UI control. Two modifiers would have doubled the catalog noise for what's clearly one concept ("curve only one segment").

- **Threshold is clamped strictly inside `(1e-6, 1 - 1e-6)` inside the evaluator only, not on the record.** This prevents divide-by-zero at the boundary while letting the user's persisted value round-trip through JSON unchanged. Same philosophy as `ResponseCurveEvaluator`'s `Exponent <= 0 → identity` guard: defensive math lives in the evaluator, not in the data record.

- **Defaults: `Threshold = 0.3`, `Exponent = 2.0`, `Region = AboveThreshold`.** Matches the example shape the user described ("first 30% linear, then exponential"). `Exponent = 2.0` gives a visibly non-linear shape out of the box so the modifier does something obvious when added with defaults — same spirit as `RampUp.Default = 0.5` and `Smoothing.Default = 0.05` picking a non-trivial value.

- **Display name includes the region.** "Segmented Response Curve (AboveThreshold)" is verbose but worth it: in a long chain, two instances of the same modifier with different regions need to be distinguishable in the chain list without expanding the card. Follows the existing convention used by `StickDynamicsModifier` and `MultiTapModifier`.

- **No `TooltipContent` on the new template.** The existing modifier param templates use a plain italic `TextBlock` hint at the bottom, not the structured tooltip system from [TOOLTIP_AUTO_WRAP.md](TOOLTIP_AUTO_WRAP.md) (that system is for surfaces *outside* the param panel). Match the established style to keep visual consistency across the modifier editor.

## Smooth transition styles (follow-up)

After the live curve-preview shipped, the user could *see* that the initial "Hard" math produced a visible **kink** at the threshold: output continuous, slope discontinuous (slope = 1 on the linear side, slope = 0 on the curved side for `Exponent > 1`). No parameter tuning could remove the kink because the formula structurally forces slope-discontinuity for `n ≠ 1`. The fix was to add alternative math.

### What changed

- New `SegmentedCurveTransitionStyle` enum: `Hard` / `SmoothStep` / `HermiteSpline`.
- `SegmentedResponseCurveModifier` gained a 4th constructor parameter `TransitionStyle = SegmentedCurveTransitionStyle.Hard` (so old JSON without the field deserializes with the original behavior — no silent upgrade).
- `Default` static factory updated to produce `HermiteSpline` instances, so the catalog's `+ Add Modifier → Segmented Response Curve` delivers smooth-by-default.
- `SegmentedResponseCurveEvaluator` refactored to dispatch on `TransitionStyle`. Hard math is preserved verbatim in a helper; SmoothStep and HermiteSpline are new.
- UI: a "Transition style" combo added to the XAML param template, below the existing "Region" combo. Hint text explains the meaning of Exponent depends on the style.
- `ModifierTypes.GetDisplayName` extended to include the style: `"Segmented Response Curve (Region, Style)"`.
- Schema version bumped 3 → 4 with a trivial [V3ToV4.cs](../../src/Mouse2Joy.Persistence/Migration/V3ToV4.cs) migration (just a version-stamp bump — the C# constructor default handles the missing-field deserialization).

### The three styles

- **`Hard`**: original kinked math, preserved as an option for users who want the sharp inflection. Same formulas as the initial implementation.
- **`SmoothStep`**: blend factor `w(u) = 3u² − 2u³` between the linear formula and the power-curve formula. `w(0) = 0, w(1) = 1, w'(0) = w'(1) = 0` — the zero derivative at both ends makes the linear-to-curve handover slope-continuous. Preserves the existing meaning of `Exponent` as the power exponent of the underlying curve.
- **`HermiteSpline`**: cubic Hermite spline through `(t, t)` with slope 1 (matching the linear segment) and `(1, 1)` with slope = `Exponent` (terminal slope). Truly C¹ smooth by construction. Exponent is reinterpreted as terminal slope — value `1` produces a straight line; higher values mean steeper extremes. This is the closest match to typical sim/racing "smooth response curve" visuals.

### Key decisions for the styles

- **Three styles, not one forced fix.** The user explicitly asked for options. Hard stays because some users may want the sharp inflection deliberately (especially with very low Exponent, where the "kink" becomes a soft inflection). SmoothStep and HermiteSpline have different mathematical character — Hermite is more aggressive at the extremes for the same Exponent value, SmoothStep dilutes the curve with linear blending. Worth exposing both.

- **JSON constructor default = `Hard`, catalog default = `HermiteSpline`.** Asymmetric defaults are deliberate: existing profiles without the field deserialize to Hard (no silent behavior change on load), but newly-added instances default to smooth (best out-of-box experience). The `Default` static factory and the constructor default differ by design.

- **Exponent semantics deliberately shift in HermiteSpline.** Power-exponent (Hard/SmoothStep) and terminal-slope (HermiteSpline) are different mathematical quantities, but the numeric scale ends up *feeling* similar — Exponent=2 is "moderate curve" in both interpretations; Exponent=4 is "aggressive" in both. So existing users switching styles don't have to recalibrate dramatically. The XAML hint and XML doc on the record both explain the meaning shift.

- **Display name verbose**: `"Segmented Response Curve (AboveThreshold, HermiteSpline)"`. Long, but follows the precedent of `StickDynamicsModifier`'s `({Mode})` and `MultiTapModifier`'s `({TapCount}×)` — make the per-instance flavour visible in the chain list without expanding the card.

- **Schema bump for a defaulted-field addition.** Per the project convention (any wire-format change bumps the version), this counts as a bump even though the C# constructor default makes deserialization backward-compatible without any rewriting. V3ToV4 is effectively a version-stamp bump and demonstrates that the "every bump gets a migration, even trivial ones" convention works fine for cosmetic changes. See [MIGRATION_CONVENTIONS.md](../MIGRATION_CONVENTIONS.md).

### Math reference

**SmoothStep (Above-threshold)**, for `a > t`, `u = (a - t) / (1 - t)`:
- `w = 3u² − 2u³`
- `linearPart = a`
- `curvePart = t + u^n * (1 - t)`
- `out = (1 − w) * linearPart + w * curvePart`

**SmoothStep (Below-threshold)**, for `a < t`, `u = a / t`:
- `w = 3u² − 2u³`
- `out = w * a + (1 − w) * u^n * t`
- (Inverted weighting because at the linear end `u=1, w=1` we want pure linear; at the tip `u=0, w=0` we want pure curve.)

**HermiteSpline (Above-threshold)**, for `a > t`, `u = (a - t) / (1 - t)`, `L = 1 - t`:
- `h1(u) = 2u³ − 3u² + 1`, `h2(u) = −2u³ + 3u²`, `h3(u) = u³ − 2u² + u`, `h4(u) = u³ − u²`
- `out = t·h1 + 1·h2 + L·1·h3 + L·s·h4` where `s = Exponent` (terminal slope)
- The `L` factor on the tangent terms is chain-rule scaling because slopes are specified in `a`-space but the basis is in `u`-space.

**HermiteSpline (Below-threshold)**, for `a < t`, `u = a / t`, `L = t`:
- `out = 0·h1 + t·h2 + L·s·h3 + L·1·h4`

Endpoint and slope properties verified by direct substitution and by [unit tests](../../tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs) using finite-difference slope checks.

## Files touched

### Initial implementation

- [src/Mouse2Joy.Persistence/Models/Modifier.cs](../../src/Mouse2Joy.Persistence/Models/Modifier.cs) — added `SegmentedCurveRegion` enum, `SegmentedResponseCurveModifier` record, and the `[JsonDerivedType]` attribute for the polymorphic discriminator.
- [src/Mouse2Joy.Persistence/ModifierTypes.cs](../../src/Mouse2Joy.Persistence/ModifierTypes.cs) — `GetIO` + `GetDisplayName` cases.
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/SegmentedResponseCurveEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/SegmentedResponseCurveEvaluator.cs) — new evaluator, stateless, mirrors `ResponseCurveEvaluator` structure.
- [src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs](../../src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs) — factory dispatch.
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierCatalog.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierCatalog.cs) — catalog entry.
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs) — `SegmentedResponseCurveProxy`.
- [src/Mouse2Joy.UI/ViewModels/Editor/BindingEditorViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/BindingEditorViewModel.cs) — `BuildProxy` factory case.
- [src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml](../../src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml) — DataTemplate with region combo + threshold slider + exponent slider + hint.

### Smooth-transition follow-up

- [src/Mouse2Joy.Persistence/Models/Modifier.cs](../../src/Mouse2Joy.Persistence/Models/Modifier.cs) — added `SegmentedCurveTransitionStyle` enum + 4th constructor param on the record (defaulted to `Hard`); `Default` factory now returns a HermiteSpline instance.
- [src/Mouse2Joy.Persistence/Models/Profile.cs](../../src/Mouse2Joy.Persistence/Models/Profile.cs) — `CurrentSchemaVersion = 4`.
- [src/Mouse2Joy.Persistence/Migration/V3ToV4.cs](../../src/Mouse2Joy.Persistence/Migration/V3ToV4.cs) — **new file**, version-stamp migration.
- [src/Mouse2Joy.Persistence/ProfileStore.cs](../../src/Mouse2Joy.Persistence/ProfileStore.cs) — V3→V4 added to the migration chain.
- [src/Mouse2Joy.Persistence/ModifierTypes.cs](../../src/Mouse2Joy.Persistence/ModifierTypes.cs) — `GetDisplayName` now includes style.
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/SegmentedResponseCurveEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/SegmentedResponseCurveEvaluator.cs) — refactored to dispatch on style; new SmoothStep and HermiteSpline math.
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs) — `SegmentedResponseCurveProxy` gains `TransitionStyle` property.
- [src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml](../../src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml) — Transition style combo added; hint text rewritten.
- [tests/Mouse2Joy.Persistence.Tests/V3ToV4MigrationTests.cs](../../tests/Mouse2Joy.Persistence.Tests/V3ToV4MigrationTests.cs) — **new file**.

Deliberately unchanged:
- [ResponseCurveModifier / ResponseCurveEvaluator](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/ResponseCurveEvaluator.cs) — kept simple and untouched per the "new modifier rather than extend" decision above.

## Tests

Original implementation: 16 unit tests in `SegmentedResponseCurveEvaluatorTests` covering both regions' linear/curved segments, value-continuity at the threshold, endpoint preservation at ±1, sign symmetry, `NaN → 0`, `Exponent <= 0 → identity`, input magnitude clamping, threshold guard at both boundaries, `Reset()` being a no-op (stateless), and `Config` exposure. These continue to assert the Hard-style math implicitly (constructor default = Hard).

Smooth-transition follow-up added 17 more tests in the same class plus 5 in `V3ToV4MigrationTests`:
- **SmoothStep**: linear-below-threshold passthrough, endpoint preserved at 1.0, slope ≈ 1 on both sides of the threshold (finite-difference check), zero-in-zero-out for Below, sign symmetry, and the special case Exponent=1 collapsing to pure linear because the blend is between two linear formulas.
- **HermiteSpline**: same checks, plus a terminal-slope test (slope at `a → 1` matches `Exponent`) and Exponent=1 → pure linear (start slope = end slope = 1 → cubic collapses to a line).
- **Cross-style**: same inputs through Hard / SmoothStep / HermiteSpline produce numerically distinct outputs (confirms the style switch dispatches); explicit `Hard` matches the no-style helper (sanity check that the constructor default is what we think it is).
- **V3→V4 migration**: a v3 doc without `transitionStyle` loads with `TransitionStyle == Hard`, version-stamp bumps to current, `V3ToV4.Apply` stamps directly, a v4 doc with explicit style round-trips, and the `Default` factory produces `HermiteSpline`.

Mirror this coverage shape for any future modifier evaluator added to the catalog.

## Shape support + QuinticSmooth / PowerCurve (follow-up)

After the smooth-transition styles shipped, the live curve preview revealed two more issues:

1. **The cubic HermiteSpline dips below the linear extension before accelerating** (for convex with terminal slope > 1). This is a property of cubic-Hermite constraints: with start-tangent ≤ chord-slope ≤ end-tangent, the cubic must curve below the chord to satisfy the constraints. Mirror problem for concave: it *bulges above* the chord.
2. **No explicit way to choose convex vs concave.** Today concavity was implicit (Exponent < 1 under Hard/SmoothStep means "boost small inputs," i.e. concave under those styles). HermiteSpline's behavior under Exponent < 1 was even messier (cubic overshoot in the other direction). The user wanted an explicit `Shape` enum.

### What changed (round 3)

- New `SegmentedCurveShape` enum (`Convex` / `Concave`). Added as a 5th constructor parameter on `SegmentedResponseCurveModifier`, defaulted to `Convex` (JSON load default) for backward compatibility with v4 documents.
- Two new `SegmentedCurveTransitionStyle` values:
  - **`QuinticSmooth`** — degree-5 Hermite spline with zero curvature matched at both endpoints. C² smooth at the threshold by construction → no dip, no bulge. Recommended style.
  - **`PowerCurve`** — additive form `out = t + (u + (n-1)·u²) · L / n` with renormalization. Simpler closed-form. No dip (the quadratic term is monotonically positive for n > 1). Has a documented small slope mismatch at the threshold from renormalization: linear-side slope is 1, curved-side starts at 1/n.
- `Default` factory now produces `QuinticSmooth + Convex` (was `HermiteSpline + Convex`).
- All five styles now support both Convex and Concave shapes. The math for the existing three styles was generalized via a `UnitCurve` helper that picks `u^n` (convex) or `1 − (1−u)^n` (concave) — the latter being the chord-reflection of the former.
- Display name extended to `"Segmented Response Curve (Region, Shape, Style)"`.
- Schema bump 4 → 5 with [V4ToV5.cs](../../src/Mouse2Joy.Persistence/Migration/V4ToV5.cs) (version-stamp migration; constructor defaults handle the missing field on load).

### Key decisions (round 3)

- **No backward-compat migration of Exponent < 1 values.** Under v4 (and earlier), Hard/SmoothStep with `Exponent = 0.5` produced concave-like shapes. Under v5 with the new explicit Shape semantics, `Exponent = 0.5` is "flatter than linear" rather than "concave," and Shape (defaulting to Convex on load) explicitly picks the direction. Existing profiles with `Exponent < 1` will silently re-render under the new math. User accepted this in Q&A — the math was buggy anyway and re-tuning is cheap. This is a documented behavior shift, not a bug.

- **Shape applies to all five styles, including the cubic HermiteSpline that misbehaves.** Documented honestly: cubic Hermite under concave has the *mirror* of the convex dip — it bulges above the chord. Users wanting "real" smooth concave should pick QuinticSmooth (or PowerCurve if they don't mind the slope mismatch). The cubic style is kept as an option mainly for completeness; the catalog default avoids it.

- **Default = QuinticSmooth + Convex.** The user picked QuinticSmooth after seeing that PowerCurve's slope mismatch at the threshold was a visible quirk (linear-side slope 1, curved-side slope 1/n). QuinticSmooth is the only style with no dip, no bulge, AND no slope mismatch. The catalog default reflects that.

- **The reflection identity `convex(a) + concave(a) ≈ 2a` (above-threshold).** This holds because convex sits below the chord (y=a) and concave is its mirror above. Tests assert this for all five styles within tolerance — captures the Shape symmetry property and would catch a regression if someone added a sixth style with broken concave math.

- **Quintic Hermite with zero curvature at BOTH ends** (not just at the threshold). Could have used quartic with zero curvature only at the threshold. Picked quintic with zero at both ends because it also keeps the terminal direction stable (no curvature at full deflection means the curve doesn't accelerate further past the endpoint slope — clean behavior).

- **The PowerCurve renormalization quirk is locked in by an explicit test.** `PowerCurve_above_documented_slope_mismatch_at_threshold` asserts that curved-side slope is `1/n` at the threshold. If a future "improvement" accidentally fixes this, the test fails and forces the fix to be deliberate. The mismatch isn't a bug — it's the price of the simpler formula.

### Math reference (round 3)

#### Convex/concave unit curves

```
UnitConvex(u, n)  = u^n              // sits below the chord v=u for u ∈ (0, 1)
UnitConcave(u, n) = 1 − (1 − u)^n    // sits above the chord (reflection)
```

These are the fundamental shape primitives. All five styles compose them with style-specific transition math.

#### QuinticSmooth (Above-threshold)

```
L = 1 − t
u = (a − t) / L
H1(u) = 1 − 10u³ + 15u⁴ − 6u⁵
H2(u) = 10u³ − 15u⁴ + 6u⁵
H3(u) = u − 6u³ + 8u⁴ − 3u⁵
H4(u) = −4u³ + 7u⁴ − 3u⁵
(Start-curvature and end-curvature basis terms H5, H6 have zero coefficient in our construction.)

Convex:  startSlope = 1,  endSlope = s   (matches linear at threshold, steep at full deflection)
Concave: startSlope = s,  endSlope = 1   (steep at threshold, matches linear at full deflection)

out = t·H1 + 1·H2 + L·startSlope·H3 + L·endSlope·H4
```

The chain-rule factor `L` on the slope basis terms is required because slopes are specified in a-space but the basis is in u-space (`d/da = (1/L)·d/du`).

#### PowerCurve (Above-threshold, Convex)

```
u = (a − t) / (1 − t)
v = (u + (n − 1)·u²) / n
out = t + v·(1 − t)
```

`v(0) = 0`, `v(1) = (1 + n−1)/n = 1`. Endpoint hits cleanly. Slope at u=0 in u-space is `1/n`; after chain-rule scaling, slope in a-space at the threshold is `1/n`. That's the documented kink.

#### PowerCurve (Above-threshold, Concave)

```
v = 1 − (um + (n − 1)·um²) / n   where um = 1 − u
```

Mirror of the convex curve across the chord.

#### Hermite & QuinticSmooth (Below-threshold) slope choice

For below-threshold, the curve goes from `(0, 0)` (tip) to `(t, t)` (threshold). The linear segment is above the threshold, so the endpoint at `u=1` (a=t) must have slope 1 to match. The free parameter is the tip slope at `u=0` (a=0):

- Convex below-threshold: tip slope = `1/s`. Curve sits at/below the chord (gentle tip).
- Concave below-threshold: tip slope = `s`. Curve sits above the chord (steep tip).

This is the inverse mapping vs. above-threshold (where endpoint slope is the free parameter and tip slope is fixed at 1).

### Files touched (round 3)

- [src/Mouse2Joy.Persistence/Models/Modifier.cs](../../src/Mouse2Joy.Persistence/Models/Modifier.cs) — `SegmentedCurveShape` enum; two new `SegmentedCurveTransitionStyle` values; 5th constructor param on the record; `Default` factory updated to QuinticSmooth + Convex.
- [src/Mouse2Joy.Persistence/Models/Profile.cs](../../src/Mouse2Joy.Persistence/Models/Profile.cs) — `CurrentSchemaVersion = 5`.
- [src/Mouse2Joy.Persistence/Migration/V4ToV5.cs](../../src/Mouse2Joy.Persistence/Migration/V4ToV5.cs) — **new file**, version-stamp migration.
- [src/Mouse2Joy.Persistence/ProfileStore.cs](../../src/Mouse2Joy.Persistence/ProfileStore.cs) — V4→V5 chained into the pipeline.
- [src/Mouse2Joy.Persistence/ModifierTypes.cs](../../src/Mouse2Joy.Persistence/ModifierTypes.cs) — display name now includes Shape.
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/SegmentedResponseCurveEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/SegmentedResponseCurveEvaluator.cs) — major refactor: 10 style helpers (5 styles × Above/Below), each handling both shapes via `UnitCurve` composition or style-specific math.
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs) — `Shape` property added.
- [src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml](../../src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml) — Shape combo added; transition style combo extended with two new entries; hint text rewritten.
- [tests/Mouse2Joy.Persistence.Tests/V4ToV5MigrationTests.cs](../../tests/Mouse2Joy.Persistence.Tests/V4ToV5MigrationTests.cs) — **new file**.

## Follow-ups

- *(Original "visual curve preview" follow-up shipped — the UI now has a live curve render. That preview is what made each round of math issues visible.)*
- **None outstanding for the modifier itself.** Five styles × two shapes × two regions covers all the use cases discussed. If a sixth style is ever wanted (e.g. user-drawn arbitrary Bezier with anchor handles), it slots in as another enum value with its own helper, following the established pattern.
