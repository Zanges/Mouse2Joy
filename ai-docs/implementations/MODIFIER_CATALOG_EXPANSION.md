# Modifier Catalog Expansion

## Context

The first modifier-chain ship ([MODIFIER_CHAIN_REWORK.md](MODIFIER_CHAIN_REWORK.md)) covered the v1-equivalent set: stick dynamics, the four curve sub-modifiers, conversions, and ramps. Five common patterns were missing from that catalog and had no clean expression in a chain:

- Capping a stick's deflection on one side independently (e.g. "this key is a 33% nudge button, that one is a 66% sprint button"). Achievable today via `DigitalToScalarModifier.OnValue`, but not for capping mouse-axis or post-shape Scalar signals.
- Toggling an output by tapping a key (caps-lock semantics).
- Smoothing twitchy mouse motion as a steady-state low-pass, distinct from the rate-limit shape that `RampUp`/`RampDown` provide.
- Auto-fire / rapid-fire while a button is held.
- Hold-to-activate (long-press latency before output fires).

All five land as new `Modifier` records + evaluators with no schema-version bump (additive on the polymorphic discriminator list). Existing v1 and v2 profiles are unaffected.

## What changed

- **Five new modifier records** in [Modifier.cs](../../src/Mouse2Joy.Persistence/Models/Modifier.cs), each with a new `$kind` discriminator:
  - `LimiterModifier` (Scalar→Scalar): `MaxPositive`, `MaxNegative`. Hard-clamps `x` asymmetrically.
  - `ToggleModifier` (Digital→Digital, stateful, no params): rising-edge flip.
  - `SmoothingModifier` (Scalar→Scalar, stateful): `TimeConstantSeconds`. First-order EMA with dt-independent constant `alpha = 1 - exp(-dt / tau)`.
  - `AutoFireModifier` (Digital→Digital, stateful): `Hz`. While input held, output toggles at `Hz` with 50% duty cycle. Releasing resets the phase.
  - `HoldToActivateModifier` (Digital→Digital, stateful): `HoldSeconds`. Output stays false until input has been held continuously for `HoldSeconds`; releasing resets the timer.
- **Five new evaluators** in [src/Mouse2Joy.Engine/Modifiers/Evaluators/](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/). All implement `IModifierEvaluator`, which means they slot into the existing `ChainBuilder` factory and `ChainEvaluator.EndOfTick` walk with no resolver changes.
- **Editor wiring**: each new kind gets a `*Proxy` in [ModifierParamProxies.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs), a catalog entry in [ModifierCatalog.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierCatalog.cs), and a `DataTemplate` in [ModifierParamsTemplates.xaml](../../src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml). `Toggle` returns a null proxy (no params) — the right pane shows nothing for it, same convention as `Invert`.
- **Tests**: 20 new evaluator tests, 5 new modifier-roundtrip serialization assertions, 3 new chain-validator tests covering the new Digital→Digital and Scalar→Scalar paths.

## Key decisions

