# Delta Scale modifier + Sensitivity → Output Scale rename

## Context

A user (Zanges) discovered that setting `SensitivityModifier < 1` after a `StickDynamicsModifier` made full stick deflection unreachable. Their intuition — shared by most gamers — was that "less sensitive" should mean "more mouse motion required, full deflection still reachable." The actual behavior was "output scaled down, max deflection capped at the multiplier."

This wasn't a bug in the implementation — `SensitivityModifier` did exactly what its code said. But its **name was misleading**. By the time the modifier sees the signal, the integrator (`StickDynamicsModifier`) has already produced a value in `[-1, 1]` and the information that the user moved the mouse past saturation has been discarded. No amount of math inside `Sensitivity` itself can recover "more motion needed but full deflection still reachable" — it's structurally impossible from that chain position.

After Q&A we decided the fix was *not* to defang the integrator (its anti-windup clamp is load-bearing for Persistent / Accumulator modes — without it, deflection would wind up to arbitrary values and stop responding to reverse input). Instead, we added the missing modifier slot: a **Delta-side scaler** that runs *before* `StickDynamics` and influences how many mouse counts produce full deflection.

The existing modifier was renamed to match what it actually is.

## What changed

- **New `DeltaScaleModifier`** (`Delta → Delta`, display name "Delta Scale", JSON discriminator `"deltaScale"`). Multiplies the raw mouse-count stream by `Factor` before integration. `Factor < 1` requires more motion to fully deflect; `Factor > 1` makes it snappier. Full deflection stays reachable in both cases. Default `Factor = 1.0` (passthrough). Negative factors are clamped to 0 in the evaluator (no inversion).
- **`SensitivityModifier` renamed to `OutputScaleModifier`** (display name "Output Scale", JSON discriminator `"outputScale"`). Property `Multiplier` renamed to `Factor` to match `DeltaScaleModifier.Factor`. Runtime behavior unchanged — it's still the post-integrator scaler / governor it always was.
- **Schema bumped 2 → 3.** New JSON-node migration in [V2ToV3.cs](../../src/Mouse2Joy.Persistence/Migration/V2ToV3.cs) rewrites `"$kind": "sensitivity"` → `"$kind": "outputScale"` and renames `"multiplier"` → `"factor"` in modifier nodes. Migrations now chain in `ProfileStore.DeserializeProfile`: v1 → V1ToV2 → V2ToV3 → final deserialize.
- **New [ai-docs/MIGRATION_CONVENTIONS.md](../MIGRATION_CONVENTIONS.md)** documenting the hybrid pattern (JSON-node rewrite for small changes, typed-record rebuild for structural changes) and the rules around versioning (bump on any wire-format change, every bump requires a migration + tests, don't pin specific version numbers in docs). CLAUDE.md's "Things that are easy to get wrong" entry on the persistence schema points at this file rather than inlining the convention.
- 16 new tests (`DeltaScaleEvaluatorTests`, `V2ToV3MigrationTests`) plus 3 integration tests in `ChainEvaluatorTests` that lock in the behavior contract: Output Scale caps, Delta Scale reduces sensitivity without capping. Existing `SensitivityEvaluatorTests` renamed to `OutputScaleEvaluatorTests`; v1 migration tests updated to reference `Profile.CurrentSchemaVersion` instead of pinning version 2.

## Key decisions

- **Add a new modifier rather than reach into `StickDynamics` from `Sensitivity`.** The clean solution preserves the composable-modifier abstraction. Reaching into a sibling modifier (e.g. "if Sensitivity < 1, silently rescale StickDynamics's MaxVelocityCounts") would create spooky-action-at-a-distance coupling, break when the chain doesn't have a `StickDynamics`, and lie to the user about what their chain is doing. Two clearly-named modifiers, each doing one job, is the right shape.

- **Rename `Sensitivity` rather than keep it with a clarified tooltip.** "Sensitivity" overwhelmingly means input gain in gaming contexts. Users will reach for it expecting "more motion required" — exactly the bug Zanges hit. A tooltip won't save the next user from the same surprise. "Output Scale" names the operation honestly.

- **Don't remove the integrator's internal ±1 clamp.** The user's first instinct was to remove the clamp inside `StickDynamicsEvaluator` and let downstream modifiers handle clamping at the very end of the chain. Anti-windup objection landed: in Persistent mode, removing the clamp means deflection can integrate to 10000 with rightward motion, then takes 10000 counts of leftward motion before the stick even starts moving back from full-right. That's a UX disaster. The internal state clamp and the output transport clamp are *different jobs*, not redundant.

- **`DeltaScale.Factor` clamped to ≥ 0 in the evaluator** (with NaN treated as 0). Negative values would mean mouse inversion, which is a *conceptually separate* operation from sensitivity scaling. Inversion is deferred to a future `DeltaInvertModifier` (or `Reverse` flag) if anyone asks. Slider range `[0, 3]`.

- **`Math.Round` instead of truncation** when scaling integer mouse counts by a fractional factor. Banker's rounding (the .NET default `MidpointRounding.ToEven`) gives an unbiased long-term average. Truncation would systematically lose ~0.5 counts per nonzero tick on slow motion, biasing the stick toward zero. The trade-off is tiny per-tick rounding noise, invisible against typical mouse polling jitter.

- **Bump schemaVersion for a cosmetic rename.** Conservative rule: any wire-format change bumps, even when runtime semantics are identical. Predictable, no judgment calls per change, no risk of silently breaking compatibility. Cost: version numbers tick somewhat faster.

- **JSON-node migration for V2→V3** rather than typed-record rebuild. V1→V2 used the typed-record-rebuild pattern because v1 had a genuinely different shape (`Curve` + `StickModel` fields had to be re-expressed as modifier chains). V2→V3 is a single-discriminator + single-field rename — the typed-record-rebuild pattern would have required copying dozens of records into `Legacy/V2/` for what's really a 30-line transform. The new pattern (JSON-node `Apply(root)`) is documented as the default for small changes; typed-record-rebuild is for structural changes.

- **Migration tests assert against `Profile.CurrentSchemaVersion`, not hardcoded numbers.** The previous V1 tests pinned version 2, which would have broken silently on this bump. Going forward, tests reference the constant so the next migration only needs to add new tests, not fix existing ones.

## Files touched

- [src/Mouse2Joy.Persistence/Models/Modifier.cs](../../src/Mouse2Joy.Persistence/Models/Modifier.cs) — new `DeltaScaleModifier` record + `[JsonDerivedType]`; rename `SensitivityModifier` → `OutputScaleModifier` (also renamed property `Multiplier` → `Factor`); updated discriminator from `"sensitivity"` to `"outputScale"`.
- [src/Mouse2Joy.Persistence/Models/Profile.cs](../../src/Mouse2Joy.Persistence/Models/Profile.cs) — `CurrentSchemaVersion` bumped to 3.
- [src/Mouse2Joy.Persistence/ModifierTypes.cs](../../src/Mouse2Joy.Persistence/ModifierTypes.cs) — `GetIO` + `GetDisplayName` cases for both modifiers.
- [src/Mouse2Joy.Persistence/Migration/V2ToV3.cs](../../src/Mouse2Joy.Persistence/Migration/V2ToV3.cs) — **new file**, JSON-node migration.
- [src/Mouse2Joy.Persistence/ProfileStore.cs](../../src/Mouse2Joy.Persistence/ProfileStore.cs) — extended `DeserializeProfile` to chain V1→V2 then V2→V3 then final deserialize.
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/DeltaScaleEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/DeltaScaleEvaluator.cs) — **new file**.
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/OutputScaleEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/OutputScaleEvaluator.cs) — **new file** (replaces `SensitivityEvaluator.cs`, which was deleted).
- [src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs](../../src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs) — factory dispatch updated.
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierCatalog.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierCatalog.cs) — new "Delta Scale" entry near the Stick Dynamics group; renamed "Sensitivity" → "Output Scale".
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs) — new `DeltaScaleProxy`; renamed `SensitivityProxy` → `OutputScaleProxy` (property `Multiplier` → `Factor`).
- [src/Mouse2Joy.UI/ViewModels/Editor/BindingEditorViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/BindingEditorViewModel.cs) — `BuildProxy` cases.
- [src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml](../../src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml) — new "Delta Scale" template; renamed "Sensitivity" template → "Output Scale" with the cross-reference hint.
- [tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs](../../tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs) — `OutputScaleEvaluatorTests` (renamed), `DeltaScaleEvaluatorTests` (new, 9 tests), 3 new integration tests inside `ChainEvaluatorTests` locking in the behavior contract.
- [tests/Mouse2Joy.Persistence.Tests/V2ToV3MigrationTests.cs](../../tests/Mouse2Joy.Persistence.Tests/V2ToV3MigrationTests.cs) — **new file**, 6 tests covering direct migration, no-op profiles, chained v1→v2→v3 path, idempotency.
- [tests/Mouse2Joy.Persistence.Tests/V1MigrationTests.cs](../../tests/Mouse2Joy.Persistence.Tests/V1MigrationTests.cs) — fixed three hardcoded `Be(2)` version assertions to reference `Profile.CurrentSchemaVersion`. Renamed `V2_json_skips_migration` → `Current_version_json_skips_migration`.
- [CLAUDE.md](../../CLAUDE.md) — dropped the stale "schemaVersion: 1" pin in the "Things that are easy to get wrong" section; replaced with a one-line pointer at the new conventions file.
- [ai-docs/MIGRATION_CONVENTIONS.md](../MIGRATION_CONVENTIONS.md) — **new file**, project-wide migration conventions reference (first top-level convention doc; `ai-docs/implementations/` stays scoped to per-feature write-downs).

