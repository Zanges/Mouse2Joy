# Stick model edits apply live (no restart)

## Context

Editing a binding's **stick model** (Velocity / Accumulator / Persistent — including its parameters) used to require restarting the app before the new behavior took effect. Every other binding property — stick selection, axis, button assignment, curve — already updated live. The asymmetry was the kind of papercut where "I'll just restart" becomes a tax on every tuning iteration.

Root cause: `BindingResolver` keeps a per-binding cache of stateful `IStickProcessor` instances in `OutputStateBuckets.StickProcessors`, keyed by `Binding.Id`. Each processor captures its `StickModel` config as a `readonly` field at construction time and never re-reads it. `SetProfile` previously only evicted entries whose binding ID had disappeared from the new profile — it had no concept of "binding still exists, but its model changed", so the stale processor stayed in the cache and `ApplyStick`'s `TryGetValue` kept returning it.

Other properties don't have this problem because they're read live every tick / every event from the `_profile` snapshot — `Target` in `ApplyBinding` / `ApplyStick`, `Curve` in `AdvanceTick`. Only `StickModel` produces long-lived cached behavior.

## What changed

- `IStickProcessor` gained a `StickModel Model { get; }` property exposing the config the processor was built from. Implemented in all three processors (`VelocityStickProcessor`, `AccumulatorStickProcessor`, `PersistentStickProcessor`) as a one-line `=> _config` expression.
- `BindingResolver.SetProfile` now reconciles the processor cache against the new profile's bindings: for each cached processor whose binding still exists, compare `binding.StickModel` against `processor.Model` and evict on mismatch. The next `ApplyStick` call lazily rebuilds via the existing factory path. Bindings whose `StickModel` is `null` (the factory-default fallback) are left alone — there's nothing to compare against, so no churn.
- Three new tests in `BindingResolverTests`:
  - `SetProfile_rebuilds_processor_on_stick_model_kind_change` — Velocity → Accumulator on the same binding ID evicts and lazily rebuilds with the new runtime type.
  - `SetProfile_rebuilds_processor_on_stick_model_parameter_change` — same kind, different parameter values: also evicts so the new params take effect.
  - `SetProfile_keeps_processor_when_stick_model_unchanged` — regression guard. Editing an unrelated property (e.g. `SuppressInput`) with an *equivalent* `StickModel` instance must not churn the cache. Records have value equality, so this works automatically.

## Key decisions

- **Evict-and-rebuild over in-place mutation.** When stick-model parameters change but the kind stays the same, the simplest correct behavior is also the chosen one: drop the processor and let the next event build a fresh one. Trade-off: in-flight deflection resets to zero on every parameter tweak. The user explicitly chose this over preserving deflection (which would have required adding an `UpdateConfig(StickModel)` method to every processor). Reasoning: matches what restart already does today, consistent with the kind-switch case, no new code paths to maintain. If the reset feels jarring during live tuning later, the doc-level follow-up below describes the alternative.
- **Compare via record value equality.** `StickModel` is an abstract record with three derived records — comparing `binding.StickModel != processor.Model` automatically handles both kind switches (different concrete types compare unequal) and parameter tweaks (same type with different values compare unequal). No custom equality logic, no per-kind switch in the resolver.
- **Expose `Model` on `IStickProcessor`, not as a separate side-table.** Could have stored a parallel `Dictionary<Guid, StickModel>` next to `StickProcessors` to record what each cached processor was built from. Decided against: the model belongs to the processor, the processor already has the field, and keeping two dictionaries in sync is exactly the kind of bookkeeping that breaks silently. The interface property is one extra line per implementation and makes the relationship local.
- **Skip eviction when `binding.StickModel` is null.** The factory's `null` arm builds a default `VelocityStickProcessor` with a hard-coded `VelocityStickModel`. If we naively compared `null != processor.Model`, every `SetProfile` call would evict the processor for any null-model binding, churning the cache forever. The fix: only evict when the binding has an explicit non-null model that differs from the cached one. This matches the user-visible semantic: "I haven't set a model, don't change anything".
- **No change to `ResetForIdleReport`.** That path (called from soft-mute and similar) already calls `Reset()` on every cached processor — preserving them is intentional for the soft-mute resume case. The eviction-on-model-change happens in `SetProfile`, which is the right hook because that's where the new config arrives.

## Files touched

- Modified: [IStickProcessor.cs](../../src/Mouse2Joy.Engine/StickModels/IStickProcessor.cs) — added `StickModel Model { get; }`.
- Modified: [VelocityStickProcessor.cs](../../src/Mouse2Joy.Engine/StickModels/VelocityStickProcessor.cs) — implements `Model`.
- Modified: [AccumulatorStickProcessor.cs](../../src/Mouse2Joy.Engine/StickModels/AccumulatorStickProcessor.cs) — implements `Model`.
- Modified: [PersistentStickProcessor.cs](../../src/Mouse2Joy.Engine/StickModels/PersistentStickProcessor.cs) — implements `Model`.
- Modified: [BindingResolver.cs](../../src/Mouse2Joy.Engine/Mapping/BindingResolver.cs) — `SetProfile` reconciles the cache against changed `StickModel`s.
- Modified: [BindingResolverTests.cs](../../tests/Mouse2Joy.Engine.Tests/BindingResolverTests.cs) — three new tests.

Deliberately unchanged:

- `StickProcessorFactory.cs` — the lazy-create path in `ApplyStick` already calls it, so no new construction code is needed.
- `MainViewModel.cs` / `InputEngine.cs` — the existing `Save → SetActiveProfile → SetProfile` chain was already correct; the gap was inside `SetProfile`.
- `OutputStateBuckets.cs` — the `StickProcessors` dictionary still keys by `Binding.Id`; the change is to the eviction policy, not the storage shape.

## Follow-ups

- If users find the deflection reset jarring while tuning parameters live (e.g. SpringPerSecond) on a deflected stick, add an `UpdateConfig(StickModel)` method to `IStickProcessor` and have `SetProfile` call it for same-kind changes instead of evicting. Kind switches would still evict (no meaningful state to carry across model types).
- No end-to-end UI test was added for this — the engine-level tests cover the cache contract, but a manual smoke test with the running app is still the best way to confirm behavior under real input.
