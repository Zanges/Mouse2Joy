# Modifier Chain Rework

## Context

v1 bound a `Source → Curve + StickModel → Target` flow into every binding: a monolithic four-field `Curve` (Sensitivity / InnerDeadzone / OuterSaturation / Exponent — always present, even when the user only wanted one) plus an optional polymorphic `StickModel` (Velocity / Accumulator / Persistent) for mouse-axis bindings. The editor surfaced four sliders + a stick-model dropdown regardless of relevance, sign-inversion was a hidden negative-sensitivity hack, and adding any new behavior (ramp, hysteresis, etc.) required surgery in `Binding.cs`, `BindingResolver.cs`, and the editor view.

The rework dissolves both `Curve` and `StickModel` into a single ordered list of typed `Modifier` records. A simple binding (e.g. button → button) carries an empty list; a complex one stacks Stick Dynamics, Sensitivity, Deadzone, Response Curve, and Ramp in any order. The editor is now context-aware — only controls relevant to the current source / target / selected modifier are shown — and validates the chain in real time. New behaviors land by adding one modifier record and one evaluator, with no impact on existing chains.

## What changed

- **Data model**: `Binding.Modifiers : IReadOnlyList<Modifier>` replaces the old `Curve` and `StickModel` fields. Polymorphic `Modifier` records use the same `$kind` discriminator pattern as `InputSource` / `OutputTarget`. v1 modifier catalog: `StickDynamics`, `DigitalToScalar`, `ScalarToDigitalThreshold`, `Sensitivity`, `InnerDeadzone`, `OuterSaturation`, `ResponseCurve`, `Invert`, `RampUp`, `RampDown`. Each carries an `Enabled` flag (disabled = passthrough but still type-checked).
- **Three signal types**: `Digital` (bool), `Delta` (signed mouse counts/tick), `Scalar` (`[-1, 1]`). Sources emit one; targets accept one. `ModifierTypes.GetIO` declares each modifier's `(in, out)` types; `ChainValidator.Validate` walks the chain to confirm the threading is type-correct end to end.
- **Engine pipeline**: `BindingResolver` and `OutputStateBuckets` rebuilt around per-binding `ChainEvaluator`s. Each chain owns a typed `ISourceAdapter` (mouse-axis Delta accumulator, key/button Digital latch, scroll Digital momentary) and an ordered list of `IModifierEvaluator`s, each carrying their own state where needed. `AdvanceTick` walks every binding's chain at end-of-tick, threads the signal through the modifiers, and routes the final value per target type (sticks sum + clamp `[-1, 1]`, triggers fold via `|x|` + sum + clamp `[0, 1]`, buttons / dpad use OR).
- **Persistence migration**: `Profile.SchemaVersion` bumped to 2. `ProfileStore.DeserializeProfile` peeks `schemaVersion` on load; v1 documents go through `LegacyProfile` types in `Persistence/Legacy/V1/` and `V1ToV2.Migrate` converts them losslessly into v2 modifier chains (insertion order matches the v1 `CurveEvaluator` so curves stay numerically identical). `Save` always writes v2.
- **Editor UI**: `BindingEditorWindow.xaml` rebuilt as a two-pane modal. Top: source / target / suppress / label (existing controls preserved, including `KeyCaptureBox` and the cascading sub-pickers). Middle-left: modifier chain list with per-card ▲/▼/✕ + Enable checkbox and a "+ Add modifier" dropdown. Middle-right: parameter pane that template-selects per modifier kind (mode picker for StickDynamics, sliders for thresholds and exponents, two-value editor for DigitalToScalar). Bottom-left: `ChainPreviewControl` renders the whole chain. Bottom: real-time validation banner; OK is disabled while the chain is invalid.
- **Auto-insert**: when the user wires source/target combinations that need a converter (mouse-axis → stick-axis demands StickDynamics; digital → scalar demands DigitalToScalar), the editor auto-inserts the converter at the chain head and shows an inline note explaining what it did. If the user explicitly removes a converter it isn't re-inserted (tracked in a per-session `_userRemoved` set).
- **Tests**: 28 Persistence (incl. 8 V1 migration), 56 Engine (incl. modifier evaluators, source adapters, chain evaluator, BindingResolver), 9 UI VM, 1 VirtualPad — all green.

## Key decisions

