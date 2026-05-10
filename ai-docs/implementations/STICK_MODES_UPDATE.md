# Stick modes update: Persistent mode + Edit Binding window polish

## Context

The Edit Binding dialog had three problems that landed in one change set because they all live in the binding editor + stick model layer:

1. The window was clipped at the bottom — `Height="540"` wasn't tall enough to show the OK/Cancel button row when the Stick model section had two parameter rows visible.
2. The Stick model section labelled its inputs `Param 1` / `Param 2`. Meaningless without reading the source — the labels gave no hint about what the parameter controlled or what range was sensible.
3. Both existing stick modes auto-recenter — Velocity decays toward 0 the moment the mouse stops, and Accumulator springs back continuously. There was no "stick stays where you put it" mode. The user wanted a third behavior where the only way to recenter is to physically move the mouse the same distance the other way.

## What changed

- New `PersistentStickModel` record + `PersistentStickProcessor` implementing `IStickProcessor`. Integrates mouse counts into a deflection just like Accumulator, but with no spring/decay term. Single parameter: `CountsPerFullDeflection`.
- New `"persistent"` discriminator added to `StickModel`'s `[JsonDerivedType]` table. New arm in `StickProcessorFactory`.
- New tests in `PersistentStickProcessorTests` covering: hold-without-input, reverse-from-clamped-point, overshoot-discarded, clamp range, sign, reset.
- Edit Binding window:
  - Sizing switched to `SizeToContent="Height"` with `MinHeight="600"`, `MinWidth="520"`, and `ResizeMode="CanResize"` so the window always fits its content even when the param row count changes.
  - Outer grid row 2 changed from `Height="*"` to `Height="Auto"` (the star row was incompatible with `SizeToContent="Height"` and the stick model groupbox should size to content anyway).
  - Stick model dropdown gained a third entry: `Persistent (no recenter)`.
  - Param rows now use `x:Name`d labels (`Param1Label`, `Param2Label`) and a named row (`Param2Row`) so the code-behind can rewrite labels, attach tooltips, and collapse the second row entirely for Persistent mode.
  - Code-behind has new `OnStickModelChanged()` (defaults + labels) and `ApplyStickModelLabels()` (label text, tooltips, row visibility). `LoadFrom` and `OnOk` handle the third mode.
  - Param 1 column widened from 120px to 160px to fit the new descriptive labels (e.g. "Counts/sec → full").

## Key decisions

- **Third mode, not a flag on Accumulator.** Could have been done as `AccumulatorStickModel` with `SpringPerSecond=0`, but the user explicitly wants the behavior to be *discoverable from the dropdown* rather than hidden behind a parameter value. A separate record also gives the persistent mode its own label/tooltip/single-param UI without conditionals leaking into Accumulator's path.
- **Clamp + discard overshoot at ±1.0.** When the user moves the mouse past the full-deflection point, the extra counts are dropped. Recovery distance is measured from the clamped point (1.0), not from however far past the edge the mouse went. The alternative — let the internal accumulator overflow and require the user to "undo" the overshoot before the stick comes off the edge — was rejected because it makes recovery feel sluggish and unpredictable. With the chosen behavior, "move back N counts to deflect by N/CountsPerFullDeflection" always holds.
- **`SizeToContent="Height"` over picking a bigger fixed height.** The Stick model groupbox grows from 2 rows (Persistent) to 3 rows (Velocity / Accumulator) depending on the selected mode. A fixed height tall enough for the worst case wastes vertical space in the others; `SizeToContent="Height"` with a `MinHeight` is robust to future row count changes too.
- **Defaults populate on dropdown change.** Switching the dropdown rewrites the param textboxes with mode-appropriate defaults (Velocity 8.0/800.0, Accumulator 5.0/400.0, Persistent 400.0). `LoadFrom` sets the index first (default fires), then overwrites with the persisted value — same pattern the original code used.
- **Tooltips, not inline help text.** The descriptive labels are short ("Decay /sec", "Counts → full") so the GroupBox stays compact; the longer "what does this do, what's a typical range" explanation lives in a tooltip on the label. Hovering surfaces it; nothing wastes space when the user already knows.
- **Scale-parameter tooltips warn about overlap with Sensitivity.** With a default curve (deadzone=0, saturation=0, exponent=1), the per-mode "counts → full" / "counts/sec → full" parameter behaves much like the Curve's Sensitivity slider — both effectively scale how much mouse input pegs the stick. The two only diverge when the user has a non-trivial curve (the stick-model param shifts where deadzone/saturation fall in mouse-distance/speed terms; Sensitivity scales after the curve). To avoid users tweaking the wrong knob first, all three scale-param tooltips (Persistent's `Counts → full`, Accumulator's `Counts → full`, Velocity's `Counts/sec → full`) carry the same advice: leave at default, tune Sensitivity instead, only change if you specifically want the deadzone/saturation interaction. The time-domain params (`Decay /sec`, `Spring /sec`) don't carry the caveat — they control response over time and are genuinely independent of anything else.

## Files touched

- New: [PersistentStickProcessor.cs](../../src/Mouse2Joy.Engine/StickModels/PersistentStickProcessor.cs)
- Modified: [StickModel.cs](../../src/Mouse2Joy.Persistence/Models/StickModel.cs) — added `PersistentStickModel` record + `[JsonDerivedType]` entry.
- Modified: [StickProcessorFactory.cs](../../src/Mouse2Joy.Engine/StickModels/StickProcessorFactory.cs) — added Persistent arm.
- Modified: [BindingEditorWindow.xaml](../../src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml) — sizing, third combo item, named labels and row.
- Modified: [BindingEditorWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml.cs) — `OnStickModelChanged`, `ApplyStickModelLabels`, Persistent handling in `LoadFrom` / `OnOk`.
- Modified: [StickProcessorTests.cs](../../tests/Mouse2Joy.Engine.Tests/StickProcessorTests.cs) — new `PersistentStickProcessorTests` class.

Deliberately unchanged:

- `BindingResolver.cs` and `InputEngine.cs` — they call against the `IStickProcessor` interface and need no awareness of the new processor type.
- The curve evaluator and per-binding curve UI — orthogonal to the stick model.

## Follow-ups

- The persistence schema version (`schemaVersion: 1`) was *not* bumped. Profiles saved by older builds without a `PersistentStickModel` keep working unchanged; profiles using the new type written by this build will fail to deserialize on older builds. If backwards-compat with older app versions becomes a concern, bump the schema and gate on it. For now this is fine because the user runs only one build at a time.
- The Param 1 textbox value carries over visually when the user switches modes (e.g., switching from Velocity to Accumulator overwrites the field via the defaults). If users want the editor to remember per-mode values within a single dialog session, that's a future iteration.
- No UI yet for visualizing the stick's current deflection in real time. The user mentioned wanting to verify Persistent behavior with a gamepad tester (e.g., `joy.cpl`); a built-in live view would be nicer but is out of scope here.