- **Limiter is asymmetric (`MaxPositive` / `MaxNegative`), not symmetric (`MaxMagnitude`)**: covers both "cap the whole stick at 50%" (set both to 0.5) and "this key only deflects right up to 0.33" (`MaxPositive=0.33`, `MaxNegative=1.0`). The asymmetric form subsumes the symmetric one with one extra field.
- **Limiter is a hard clamp, not a rescale**: `|x| > max` becomes `±max`, not `(x / 1) × max`. This matches the user's "cap" intent — two bindings to the same axis with `MaxPositive=0.33` and `MaxPositive=0.66` sum and clamp at the target to produce coherent multi-key deflection. Rescaling would compress instead, which isn't what anyone asks for.
- **Toggle flips on rising edge only, not falling**: the natural caps-lock model is "tap once to turn on, tap again to turn off". Falling edges firing flips means a single tap toggles twice; that's not what users mean.
- **Toggle's first observed press DOES flip immediately**: `_prevInput` defaults to `false`, so an initial `true` reads as a rising edge. That's the right behavior for the case where the user activates a binding while already holding the key — they tap it next, that release-then-press flips state. The alternative (require a known false-then-true sequence) would mean toggle bindings don't respond to the first interaction after profile activation, which is surprising. Documented in `Held_input_at_construction_does_not_flip_until_release_and_repress` test.
- **Smoothing uses `1 - exp(-dt / tau)` as the EMA constant, not a fixed `alpha`**: makes the filter dt-independent — switching tick rate doesn't change the time-domain response. Tau = 0 is treated as passthrough so the modifier can be configured "off" without removing the card.
- **Smoothing seeds on first sample**: without seeding, the EMA would start from 0 and lag heavily for the first tau seconds after enabling. Seeded behavior matches what the user expects ("turn smoothing on, see no immediate jump").
- **AutoFire uses 50% duty cycle, not a configurable duty**: keeps the parameter surface to one field (`Hz`). If asymmetric duty becomes a need it's a follow-up — most rapid-fire use cases just want "pulse on/off as fast as possible at this rate".
- **AutoFire releasing resets phase**: prevents the awkward case where a re-press lands mid-period and the first pulse is short. The reset means every press starts at "true" cleanly.
- **HoldToActivate's release fully resets the timer**: a brief release-then-press starts the count from zero, not from where it was. Matches "long press" expectation; the "resume after a brief release" semantic would be a different modifier (think trigger anti-jitter), not what users are asking for here.
- **Toggle / AutoFire / HoldToActivate are Digital→Digital**: this is significant — they don't need a `DigitalToScalar` converter. A typical toggle binding is `Key → Toggle → Button`, no converter at all. To use them with a stick target, the user adds `DigitalToScalar` after them: `Key → Toggle → DigitalToScalar → Stick Axis`.
- **No schema bump**: additive `JsonDerivedType` entries on the polymorphic discriminator. Old profiles deserialize unchanged; new profiles using the new modifiers can't be opened by an older build (same forward-compat assumption as the rest of the system).

## Files touched

Created:
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/LimiterEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/LimiterEvaluator.cs)
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/ToggleEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/ToggleEvaluator.cs)
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/SmoothingEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/SmoothingEvaluator.cs)
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/AutoFireEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/AutoFireEvaluator.cs)
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/HoldToActivateEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/HoldToActivateEvaluator.cs)

Modified:
- [src/Mouse2Joy.Persistence/Models/Modifier.cs](../../src/Mouse2Joy.Persistence/Models/Modifier.cs) — five new records + discriminator entries.
- [src/Mouse2Joy.Persistence/ModifierTypes.cs](../../src/Mouse2Joy.Persistence/ModifierTypes.cs) — IO type metadata + display names.
- [src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs](../../src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs) — five new factory branches.
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierCatalog.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierCatalog.cs) — five new entries.
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs) — four new proxies (Toggle has none).
- [src/Mouse2Joy.UI/ViewModels/Editor/BindingEditorViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/BindingEditorViewModel.cs) — `BuildProxy` dispatch for new kinds.
- [src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml](../../src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml) — five new DataTemplates.
- [tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs](../../tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs) — five new test classes, 20 new tests.
- [tests/Mouse2Joy.Persistence.Tests/ModifierSerializationTests.cs](../../tests/Mouse2Joy.Persistence.Tests/ModifierSerializationTests.cs) — five new modifiers covered in the round-trip case.
- [tests/Mouse2Joy.Persistence.Tests/ChainValidatorTests.cs](../../tests/Mouse2Joy.Persistence.Tests/ChainValidatorTests.cs) — three new chain-shape tests.

Deliberately unchanged:
- `BindingResolver`, `ChainEvaluator`, `OutputStateBuckets`, `ProfileStore`, `V1ToV2` — the new modifiers are pure additions to the polymorphic catalog; nothing in the resolution / evaluation / persistence pipeline needed an update.
- The chain preview ([ChainPreviewControl.cs](../../src/Mouse2Joy.UI/Controls/ChainPreviewControl.cs)) renders post-StickDynamics shape sweeps; the new Scalar modifiers (Limiter, Smoothing) participate in those sweeps automatically. The new Digital modifiers (Toggle, AutoFire, HoldToActivate) sit on Digital→Digital paths where the preview already shows the simpler "off/on" two-bar fallback — richer time-domain rendering is a follow-up.

## Follow-ups