- **Ordered, user-reorderable chain (not slot-based, not canonical-order)**: order matters semantically (Invert before Sensitivity ≠ Invert after Sensitivity for non-symmetric curves), and "freedom to reorder is the point". Drag-and-drop is deferred — ▲/▼ buttons in v1 ship reliable WPF behavior without burning time on AdornerLayer DnD. Why: gives users the most expressive surface with the least UI engineering risk.
- **Three-type signal system with explicit converters**: `Digital`, `Delta`, `Scalar` are distinct types; the chain validator enforces type-correctness; converters (`DigitalToScalar`, `ScalarToDigitalThreshold`, `StickDynamics`) are explicit modifiers, not implicit edges. Why: the user wanted "explicit conversions" so converter cards are visible and configurable (e.g. tweak the on-value of a DigitalToScalar without disabling the binding). Trade-off: simplest chains (button → button) still need 0 modifiers, but button → stick needs at least 1.
- **Editor auto-inserts converters; engine never does**: only the editor knows the user is wiring something new. The engine consumes whatever's in the list and validates verbatim — invalid chains are skipped at runtime, not crashed. Why: keeps engine pure (no smarts about user intent) and keeps the editor's behavior visible (every chain change is an explicit edit the user can see and undo).
- **Curve math: Inner/OuterSaturation renormalize independently**: split into separate modifiers, each renormalizes on its own (`(a-d)/(1-d)` and `min(a, 1-o)/(1-o)` respectively), only equivalent to v1's joint `(a-d)/(1-d-o)` formula when ordered Inner → Outer with no Sensitivity between. Migration always emits Inner → Outer adjacently so v1 profiles stay numerically identical. Non-canonical orderings produce different (well-defined) curves. Why: composability requires modifiers to be self-contained; users who want the v1 joint formula get it via the canonical order, and the new freedom comes with no added complexity for the common case.
- **Each Scalar-producing modifier clamps output to `[-1, 1]`**: documented invariant on every Scalar→Scalar evaluator. Why: prevents one modifier's overshoot from breaking downstream math (e.g. a sensitivity of 5 followed by a deadzone shouldn't input `5.0` to the deadzone formula).
- **`SetProfile` preserves chain state via record value-equality**: when the user edits an unrelated field on a binding (e.g. `SuppressInput`), the cached chain stays intact (no stick deflection lost). When any modifier in the list changes by value, the whole chain rebuilds. Implemented as `cached.ConfigMatches(newBinding.Modifiers)` doing a position-by-position record equality check. Why: matches v1 behavior where Curve/StickModel record equality drove cache eviction; generalizes to chains without inventing new logic.
- **Multiple bindings to same target sum independently then clamp**: behavior change from v1, where two key bindings to the same stick axis used to share a `StickDirect` bucket and last-write-wins. Now each chain has independent state, both produce their own value, sums clamped at the target. Why: matches mouse-axis binding behavior (already summed), removes the digital-source quirk, and is what the user picked when asked. The `Multiple_bindings_to_same_target_compose` test now asserts the new sum-then-clamp semantic.
- **All phases landed in one PR**: ships 1+2+3+4+4b together to avoid an intermediate state on `main` where v1 profiles silently no-op. Each phase remained independently compilable for local testing.
- **`Curve` and `StickModel` records moved to `Persistence/Legacy/V1/`**: renamed to `LegacyCurve` / `LegacyStickModel` (and a `LegacyBinding` / `LegacyProfile`) to make their direction of dependency obvious. `internal` access; only the `V1ToV2` migrator references them. Why: persistence still needs to read v1 JSON indefinitely; keeping the types alive but isolated is cleaner than reaching into raw `JsonDocument`.
- **Hysteresis deferred** from `ScalarToDigitalThresholdModifier`. Just a single threshold in v1. Why: keeps the catalog small; hysteresis can land as a parameter addition without a schema change.
- **Rich live-runtime preview deferred** from `ChainPreviewControl`: it sweeps `x ∈ [-1, 1]` for Scalar pipelines (+ Delta-source approximation), and shows two-bar off/on for Digital chains. Real-time mirroring of in-flight signal is a follow-up. Why: covers the common "what does my response curve look like" use case at far less engineering cost than wiring a parallel evaluator into the editor.
- **Scroll stays Digital momentary** (latched true on the tick a scroll event arrives, reset at end-of-tick). Scroll-velocity-as-Delta could land later as an alternative source kind. Why: matches v1 behavior; nothing surprising.
- **Per-tick allocation discipline**: `Signal` is a `readonly struct` passed by `in` ref. Back-of-envelope ~15K modifier evaluations/sec at 250Hz with realistic profile sizes — well within the 4ms budget, no GC pressure measured.

## Files touched