Deliberately unchanged:
- [StickDynamicsEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/StickDynamicsEvaluator.cs) — anti-windup clamp stays. The integrator is correct as-is.
- [V1ToV2.cs](../../src/Mouse2Joy.Persistence/Migration/V1ToV2.cs) — works, is tested, follows the older typed-record-rebuild convention. Don't rewrite it just because the convention evolved.

## Follow-ups

- **Mouse inversion as a separate modifier.** If a user asks for inverted aim, add `DeltaInvertModifier` (Delta → Delta, just negates) rather than allowing negative `DeltaScale.Factor`. Inversion is conceptually distinct from sensitivity scaling.
- **Auto-suggest `DeltaScale` when the user has a saturated `StickDynamics` + low `OutputScale`.** Nice UX but out of scope. The tooltip cross-references the two modifiers, which should be enough hint for most users.
- **Migrate the V1→V2 implementation to the JSON-node convention.** Not worth doing opportunistically — the existing one works and is tested. If V1 support is ever dropped, this code can go entirely.

## Tests

23 new tests landed:

- `OutputScaleEvaluatorTests` — 4 tests (3 inherited from `SensitivityEvaluatorTests` renamed + 1 new `Factor_below_one_caps_saturated_input_below_unity` that explicitly asserts the governor contract).
- `DeltaScaleEvaluatorTests` — 9 tests covering identity at Factor=1, proportional scaling, banker's-rounding behavior, zero/negative/NaN factor guards, no overflow at realistic magnitudes, stateless `Reset`, `Config` exposure.
- `ChainEvaluatorTests` (extended) — 3 new tests: `OutputScale_caps_post_integrator_signal`, `DeltaScale_reduces_sensitivity_without_capping_max`, `DeltaScale_below_one_requires_more_motion_for_same_deflection`. These codify the behavior contract: Output Scale governs the cap, Delta Scale governs sensitivity, both produce numerically predictable results in Persistent mode.
- `V2ToV3MigrationTests` — 6 tests covering direct `V2ToV3.Apply` rewrite (kind + property), `DeserializeProfile` end-to-end for v2-with-sensitivity, v2-without-sensitivity (no-op path), v3 round-trip (no migration touches it), and the full v1 → V1ToV2 → V2ToV3 pipeline.

Existing test impact: three `SchemaVersion.Should().Be(2)` assertions in `V1MigrationTests` updated to `Be(Profile.CurrentSchemaVersion)` so they don't go stale on the next bump.