- **Configurable duty cycle on AutoFire** — add a `Duty` field if the 50% default proves limiting.
- **AutoFire phase preservation across releases** as a separate modifier or option, for cases where users want exact constant timing regardless of release/re-press timing.
- **Time-domain preview** for Digital chains (Toggle, AutoFire, HoldToActivate, Tap, MultiTap) — the current chain preview only sweeps Scalar inputs; a small timeline visualization would make these modifiers' behavior obvious at a glance.

---

## Addendum: Tap and MultiTap (added later in the same iteration cycle)

### Context

[HoldToActivate](#) covers "fire after the input is held N seconds" but had no complement for "fire when the input is *not* held that long". The original design called the tap-vs-hold split a binding-shape problem, but the user pointed out the cleaner expression: **two bindings on the same source**, one with a Tap modifier (fires on release if hold was short), one with HoldToActivate (latched while held past threshold). The same composition handles asymmetric magnitudes — bind 33% deflection unconditionally and 67% via HoldToActivate; sums to 100% at the target on a sustained hold.

The user also asked for **multi-press detection** (double-tap to activate a different binding). Bundling into Tap was considered but rejected — a single-tap with a `TapCount=1` field is messier than a purpose-built modifier with `TapCount`, `WindowSeconds`, and per-tap hold cap.

### What changed

- **`TapModifier`** (Digital→Digital, stateful): `MaxHoldSeconds` (max hold duration that still counts as a tap) + `PulseSeconds` (how long the output stays true after a tap). Pulses true on release when the input was held shorter than `MaxHoldSeconds`. Long holds are silently absorbed.
- **`MultiTapModifier`** (Digital→Digital, stateful): `TapCount`, `WindowSeconds` (max time between successive taps), `MaxHoldSeconds`, `PulseSeconds`. Counts qualifying taps; fires when N land within the window.
- Catalog labels: `Tap`, `Multi-tap (N×)` (the count is rendered into the display name so the user can tell two MultiTap cards apart at a glance).

### Key decisions

- **Tap fires on release, not on press**: matches the natural "tap" mental model and lets the modifier reject too-long holds (a hold has to complete via release before we know whether it qualified). Pressing-and-holding for 5 seconds, then releasing, fires nothing — the release is too late.
- **Pulse decay reads post-decay**: `_pulseRemaining` is decremented by `dt` *before* the output is read, so the pulse lasts for exactly `PulseSeconds` of cumulative dt before dropping. The pre-decay variant overshoots by one tick, which is visible in tests with `PulseSeconds` near typical dt. `MultiTapEvaluator` follows the same rule for consistency.
- **`PulseSeconds = 0` falls back to "one tick"**: implemented as `_pulseRemaining = double.Epsilon`, which decays to 0 within a single tick of any positive `dt`. Keeps the field meaningful at zero while preserving the "single-tick pulse" intent.
- **Hold validation is sticky**: once `_heldFor` exceeds `MaxHoldSeconds` mid-hold, `_holdInvalidated` is set and the eventual release silently absorbs. Without the sticky bit, the release tick would re-check `_heldFor <= MaxHoldSeconds` and could fire if `dt` overshoots are subtle.
- **`<=` (not `<`) at the boundary**: holding for *exactly* `MaxHoldSeconds` still counts as a tap. Inclusive boundary feels less surprising in practice ("0.3s tap window" should include 0.3s exactly).
- **MultiTap window arms on each tap, not on the first**: the window timer resets to `WindowSeconds` after every successful tap. That means "second tap within 0.4s of the first; third tap within 0.4s of the second"; a steady cadence of just-under-0.4s taps stays valid forever until count is reached. Alternative ("window starts on first tap, doesn't reset") would require all N taps within one `WindowSeconds` interval — too tight for triples.
- **MultiTap window decays before edge handling**: ensures a press exactly at the boundary still counts as part of the sequence (the decay reduces `_windowRemaining` to ≤0, the press creates a fresh `_windowRemaining` for the next tap). Order chosen so a fresh first tap can't be wiped out by its own dt decrement.
- **No tap-count threshold below 2 for MultiTap**: editor slider min is 2; setting `TapCount=1` would be redundant with `TapModifier`. Code defensively clamps to ≥1 to handle hand-edited JSON.
- **No `MaxHoldSeconds` flag on HoldToActivate**: the original Q&A asked about this; the tap-vs-hold split is already cleanly expressed by Tap + HoldToActivate as separate bindings. Adding a flag to HoldToActivate would have created two ways to do the same thing.

### Files touched

Created:
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/TapEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/TapEvaluator.cs)
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/MultiTapEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/MultiTapEvaluator.cs)