Created:
- [src/Mouse2Joy.Persistence/Models/Modifier.cs](../../src/Mouse2Joy.Persistence/Models/Modifier.cs) — modifier record hierarchy.
- [src/Mouse2Joy.Persistence/Models/SignalType.cs](../../src/Mouse2Joy.Persistence/Models/SignalType.cs) — `Digital` / `Delta` / `Scalar`.
- [src/Mouse2Joy.Persistence/ModifierTypes.cs](../../src/Mouse2Joy.Persistence/ModifierTypes.cs) — type metadata + display names.
- [src/Mouse2Joy.Persistence/ChainValidator.cs](../../src/Mouse2Joy.Persistence/ChainValidator.cs) — chain type validation.
- [src/Mouse2Joy.Persistence/Legacy/V1/LegacyCurve.cs](../../src/Mouse2Joy.Persistence/Legacy/V1/LegacyCurve.cs)
- [src/Mouse2Joy.Persistence/Legacy/V1/LegacyStickModel.cs](../../src/Mouse2Joy.Persistence/Legacy/V1/LegacyStickModel.cs)
- [src/Mouse2Joy.Persistence/Legacy/V1/LegacyProfile.cs](../../src/Mouse2Joy.Persistence/Legacy/V1/LegacyProfile.cs) — v1 deserializable shapes.
- [src/Mouse2Joy.Persistence/Migration/V1ToV2.cs](../../src/Mouse2Joy.Persistence/Migration/V1ToV2.cs) — schema migration.
- [src/Mouse2Joy.Engine/Modifiers/Signal.cs](../../src/Mouse2Joy.Engine/Modifiers/Signal.cs) — tagged-union signal struct.
- [src/Mouse2Joy.Engine/Modifiers/IModifierEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/IModifierEvaluator.cs)
- [src/Mouse2Joy.Engine/Modifiers/ISourceAdapter.cs](../../src/Mouse2Joy.Engine/Modifiers/ISourceAdapter.cs)
- [src/Mouse2Joy.Engine/Modifiers/ChainEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/ChainEvaluator.cs) — owns adapter + evaluator list.
- [src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs](../../src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs) — factory.
- [src/Mouse2Joy.Engine/Modifiers/SourceAdapters/MouseAxisAdapter.cs](../../src/Mouse2Joy.Engine/Modifiers/SourceAdapters/MouseAxisAdapter.cs)
- [src/Mouse2Joy.Engine/Modifiers/SourceAdapters/DigitalLatchAdapter.cs](../../src/Mouse2Joy.Engine/Modifiers/SourceAdapters/DigitalLatchAdapter.cs)
- [src/Mouse2Joy.Engine/Modifiers/SourceAdapters/DigitalMomentaryAdapter.cs](../../src/Mouse2Joy.Engine/Modifiers/SourceAdapters/DigitalMomentaryAdapter.cs)
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/) — one file per modifier kind (10 evaluators).
- [src/Mouse2Joy.UI/Controls/ChainPreviewControl.cs](../../src/Mouse2Joy.UI/Controls/ChainPreviewControl.cs) — replaces v1 CurveEditorControl.
- [src/Mouse2Joy.UI/Converters/InvBoolToVisConverter.cs](../../src/Mouse2Joy.UI/Converters/InvBoolToVisConverter.cs) — for the validation banner.
- [src/Mouse2Joy.UI/ViewModels/Editor/](../../src/Mouse2Joy.UI/ViewModels/Editor/) — `BindingEditorViewModel`, `ModifierCardViewModel`, `ModifierCatalog`, per-kind `ModifierParamProxy`.
- [src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml](../../src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml) — DataTemplates per proxy kind.
- [tests/Mouse2Joy.UI.Tests/](../../tests/Mouse2Joy.UI.Tests/) — new test project for VM tests.
- [tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs](../../tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs) — per-evaluator + chain + adapter tests.
- [tests/Mouse2Joy.Persistence.Tests/ModifierSerializationTests.cs](../../tests/Mouse2Joy.Persistence.Tests/ModifierSerializationTests.cs)
- [tests/Mouse2Joy.Persistence.Tests/ChainValidatorTests.cs](../../tests/Mouse2Joy.Persistence.Tests/ChainValidatorTests.cs)
- [tests/Mouse2Joy.Persistence.Tests/V1MigrationTests.cs](../../tests/Mouse2Joy.Persistence.Tests/V1MigrationTests.cs)