Modified:
- [src/Mouse2Joy.Persistence/Models/Modifier.cs](../../src/Mouse2Joy.Persistence/Models/Modifier.cs) — `TapModifier`, `MultiTapModifier` records + discriminators.
- [src/Mouse2Joy.Persistence/ModifierTypes.cs](../../src/Mouse2Joy.Persistence/ModifierTypes.cs) — IO types + display names (MultiTap shows its count in the name).
- [src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs](../../src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs) — factory branches.
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierCatalog.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierCatalog.cs) — catalog entries.
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs) — `TapProxy`, `MultiTapProxy`.
- [src/Mouse2Joy.UI/ViewModels/Editor/BindingEditorViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/BindingEditorViewModel.cs) — `BuildProxy` dispatch.
- [src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml](../../src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml) — Tap and MultiTap DataTemplates.
- [tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs](../../tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs) — `TapEvaluatorTests`, `MultiTapEvaluatorTests` (11 new tests).
- [tests/Mouse2Joy.Persistence.Tests/ModifierSerializationTests.cs](../../tests/Mouse2Joy.Persistence.Tests/ModifierSerializationTests.cs) — round-trip coverage for both new modifiers.

### Patterns these enable

- **Tap vs. hold split**: bind LMB twice — once with `Tap → ButtonA`, once with `HoldToActivate → ButtonB`. Tap binding fires on quick release; HoldToActivate fires after sustained hold. Mutually exclusive at runtime.
- **Asymmetric magnitude on hold**: bind LMB twice — once with `DigitalToScalar(OnValue=0.33) → StickX` (always fires while held), once with `HoldToActivate → DigitalToScalar(OnValue=0.67) → StickX`. Sums to 1.0 at the target after the hold threshold; quick presses stay at 0.33.
- **Double-click to activate**: bind a key with `MultiTap(2) → ButtonA`. Holds and single taps don't fire; only a fast double-tap does.
- **Triple-tap power move**: bind a key with `MultiTap(3) → ButtonB`. Pairs naturally with a single-tap binding for "tap = light attack, triple-tap = heavy".

### Follow-ups (additions)

- **Configurable hold-validation start** for Tap (currently from press): if a user wants "tap = release within N seconds of *anything*, including a brief release-press cycle", add a `Mode` field. No requests yet.
- **MultiTap with mixed long/short patterns** (e.g. "short, short, long" Morse-like sequences) — out of scope; would need a richer state machine and config.

---

## Addendum 2: Tap-resolution disambiguation (WaitForHigherTaps + WaitForTapResolution)

### Context

After shipping Tap and MultiTap, the user hit the obvious problem: a `MultiTap(2)` binding fires on the second tap of a triple-tap because nothing tells it to wait. Same problem for plain "single press" bindings: `Key → Button` fires immediately on press, so a `MultiTap(2)` sibling can't reliably take precedence. The solution needs to be opt-in (existing bindings shouldn't gain unexpected latency) and composable across multiple Tap/MultiTap bindings on the same source.

### What changed

- **`WaitForHigherTaps : bool`** flag added to `TapModifier` (with companion `ConfirmWaitSeconds` field) and `MultiTapModifier` (reuses `WindowSeconds`). Default false → existing bindings unchanged. When true, the modifier delays its pulse for the wait window after release; if a follow-up press arrives during the wait, the prior pending pulse is canceled.
- **New `WaitForTapResolutionModifier`** (Digital→Digital): standalone wait+passthrough for plain `Key → Button` bindings that don't use Tap. Fields: `MaxHoldSeconds`, `WaitSeconds`, `PulseSeconds`. Same wait-and-cancel semantics as Tap with the flag on, but no tap-counting and no inheritance from existing Tap behavior.
- **Early-exit on overflow**: all three modifiers fire pending pulses *immediately* when a follow-up press is detected to be "not a tap" — i.e. held longer than `MaxHoldSeconds`. The waiting period doesn't run to completion; the long press unambiguously rules out further taps and confirms the prior one.
- **Passthrough on overflow** for Tap (with flag on) and WaitForTapResolution: a press held past `MaxHoldSeconds` mirrors the input directly while held. This means a single binding with these modifiers serves *both* "tap fires a pulse" and "press-and-hold mirrors input" use cases. MultiTap explicitly does NOT passthrough — a long press in a multi-tap sequence resets the partial count silently (multi-tap is purpose-built for tap counting).

### Key decisions

- **Wait-pause-while-input-held**: the wait countdown only decrements when the input is currently false. While the user is mid-press, we don't know yet whether the press will be a tap or an overflow. Pausing the timer keeps the wait window meaningful regardless of how long the new press takes to resolve. Without this, a slow follow-up press could expire the wait mid-press and fire incorrectly.
- **Don't cancel pending on rising edge — wait for the resolve**: the original implementation canceled pending pulses immediately on any new press. This broke the early-exit-on-overflow path entirely (by the time we detected overflow, there was no pending pulse left to fire). The fix: rising edge stores the new press's hold timer but leaves pending intact. The falling edge (tap) cancels pending; the overflow trip (long press) fires pending early.
- **Two short presses in a row collapse to "the last one fires"** for Tap and WaitForTapResolution: the second tap's release overwrites the first's pending wait with a fresh wait. If a third press arrives, the second's wait gets overwritten in turn. This means "spamming a key" with a wait-flag binding fires once, after the user stops. For sibling MultiTap bindings, the second short press cancels the first's wait too — the user's intent is "I'm doing a multi-tap on a sibling, ignore me here".
- **MultiTap doesn't increment count when a tap arrives during pending wait**: once an N-tap fires internally, a follow-up tap means "a higher-count sibling is taking over" — the modifier cancels the pending pulse and explicitly does NOT count the follow-up toward its own next sequence. This avoids double-firing if the user does N, then N more, then N more (each N-burst would otherwise fire two pulses).
- **Reuse `WindowSeconds` as the MultiTap wait window**: same semantic ("how long between taps?"), one fewer field. Tap doesn't have `WindowSeconds`, so it gets a new `ConfirmWaitSeconds` field used only when the flag is on.
- **Pre-existing tests with default (flag-off) behavior remain green**: backwards compatibility verified. The flag is purely additive.
- **Three modifiers, not one super-modifier**: Tap covers "single tap with optional wait"; MultiTap covers "N taps with optional wait"; WaitForTapResolution covers "plain press with mandatory wait". I considered collapsing them into one but each has different default behaviors (Tap fires on release, MultiTap counts, WaitForTapResolution always waits) and different parameter shapes. Three single-purpose modifiers compose better than one with mode flags.

### Recommended composition for the user's scenario

To bind LMB to fire `A` on single press, `B` on double tap, `C` on triple tap, all without conflicts:

- Bind 1: `LMB → WaitForTapResolution(MaxHold=0.3, Wait=0.4) → Button A` — single press fires after wait clears.
- Bind 2: `LMB → MultiTap(2, Window=0.4, ..., WaitForHigherTaps=true) → Button B` — double tap fires after wait clears.
- Bind 3: `LMB → MultiTap(3, Window=0.4, ...) → Button C` — triple tap fires immediately on third release.

Triple-tap arrives → all three see it. Bind 3 fires immediately. Bind 2's pending double-tap pulse is canceled by the third tap arriving during its wait (even though Bind 3 already fired separately). Bind 1's pending pulse is canceled by the second press arriving during its wait. Net: only Bind 3 fires.

Double-tap arrives → Bind 1's wait canceled by second press. Bind 2 fires after its wait. Bind 3 sees only two taps and never fires.

Single press → only Bind 1 fires after its wait.

Long press → all three see it. Bind 1's WaitForTapResolution engages passthrough (Button A held while held). Bind 2's MultiTap resets (no count). Bind 3's MultiTap resets. Net: Button A is held while LMB is held.

### Files touched