Modified:
- [src/Mouse2Joy.Persistence/Models/Binding.cs](../../src/Mouse2Joy.Persistence/Models/Binding.cs) — replace `Curve` + `StickModel` fields with `Modifiers`.
- [src/Mouse2Joy.Persistence/Models/Profile.cs](../../src/Mouse2Joy.Persistence/Models/Profile.cs) — `CurrentSchemaVersion = 2`.
- [src/Mouse2Joy.Persistence/ProfileStore.cs](../../src/Mouse2Joy.Persistence/ProfileStore.cs) — peek `schemaVersion` on load, route v1 through migration.
- [src/Mouse2Joy.Persistence/Mouse2Joy.Persistence.csproj](../../src/Mouse2Joy.Persistence/Mouse2Joy.Persistence.csproj) — `InternalsVisibleTo` for tests.
- [src/Mouse2Joy.Engine/Mapping/OutputStateBuckets.cs](../../src/Mouse2Joy.Engine/Mapping/OutputStateBuckets.cs) — replace `StickProcessors` / `StickDirect` with `Chains`.
- [src/Mouse2Joy.Engine/Mapping/BindingResolver.cs](../../src/Mouse2Joy.Engine/Mapping/BindingResolver.cs) — chain-based Apply / AdvanceTick / SetProfile.
- [src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml](../../src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml) — full rewrite to two-pane.
- [src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml.cs) — VM-bound, source/target subpicker plumbing preserved.
- [src/Mouse2Joy.UI/ViewModels/BindingDisplay.cs](../../src/Mouse2Joy.UI/ViewModels/BindingDisplay.cs) — unchanged (auto-label format unaffected by modifiers).
- [src/Mouse2Joy.App/App.xaml](../../src/Mouse2Joy.App/App.xaml) — register `InvBoolToVis` and `BoolToVis` converters.
- [tests/Mouse2Joy.Persistence.Tests/ProfileSerializationRoundtripTests.cs](../../tests/Mouse2Joy.Persistence.Tests/ProfileSerializationRoundtripTests.cs) — switched to Modifiers; asserts `schemaVersion=2`.
- [tests/Mouse2Joy.Engine.Tests/BindingResolverTests.cs](../../tests/Mouse2Joy.Engine.Tests/BindingResolverTests.cs) — rewritten for the new shape; multi-binding test updated to assert sum-then-clamp.

Deleted:
- `src/Mouse2Joy.Persistence/Models/Curve.cs` (lives in Legacy/V1 now).
- `src/Mouse2Joy.Persistence/Models/StickModel.cs` (lives in Legacy/V1 now).
- `src/Mouse2Joy.Engine/Mapping/CurveEvaluator.cs` — replaced by per-modifier evaluators.
- `src/Mouse2Joy.Engine/StickModels/` (entire directory) — replaced by `StickDynamicsEvaluator` + the integration logic in `Modifiers/Evaluators/`.
- `src/Mouse2Joy.UI/Controls/CurveEditorControl.cs` — replaced by `ChainPreviewControl`.
- `tests/Mouse2Joy.Engine.Tests/CurveEvaluatorTests.cs`, `StickProcessorTests.cs` — replaced by the per-evaluator tests in `Modifiers/`.

Deliberately unchanged:
- `BindingDisplay.cs`, `BindingRowViewModel.cs` — they read only `Source`, `Target`, `Label`, `Enabled`. Auto-label format is unaffected.
- The panic hotkey, soft/hard mode toggling, profile activation lifecycle, virtual pad code, hotkey settings, overlay widgets — all orthogonal to bindings.
- `Mouse2Joy.Engine.Tests.HotkeyMatcherTests` — passes unchanged.

## Follow-ups

- **Drag-and-drop reorder** for the modifier list. ▲/▼ buttons ship in v1; DnD via WPF AdornerLayer is a polish PR.
- **Hysteresis** on `ScalarToDigitalThresholdModifier` (low/high thresholds). Single field added to the record; no schema break.
- **Live runtime preview** in the editor — the chain pipeline is decoupled enough that wiring a parallel evaluator from the engine into `ChainPreviewControl` is straightforward.
- **Rich Digital/Delta-source previews** — Digital chains currently render two-bar off/on; could show timeline (e.g. "press for 0.5s shows the ramp curve"). Delta chains use a steady-state approximation; could replace with a true mouse-motion playback.
- **Scroll velocity as Delta** — currently scroll is momentary Digital. Adding "scroll velocity → analog stick deflection" is a new source adapter (Delta) + migration handling for any user who explicitly opts in.
- **Multi-binding-same-target documentation** — update user docs to mention the v1→v2 behavior change for the rare digital-to-stick combo.