Modified:
- [src/Mouse2Joy.Persistence/Models/Modifier.cs](../../src/Mouse2Joy.Persistence/Models/Modifier.cs) — `TapModifier` adds `WaitForHigherTaps`, `ConfirmWaitSeconds`. `MultiTapModifier` adds `WaitForHigherTaps`. New `WaitForTapResolutionModifier` record + `$kind` discriminator.
- [src/Mouse2Joy.Persistence/ModifierTypes.cs](../../src/Mouse2Joy.Persistence/ModifierTypes.cs) — IO type metadata + display name for the new modifier.
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/TapEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/TapEvaluator.cs) — full rewrite for wait/cancel/early-fire/passthrough state machine.
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/MultiTapEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/MultiTapEvaluator.cs) — same state machine extension, no passthrough.
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/WaitForTapResolutionEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/WaitForTapResolutionEvaluator.cs) — new file.
- [src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs](../../src/Mouse2Joy.Engine/Modifiers/ChainBuilder.cs) — factory branch for new evaluator.
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierCatalog.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierCatalog.cs) — catalog entry.
- [src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/ModifierParamProxies.cs) — `TapProxy` adds `WaitForHigherTaps` + `ConfirmWaitSeconds` + `ConfirmWaitVisible`. `MultiTapProxy` adds `WaitForHigherTaps`. New `WaitForTapResolutionProxy`.
- [src/Mouse2Joy.UI/ViewModels/Editor/BindingEditorViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/Editor/BindingEditorViewModel.cs) — `BuildProxy` dispatch.
- [src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml](../../src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml) — Tap template gets the wait checkbox + collapsible ConfirmWait field. MultiTap template gets the wait checkbox. New WaitForTapResolution template.
- [tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs](../../tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs) — new test classes `TapWithWaitForHigherTapsTests`, `MultiTapWithWaitForHigherTapsTests`, `WaitForTapResolutionEvaluatorTests` (~14 new tests).
- [tests/Mouse2Joy.Persistence.Tests/ModifierSerializationTests.cs](../../tests/Mouse2Joy.Persistence.Tests/ModifierSerializationTests.cs) — round-trip coverage for new flag values and the new modifier.

### Follow-ups (more)

- **Visual indicator in the editor** when a binding is in the "waiting to confirm" state at runtime — currently invisible to the user, who might wonder why their tap takes 0.4s to register. Could surface in the Profile activity feed or as a per-binding tooltip.
- **Per-source coordination**: the current design relies on the user manually setting matching wait windows across sibling bindings. A future "binding group" abstraction could share a single wait timer across all bindings on the same source, eliminating the need to coordinate `ConfirmWaitSeconds`/`WindowSeconds` values.

---

## Addendum 3: Multi-press suppression and hold-only mode

### Context

Live testing revealed that the previous "fresh tap restarts wait" behavior in `TapEvaluator` and `WaitForTapResolutionEvaluator` produced an unexpected result: when the user did a double or triple tap intending only the matching MultiTap sibling to fire, the single-tap binding (Tap or WaitForTapResolution) ALSO fired — because the second/third tap's release armed a fresh pending pulse for that binding.

Separately, the user noticed that PulseSeconds=0 didn't work as a "hold-only" config. The original code converted 0 to a single-tick pulse (`double.Epsilon`), which made the binding fire briefly on every tap regardless. There was no way to express "I want this binding to only respond to long-presses, never to taps".

### What changed

- **Suppression mode** added to `TapEvaluator` and `WaitForTapResolutionEvaluator` via a new `_pendingFire` boolean alongside the existing wait timer:
  - Fresh tap (no wait in flight) → arm wait + `_pendingFire = true`. Wait expiry fires the pulse.
  - Tap during pending wait → refresh wait + `_pendingFire = false`. The user is doing a multi-press sequence; this binding stays silent. Subsequent taps keep refreshing the wait so suppression covers the full window from each release.
  - Long press during pending wait → if `_pendingFire = true`, fire pulse early (overflow rules out further taps); if `_pendingFire = false`, clear silently. Passthrough engages either way.
  - Wait expires with `_pendingFire = false` → just clear the wait state, no pulse.
- **Press resolution decides pendingFire**, not the rising edge. The previous attempt cleared `_pendingFire` on rising edge (any new press) — that broke long-press-during-wait early-fire because the press's overflow could no longer revive a "real" pending fire. The new logic waits until the press resolves: tap → set `_pendingFire = false`; overflow → leave `_pendingFire` alone (so a prior pending fire confirms).
- **PulseSeconds = 0 = no pulse** for `TapEvaluator` (when `WaitForHigherTaps = true`) and `WaitForTapResolutionEvaluator`. The pulse-arming code now skips entirely when `PulseSeconds <= 0`. Combined with the existing passthrough-on-overflow, this gives a "hold-only" binding: tap fires nothing, hold past `MaxHoldSeconds` engages passthrough until release.
  - For plain `TapEvaluator` without `WaitForHigherTaps` (no passthrough), PulseSeconds=0 still collapses to a single-tick pulse — otherwise the modifier would be inert with no useful semantic.
- **UI tooltip** added to both Tap (in the WaitForHigherTaps-only collapsed section) and WaitForTapResolution explaining the PulseSeconds=0 hold-only trick.

### Key decisions

- **Suppression mode is "set on tap, not on press"**: the rising edge alone doesn't determine intent. The user might be starting a multi-press sequence (release coming soon → tap) or a long-press (release after MaxHold → overflow). Defer the decision to the resolution edge. This is how the long-press-during-wait early-fire path keeps working.
- **Refresh wait on every tap in suppression mode**: ensures the binding stays suppressed for the full window after the *last* tap, not just the first. Without this, a slow triple-tap (taps spaced ~window apart) would suppress on tap 2 and fire on tap 3.
- **`PulseSeconds = 0` is honored in wait-paths only**: the original "epsilon = single tick" semantic was useful for backward compat. Changing it everywhere would break existing profiles. Now the rule is: in modes that have a meaningful "no pulse" use (those with passthrough), 0 means no pulse; elsewhere, 0 collapses to single-tick.
- **Hold-only is a config combination, not a separate modifier**: the user can already get hold-only from `TapModifier` with `PulseSeconds=0, WaitForHigherTaps=true` or `WaitForTapResolutionModifier` with `PulseSeconds=0`. No new modifier needed.

### Files touched

Modified:
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/TapEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/TapEvaluator.cs) — `_pendingFire` field, suppression-mode logic on rising/falling edges, PulseSeconds=0 honored in wait path.
- [src/Mouse2Joy.Engine/Modifiers/Evaluators/WaitForTapResolutionEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/WaitForTapResolutionEvaluator.cs) — same pattern.
- [src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml](../../src/Mouse2Joy.UI/Views/Editor/ModifierParamsTemplates.xaml) — hold-only tip added to Tap (WaitForHigherTaps section) and WaitForTapResolution.
- [tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs](../../tests/Mouse2Joy.Engine.Tests/Modifiers/ModifierEvaluatorTests.cs) — `Followup_short_press_suppresses_pending_pulse_for_full_window`, `Triple_press_sequence_stays_suppressed` (Tap + WaitForTapResolution), `Pulse_zero_with_wait_makes_binding_hold_only` (Tap), `Pulse_zero_makes_binding_hold_only` (WaitForTapResolution). Replaced the prior "second tap fires on its own wait" tests which asserted the old behavior.

### Composition pattern: triple-tap with single-tap and hold

For LMB → A on single tap, B on double tap, C on triple tap, D on hold (all non-conflicting):

- Bind 1 (single): `WaitForTapResolution(MaxHold=0.3, Wait=0.4, PulseSeconds=0.05) → Button A`
- Bind 2 (double): `MultiTap(2, Window=0.4, ..., WaitForHigherTaps=true) → Button B`
- Bind 3 (triple): `MultiTap(3, Window=0.4, ...) → Button C`
- Bind 4 (hold): `WaitForTapResolution(MaxHold=0.3, Wait=0.4, PulseSeconds=0) → Button D`

Single tap → Bind 1 fires after wait. Bind 4 sees the tap but pulse=0 → silent.
Double tap → Bind 2 fires after wait. Bind 1 enters suppression mode → silent. Bind 4 silent.
Triple tap → Bind 3 fires immediately on third release. Binds 1 and 2 enter suppression mode. Bind 4 silent.
Hold → Bind 4 engages passthrough (Button D held while LMB held). Bind 1 also engages passthrough (Button A held — both hold-bindings activate simultaneously). Binds 2 and 3 silent.

Net: clean separation across all four interaction patterns with five fields per binding to coordinate (MaxHold, Wait/Window, PulseSeconds, WaitForHigherTaps, TapCount).
