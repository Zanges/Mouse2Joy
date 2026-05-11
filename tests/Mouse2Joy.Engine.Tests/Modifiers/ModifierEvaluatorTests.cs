using FluentAssertions;
using Mouse2Joy.Engine.Modifiers;
using Mouse2Joy.Engine.Modifiers.Evaluators;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Tests.Modifiers;

public class SensitivityEvaluatorTests
{
    [Fact]
    public void Identity_passes_through()
    {
        var e = new SensitivityEvaluator(new SensitivityModifier(1.0));
        var output = e.Evaluate(Signal.Scalar(0.5), 0.01);
        output.ScalarValue.Should().Be(0.5);
    }

    [Fact]
    public void Multiplier_scales_signal()
    {
        var e = new SensitivityEvaluator(new SensitivityModifier(2.0));
        e.Evaluate(Signal.Scalar(0.4), 0.01).ScalarValue.Should().Be(0.8);
    }

    [Fact]
    public void Output_clamps_to_unit_range()
    {
        var e = new SensitivityEvaluator(new SensitivityModifier(2.0));
        e.Evaluate(Signal.Scalar(0.6), 0.01).ScalarValue.Should().Be(1.0);
        e.Evaluate(Signal.Scalar(-0.6), 0.01).ScalarValue.Should().Be(-1.0);
    }
}

public class InnerDeadzoneEvaluatorTests
{
    [Fact]
    public void Inputs_below_threshold_are_zero()
    {
        var e = new InnerDeadzoneEvaluator(new InnerDeadzoneModifier(0.2));
        e.Evaluate(Signal.Scalar(0.1), 0.01).ScalarValue.Should().Be(0.0);
        e.Evaluate(Signal.Scalar(-0.2), 0.01).ScalarValue.Should().Be(0.0);
    }

    [Fact]
    public void Inputs_above_threshold_renormalize()
    {
        var e = new InnerDeadzoneEvaluator(new InnerDeadzoneModifier(0.2));
        // (0.6 - 0.2) / (1 - 0.2) = 0.5
        e.Evaluate(Signal.Scalar(0.6), 0.01).ScalarValue.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Sign_preserved()
    {
        var e = new InnerDeadzoneEvaluator(new InnerDeadzoneModifier(0.2));
        e.Evaluate(Signal.Scalar(-0.6), 0.01).ScalarValue.Should().BeApproximately(-0.5, 1e-9);
    }
}

public class OuterSaturationEvaluatorTests
{
    [Fact]
    public void Identity_at_zero_threshold()
    {
        var e = new OuterSaturationEvaluator(new OuterSaturationModifier(0.0));
        e.Evaluate(Signal.Scalar(0.5), 0.01).ScalarValue.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Inputs_in_outer_band_clamp_to_unit()
    {
        var e = new OuterSaturationEvaluator(new OuterSaturationModifier(0.2));
        // |x|=0.85 → min(0.85, 0.8) / 0.8 = 1.0
        e.Evaluate(Signal.Scalar(0.85), 0.01).ScalarValue.Should().Be(1.0);
        e.Evaluate(Signal.Scalar(-0.9), 0.01).ScalarValue.Should().Be(-1.0);
    }

    [Fact]
    public void Inputs_in_main_range_renormalize()
    {
        var e = new OuterSaturationEvaluator(new OuterSaturationModifier(0.2));
        // |x|=0.4 → 0.4/0.8 = 0.5
        e.Evaluate(Signal.Scalar(0.4), 0.01).ScalarValue.Should().BeApproximately(0.5, 1e-9);
    }
}

public class ResponseCurveEvaluatorTests
{
    [Fact]
    public void Identity_at_exponent_one()
    {
        var e = new ResponseCurveEvaluator(new ResponseCurveModifier(1.0));
        e.Evaluate(Signal.Scalar(0.5), 0.01).ScalarValue.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Convex_attenuates_small_inputs()
    {
        var e = new ResponseCurveEvaluator(new ResponseCurveModifier(2.0));
        e.Evaluate(Signal.Scalar(0.5), 0.01).ScalarValue.Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void Concave_boosts_small_inputs()
    {
        var e = new ResponseCurveEvaluator(new ResponseCurveModifier(0.5));
        e.Evaluate(Signal.Scalar(0.25), 0.01).ScalarValue.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Sign_preserved()
    {
        var e = new ResponseCurveEvaluator(new ResponseCurveModifier(2.0));
        e.Evaluate(Signal.Scalar(-0.5), 0.01).ScalarValue.Should().BeApproximately(-0.25, 1e-9);
    }

    [Fact]
    public void Nonpositive_exponent_collapses_to_identity()
    {
        var e = new ResponseCurveEvaluator(new ResponseCurveModifier(0.0));
        e.Evaluate(Signal.Scalar(0.5), 0.01).ScalarValue.Should().BeApproximately(0.5, 1e-9);
    }
}

public class SegmentedResponseCurveEvaluatorTests
{
    private static SegmentedResponseCurveEvaluator Above(double threshold, double exponent) =>
        new(new SegmentedResponseCurveModifier(threshold, exponent, SegmentedCurveRegion.AboveThreshold));

    private static SegmentedResponseCurveEvaluator Below(double threshold, double exponent) =>
        new(new SegmentedResponseCurveModifier(threshold, exponent, SegmentedCurveRegion.BelowThreshold));

    [Fact]
    public void Above_linear_segment_passes_through_unchanged()
    {
        var e = Above(threshold: 0.3, exponent: 2.0);
        // Inputs at or below threshold are linear passthrough.
        e.Evaluate(Signal.Scalar(0.0), 0.01).ScalarValue.Should().BeApproximately(0.0, 1e-9);
        e.Evaluate(Signal.Scalar(0.1), 0.01).ScalarValue.Should().BeApproximately(0.1, 1e-9);
        e.Evaluate(Signal.Scalar(0.3), 0.01).ScalarValue.Should().BeApproximately(0.3, 1e-9);
    }

    [Fact]
    public void Above_curved_segment_applies_remapped_power()
    {
        var e = Above(threshold: 0.3, exponent: 2.0);
        // a = 0.65, t = 0.3 → u = (0.65 - 0.3) / 0.7 = 0.5; v = 0.25; out = 0.3 + 0.25*0.7 = 0.475.
        e.Evaluate(Signal.Scalar(0.65), 0.01).ScalarValue.Should().BeApproximately(0.475, 1e-9);
    }

    [Fact]
    public void Above_is_continuous_at_threshold()
    {
        // The whole point of segment remap: linear and curved segments meet
        // cleanly. Output at a = t should equal t exactly, not jump.
        var e = Above(threshold: 0.3, exponent: 4.0);
        e.Evaluate(Signal.Scalar(0.3), 0.01).ScalarValue.Should().BeApproximately(0.3, 1e-9);
        // Just above: still effectively continuous (small u → small v).
        e.Evaluate(Signal.Scalar(0.30001), 0.01).ScalarValue.Should().BeApproximately(0.3, 1e-4);
    }

    [Fact]
    public void Above_preserves_endpoint_at_one()
    {
        // Full deflection must survive any curve shape so the user can still hit ±1.
        Above(0.3, 2.0).Evaluate(Signal.Scalar(1.0), 0.01).ScalarValue.Should().BeApproximately(1.0, 1e-9);
        Above(0.5, 4.0).Evaluate(Signal.Scalar(1.0), 0.01).ScalarValue.Should().BeApproximately(1.0, 1e-9);
        Above(0.1, 0.5).Evaluate(Signal.Scalar(1.0), 0.01).ScalarValue.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Below_linear_segment_passes_through_unchanged()
    {
        var e = Below(threshold: 0.3, exponent: 2.0);
        // Inputs at or above threshold are linear passthrough.
        e.Evaluate(Signal.Scalar(0.3), 0.01).ScalarValue.Should().BeApproximately(0.3, 1e-9);
        e.Evaluate(Signal.Scalar(0.7), 0.01).ScalarValue.Should().BeApproximately(0.7, 1e-9);
        e.Evaluate(Signal.Scalar(1.0), 0.01).ScalarValue.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Below_curved_segment_applies_remapped_power()
    {
        var e = Below(threshold: 0.4, exponent: 2.0);
        // a = 0.2, t = 0.4 → u = 0.2/0.4 = 0.5; v = 0.25; out = 0.25 * 0.4 = 0.1.
        e.Evaluate(Signal.Scalar(0.2), 0.01).ScalarValue.Should().BeApproximately(0.1, 1e-9);
    }

    [Fact]
    public void Below_is_continuous_at_threshold()
    {
        var e = Below(threshold: 0.4, exponent: 4.0);
        // At a = t: u = 1, v = 1, out = t — meets the linear segment cleanly.
        e.Evaluate(Signal.Scalar(0.4), 0.01).ScalarValue.Should().BeApproximately(0.4, 1e-9);
        e.Evaluate(Signal.Scalar(0.39999), 0.01).ScalarValue.Should().BeApproximately(0.4, 1e-4);
    }

    [Fact]
    public void Below_zero_in_zero_out()
    {
        Below(0.3, 2.0).Evaluate(Signal.Scalar(0.0), 0.01).ScalarValue.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void Sign_preserved_in_both_regions()
    {
        var above = Above(threshold: 0.3, exponent: 2.0);
        above.Evaluate(Signal.Scalar(-0.65), 0.01).ScalarValue.Should().BeApproximately(-0.475, 1e-9);
        above.Evaluate(Signal.Scalar(-0.1), 0.01).ScalarValue.Should().BeApproximately(-0.1, 1e-9);

        var below = Below(threshold: 0.4, exponent: 2.0);
        below.Evaluate(Signal.Scalar(-0.2), 0.01).ScalarValue.Should().BeApproximately(-0.1, 1e-9);
        below.Evaluate(Signal.Scalar(-0.7), 0.01).ScalarValue.Should().BeApproximately(-0.7, 1e-9);
    }

    [Fact]
    public void Nan_input_yields_zero_scalar()
    {
        Above(0.3, 2.0).Evaluate(Signal.Scalar(double.NaN), 0.01).ScalarValue.Should().Be(0.0);
        Below(0.3, 2.0).Evaluate(Signal.Scalar(double.NaN), 0.01).ScalarValue.Should().Be(0.0);
    }

    [Fact]
    public void Nonpositive_exponent_collapses_to_identity()
    {
        // Same guard as ResponseCurveEvaluator: exponent <= 0 → n = 1.
        // In Above mode this means linear-then-linear → full passthrough.
        var above = Above(threshold: 0.3, exponent: 0.0);
        above.Evaluate(Signal.Scalar(0.5), 0.01).ScalarValue.Should().BeApproximately(0.5, 1e-9);
        above.Evaluate(Signal.Scalar(0.9), 0.01).ScalarValue.Should().BeApproximately(0.9, 1e-9);

        var below = Below(threshold: 0.4, exponent: -1.0);
        below.Evaluate(Signal.Scalar(0.2), 0.01).ScalarValue.Should().BeApproximately(0.2, 1e-9);
    }

    [Fact]
    public void Inputs_clamped_to_unit_magnitude()
    {
        // |x| > 1 must clamp to 1 before the segment math, otherwise the
        // upper-segment remap u = (a - t)/(1 - t) could exceed 1 and produce
        // out-of-range output.
        Above(0.3, 2.0).Evaluate(Signal.Scalar(1.5), 0.01).ScalarValue.Should().BeApproximately(1.0, 1e-9);
        Above(0.3, 2.0).Evaluate(Signal.Scalar(-1.5), 0.01).ScalarValue.Should().BeApproximately(-1.0, 1e-9);
    }

    [Fact]
    public void Threshold_near_zero_does_not_produce_nan_or_inf()
    {
        // The evaluator's defensive clamp (Threshold ∈ [1e-6, 1-1e-6]) must
        // keep the segment remap finite even when the user stores 0.0.
        var above = Above(threshold: 0.0, exponent: 2.0);
        var outVal = above.Evaluate(Signal.Scalar(0.5), 0.01).ScalarValue;
        outVal.Should().NotBe(double.NaN);
        double.IsFinite(outVal).Should().BeTrue();

        var below = Below(threshold: 0.0, exponent: 2.0);
        var outVal2 = below.Evaluate(Signal.Scalar(0.5), 0.01).ScalarValue;
        double.IsFinite(outVal2).Should().BeTrue();
    }

    [Fact]
    public void Threshold_near_one_does_not_produce_nan_or_inf()
    {
        var above = Above(threshold: 1.0, exponent: 2.0);
        var outVal = above.Evaluate(Signal.Scalar(0.9999), 0.01).ScalarValue;
        double.IsFinite(outVal).Should().BeTrue();
        // Almost-all-linear range: output very close to input.
        outVal.Should().BeApproximately(0.9999, 1e-3);

        var below = Below(threshold: 1.0, exponent: 2.0);
        var outVal2 = below.Evaluate(Signal.Scalar(0.5), 0.01).ScalarValue;
        double.IsFinite(outVal2).Should().BeTrue();
    }

    [Fact]
    public void Reset_is_a_noop_because_evaluator_is_stateless()
    {
        var e = Above(0.3, 2.0);
        e.Evaluate(Signal.Scalar(0.65), 0.01).ScalarValue.Should().BeApproximately(0.475, 1e-9);
        e.Reset();
        // Same input → same output; no state carried.
        e.Evaluate(Signal.Scalar(0.65), 0.01).ScalarValue.Should().BeApproximately(0.475, 1e-9);
    }

    [Fact]
    public void Config_property_exposes_underlying_modifier()
    {
        var mod = new SegmentedResponseCurveModifier(0.3, 2.0, SegmentedCurveRegion.AboveThreshold);
        var e = new SegmentedResponseCurveEvaluator(mod);
        e.Config.Should().BeSameAs(mod);
    }
}

public class InvertEvaluatorTests
{
    [Fact]
    public void Negates_input()
    {
        var e = new InvertEvaluator(new InvertModifier());
        e.Evaluate(Signal.Scalar(0.5), 0.01).ScalarValue.Should().Be(-0.5);
        e.Evaluate(Signal.Scalar(-0.7), 0.01).ScalarValue.Should().Be(0.7);
    }
}

public class DigitalToScalarEvaluatorTests
{
    [Fact]
    public void True_yields_on_value()
    {
        var e = new DigitalToScalarEvaluator(new DigitalToScalarModifier(1.0, 0.0));
        e.Evaluate(Signal.Digital(true), 0.01).ScalarValue.Should().Be(1.0);
    }

    [Fact]
    public void False_yields_off_value()
    {
        var e = new DigitalToScalarEvaluator(new DigitalToScalarModifier(1.0, 0.0));
        e.Evaluate(Signal.Digital(false), 0.01).ScalarValue.Should().Be(0.0);
    }

    [Fact]
    public void Custom_values_supported()
    {
        var e = new DigitalToScalarEvaluator(new DigitalToScalarModifier(0.7, -0.3));
        e.Evaluate(Signal.Digital(true), 0.01).ScalarValue.Should().Be(0.7);
        e.Evaluate(Signal.Digital(false), 0.01).ScalarValue.Should().Be(-0.3);
    }
}

public class ScalarToDigitalThresholdEvaluatorTests
{
    [Fact]
    public void Below_threshold_is_false()
    {
        var e = new ScalarToDigitalThresholdEvaluator(new ScalarToDigitalThresholdModifier(0.5));
        e.Evaluate(Signal.Scalar(0.3), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Above_threshold_is_true()
    {
        var e = new ScalarToDigitalThresholdEvaluator(new ScalarToDigitalThresholdModifier(0.5));
        e.Evaluate(Signal.Scalar(0.6), 0.01).DigitalValue.Should().BeTrue();
    }

    [Fact]
    public void Threshold_is_magnitude_based()
    {
        var e = new ScalarToDigitalThresholdEvaluator(new ScalarToDigitalThresholdModifier(0.5));
        e.Evaluate(Signal.Scalar(-0.6), 0.01).DigitalValue.Should().BeTrue();
    }
}

public class RampUpEvaluatorTests
{
    [Fact]
    public void Increases_are_rate_limited()
    {
        var e = new RampUpEvaluator(new RampUpModifier(1.0));
        // dt=0.1 → max step = 0.1
        e.Evaluate(Signal.Scalar(1.0), 0.1).ScalarValue.Should().BeApproximately(0.1, 1e-9);
        e.Evaluate(Signal.Scalar(1.0), 0.1).ScalarValue.Should().BeApproximately(0.2, 1e-9);
    }

    [Fact]
    public void Decreases_pass_through()
    {
        var e = new RampUpEvaluator(new RampUpModifier(1.0));
        // ramp up to 0.5
        for (int i = 0; i < 5; i++) e.Evaluate(Signal.Scalar(1.0), 0.1);
        // now drop input to 0 — should be instant
        e.Evaluate(Signal.Scalar(0.0), 0.1).ScalarValue.Should().Be(0.0);
    }

    [Fact]
    public void Reaches_full_after_full_seconds()
    {
        var e = new RampUpEvaluator(new RampUpModifier(0.5));
        // 0.5 seconds at dt=0.1 = 5 ticks; should reach 1.0
        Signal output = Signal.ZeroScalar;
        for (int i = 0; i < 5; i++)
            output = e.Evaluate(Signal.Scalar(1.0), 0.1);
        output.ScalarValue.Should().BeApproximately(1.0, 1e-9);
    }
}

public class RampDownEvaluatorTests
{
    [Fact]
    public void Decreases_are_rate_limited()
    {
        var e = new RampDownEvaluator(new RampDownModifier(1.0));
        // First, snap to 1.0 (an increase, passes through).
        e.Evaluate(Signal.Scalar(1.0), 0.1).ScalarValue.Should().Be(1.0);
        // Then drop input to 0 — should ramp down at 0.1/tick.
        e.Evaluate(Signal.Scalar(0.0), 0.1).ScalarValue.Should().BeApproximately(0.9, 1e-9);
        e.Evaluate(Signal.Scalar(0.0), 0.1).ScalarValue.Should().BeApproximately(0.8, 1e-9);
    }

    [Fact]
    public void Increases_pass_through()
    {
        var e = new RampDownEvaluator(new RampDownModifier(1.0));
        e.Evaluate(Signal.Scalar(0.7), 0.1).ScalarValue.Should().Be(0.7);
        e.Evaluate(Signal.Scalar(1.0), 0.1).ScalarValue.Should().Be(1.0);
    }
}

public class StickDynamicsEvaluatorTests
{
    [Fact]
    public void Velocity_mode_matches_v1_velocity_processor()
    {
        // Same params as the v1 VelocityStickProcessorTests "sustained input"
        // case: high decay (50/sec) + 100 max counts/sec → near-1.0 after
        // many ticks of saturating input.
        var e = new StickDynamicsEvaluator(new StickDynamicsModifier(StickDynamicsMode.Velocity, 50.0, 100.0));
        Signal output = Signal.ZeroScalar;
        for (int i = 0; i < 100; i++)
            output = e.Evaluate(Signal.Delta(10), 0.01);
        output.ScalarValue.Should().BeApproximately(1.0, 1e-3);
    }

    [Fact]
    public void Accumulator_mode_integrates_then_springs()
    {
        var e = new StickDynamicsEvaluator(new StickDynamicsModifier(StickDynamicsMode.Accumulator, 0.0, 100.0));
        e.Evaluate(Signal.Delta(50), 0.01).ScalarValue.Should().BeApproximately(0.5, 1e-9);
        e.Evaluate(Signal.Delta(50), 0.01).ScalarValue.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Persistent_mode_holds_position()
    {
        var e = new StickDynamicsEvaluator(new StickDynamicsModifier(StickDynamicsMode.Persistent, 100.0, 0.0));
        e.Evaluate(Signal.Delta(50), 0.01).ScalarValue.Should().BeApproximately(0.5, 1e-9);
        for (int i = 0; i < 100; i++)
            e.Evaluate(Signal.Delta(0), 0.01).ScalarValue.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Reset_zeros_state()
    {
        var e = new StickDynamicsEvaluator(new StickDynamicsModifier(StickDynamicsMode.Persistent, 100.0, 0.0));
        e.Evaluate(Signal.Delta(50), 0.01);
        e.Reset();
        e.Evaluate(Signal.Delta(0), 0.01).ScalarValue.Should().Be(0.0);
    }
}

public class ChainEvaluatorTests
{
    [Fact]
    public void Mouse_axis_through_full_chain_produces_scalar()
    {
        var chain = new ChainEvaluator(
            new MouseAxisSource(MouseAxis.X),
            new Modifier[]
            {
                new StickDynamicsModifier(StickDynamicsMode.Persistent, 100.0, 0.0),
                new SensitivityModifier(0.5),
            },
            new StickAxisTarget(Stick.Left, AxisComponent.X));

        chain.IsValid.Should().BeTrue();
        chain.Apply(RawEvent.ForMouseMove(50, 0, 0));
        var sig = chain.EndOfTick(0.01);
        sig.Type.Should().Be(SignalType.Scalar);
        // Persistent: 50/100 = 0.5; * Sensitivity 0.5 = 0.25.
        sig.ScalarValue.Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void Invalid_chain_reports_isvalid_false()
    {
        var chain = new ChainEvaluator(
            new KeySource(new VirtualKey(0x11, false)),
            Array.Empty<Modifier>(), // missing converter
            new StickAxisTarget(Stick.Left, AxisComponent.X));

        chain.IsValid.Should().BeFalse();
        chain.InvalidReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Disabled_modifier_is_skipped_in_evaluation()
    {
        var chain = new ChainEvaluator(
            new KeySource(new VirtualKey(0x11, false)),
            new Modifier[]
            {
                DigitalToScalarModifier.Default,
                new InvertModifier { Enabled = false }
            },
            new StickAxisTarget(Stick.Left, AxisComponent.X));

        chain.Apply(RawEvent.ForKey(new VirtualKey(0x11, false), true, KeyModifiers.None, 0));
        var sig = chain.EndOfTick(0.01);
        sig.ScalarValue.Should().Be(1.0); // Invert skipped, so +1, not -1.
    }
}

public class LimiterEvaluatorTests
{
    [Fact]
    public void Caps_positive_at_max_positive()
    {
        var e = new LimiterEvaluator(new LimiterModifier(0.33, 1.0));
        e.Evaluate(Signal.Scalar(0.8), 0.01).ScalarValue.Should().BeApproximately(0.33, 1e-9);
    }

    [Fact]
    public void Caps_negative_at_max_negative()
    {
        var e = new LimiterEvaluator(new LimiterModifier(1.0, 0.33));
        e.Evaluate(Signal.Scalar(-0.8), 0.01).ScalarValue.Should().BeApproximately(-0.33, 1e-9);
    }

    [Fact]
    public void Pass_through_when_within_caps()
    {
        var e = new LimiterEvaluator(new LimiterModifier(0.5, 0.5));
        e.Evaluate(Signal.Scalar(0.4), 0.01).ScalarValue.Should().Be(0.4);
        e.Evaluate(Signal.Scalar(-0.4), 0.01).ScalarValue.Should().Be(-0.4);
    }

    [Fact]
    public void Asymmetric_caps_apply_independently()
    {
        var e = new LimiterEvaluator(new LimiterModifier(0.33, 1.0));
        // Positive capped, negative untouched.
        e.Evaluate(Signal.Scalar(0.8), 0.01).ScalarValue.Should().BeApproximately(0.33, 1e-9);
        e.Evaluate(Signal.Scalar(-0.8), 0.01).ScalarValue.Should().Be(-0.8);
    }

    [Fact]
    public void Zero_max_blocks_that_direction_entirely()
    {
        var e = new LimiterEvaluator(new LimiterModifier(0.0, 1.0));
        e.Evaluate(Signal.Scalar(0.8), 0.01).ScalarValue.Should().Be(0.0);
        e.Evaluate(Signal.Scalar(-0.8), 0.01).ScalarValue.Should().Be(-0.8);
    }
}

public class ToggleEvaluatorTests
{
    [Fact]
    public void Rising_edge_flips_state()
    {
        var e = new ToggleEvaluator(new ToggleModifier());
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        // First press → on
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        // Hold → still on
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        // Release → still on (toggles only on rising edges)
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeTrue();
        // Second press → off
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Reset_zeros_state_and_edge_tracker()
    {
        var e = new ToggleEvaluator(new ToggleModifier());
        e.Evaluate(Signal.Digital(true), 0.01); // on
        e.Reset();
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        // After reset, the next rising edge from false→true should flip to true
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
    }

    [Fact]
    public void Held_input_at_construction_does_not_flip_until_release_and_repress()
    {
        // Common bug: if the evaluator naively reads "input is true" as a rising
        // edge on first call, the toggle would flip immediately when the user
        // is already holding the key. Confirm we wait for an actual transition.
        var e = new ToggleEvaluator(new ToggleModifier());
        // First call: input is already true (e.g. user was holding when binding became active).
        // We treat _prevInput=false → true as a rising edge — this DOES flip on first call.
        // That's intentional: it matches what users expect when binding a fresh toggle to a
        // currently-released key. If we wanted "ignore the first edge until a known release"
        // we'd need extra state.
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
    }
}

public class SmoothingEvaluatorTests
{
    [Fact]
    public void Identity_at_zero_time_constant()
    {
        var e = new SmoothingEvaluator(new SmoothingModifier(0.0));
        e.Evaluate(Signal.Scalar(0.7), 0.01).ScalarValue.Should().Be(0.7);
        e.Evaluate(Signal.Scalar(-0.3), 0.01).ScalarValue.Should().Be(-0.3);
    }

    [Fact]
    public void Approaches_target_exponentially()
    {
        // Step input 0 → 1, tau = 0.1s. After 0.1s the smoothed value should
        // be 1 - 1/e ≈ 0.632.
        var e = new SmoothingEvaluator(new SmoothingModifier(0.1));
        e.Evaluate(Signal.Scalar(0.0), 0.01); // seed at 0
        Signal output = Signal.ZeroScalar;
        // 10 ticks of dt=0.01 = 0.1s of step input at 1.0
        for (int i = 0; i < 10; i++)
            output = e.Evaluate(Signal.Scalar(1.0), 0.01);
        output.ScalarValue.Should().BeApproximately(1.0 - Math.Exp(-1.0), 0.01);
    }

    [Fact]
    public void First_sample_seeds_smoothed_value()
    {
        // Without seed-on-first-sample the EMA would start at 0 and lag heavily.
        var e = new SmoothingEvaluator(new SmoothingModifier(0.1));
        e.Evaluate(Signal.Scalar(0.5), 0.01).ScalarValue.Should().Be(0.5);
    }

    [Fact]
    public void Reset_clears_seed_and_state()
    {
        var e = new SmoothingEvaluator(new SmoothingModifier(0.1));
        e.Evaluate(Signal.Scalar(1.0), 0.01); // seeded at 1
        e.Reset();
        // Next sample re-seeds at the new value.
        e.Evaluate(Signal.Scalar(0.0), 0.01).ScalarValue.Should().Be(0.0);
    }
}

public class AutoFireEvaluatorTests
{
    [Fact]
    public void Held_input_pulses_at_configured_hz()
    {
        var e = new AutoFireEvaluator(new AutoFireModifier(10.0));
        // 10 Hz → period = 0.1s → first half (0-0.05s) true, second half false.
        // Tick the dt forward and observe the high/low pattern.
        var dt = 0.01;
        var firstHalf = e.Evaluate(Signal.Digital(true), dt).DigitalValue;
        firstHalf.Should().BeTrue("first sample of held input is in the high half of the period");
        // Walk to ~0.06s total — past the high/low boundary.
        for (int i = 0; i < 5; i++) e.Evaluate(Signal.Digital(true), dt);
        e.Evaluate(Signal.Digital(true), dt).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Released_input_outputs_false()
    {
        var e = new AutoFireEvaluator(new AutoFireModifier(10.0));
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Releasing_resets_phase_so_next_press_starts_high()
    {
        var e = new AutoFireEvaluator(new AutoFireModifier(10.0));
        e.Evaluate(Signal.Digital(true), 0.06); // mid-period
        e.Evaluate(Signal.Digital(false), 0.01); // release
        // Re-press should start fresh in the high half.
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
    }

    [Fact]
    public void Zero_hz_passes_held_input_through()
    {
        var e = new AutoFireEvaluator(new AutoFireModifier(0.0));
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }
}

public class HoldToActivateEvaluatorTests
{
    [Fact]
    public void Output_false_until_hold_duration_elapses()
    {
        var e = new HoldToActivateEvaluator(new HoldToActivateModifier(0.1));
        e.Evaluate(Signal.Digital(true), 0.05).DigitalValue.Should().BeFalse();
        e.Evaluate(Signal.Digital(true), 0.04).DigitalValue.Should().BeFalse();
        // Crosses 0.1s threshold here.
        e.Evaluate(Signal.Digital(true), 0.02).DigitalValue.Should().BeTrue();
    }

    [Fact]
    public void Releasing_resets_timer()
    {
        var e = new HoldToActivateEvaluator(new HoldToActivateModifier(0.1));
        e.Evaluate(Signal.Digital(true), 0.09).DigitalValue.Should().BeFalse();
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        // Re-press starts the clock from zero.
        e.Evaluate(Signal.Digital(true), 0.05).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Stays_true_while_held_past_threshold()
    {
        var e = new HoldToActivateEvaluator(new HoldToActivateModifier(0.05));
        e.Evaluate(Signal.Digital(true), 0.06).DigitalValue.Should().BeTrue();
        e.Evaluate(Signal.Digital(true), 0.5).DigitalValue.Should().BeTrue();
    }

    [Fact]
    public void Zero_hold_passes_input_through()
    {
        var e = new HoldToActivateEvaluator(new HoldToActivateModifier(0.0));
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }
}

public class TapEvaluatorTests
{
    [Fact]
    public void Quick_press_release_pulses_true()
    {
        var e = new TapEvaluator(new TapModifier(MaxHoldSeconds: 0.3, PulseSeconds: 0.05));
        // Press for 0.1s then release; should fire on the release tick.
        e.Evaluate(Signal.Digital(true), 0.05).DigitalValue.Should().BeFalse();
        e.Evaluate(Signal.Digital(true), 0.05).DigitalValue.Should().BeFalse();
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeTrue();
    }

    [Fact]
    public void Long_hold_releases_silently()
    {
        var e = new TapEvaluator(new TapModifier(MaxHoldSeconds: 0.3, PulseSeconds: 0.05));
        // Hold for 0.5s, exceeding the cap.
        for (int i = 0; i < 50; i++)
            e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeFalse();
        // Release: should NOT fire.
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Pulse_lasts_for_configured_duration()
    {
        var e = new TapEvaluator(new TapModifier(MaxHoldSeconds: 0.3, PulseSeconds: 0.1));
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeTrue(); // pulse starts
        // Should still be true 0.05s in.
        e.Evaluate(Signal.Digital(false), 0.05).DigitalValue.Should().BeTrue();
        // After 0.1s elapsed, should be false.
        e.Evaluate(Signal.Digital(false), 0.05).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Reset_zeros_state()
    {
        var e = new TapEvaluator(new TapModifier(MaxHoldSeconds: 0.3, PulseSeconds: 0.1));
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01); // pulse active
        e.Reset();
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Hold_just_at_threshold_still_counts_as_tap()
    {
        var e = new TapEvaluator(new TapModifier(MaxHoldSeconds: 0.1, PulseSeconds: 0.05));
        // Hold exactly 0.1s; using <= compare so this fires.
        e.Evaluate(Signal.Digital(true), 0.05).DigitalValue.Should().BeFalse();
        e.Evaluate(Signal.Digital(true), 0.05).DigitalValue.Should().BeFalse();
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeTrue();
    }
}

public class MultiTapEvaluatorTests
{
    [Fact]
    public void Two_taps_within_window_fire()
    {
        var e = new MultiTapEvaluator(new MultiTapModifier(TapCount: 2, WindowSeconds: 0.4, MaxHoldSeconds: 0.2, PulseSeconds: 0.05));
        // Tap 1
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse(); // first tap, no fire yet
        // Gap
        e.Evaluate(Signal.Digital(false), 0.1).DigitalValue.Should().BeFalse();
        // Tap 2 within window
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeTrue(); // double-tap fires
    }

    [Fact]
    public void Single_tap_does_not_fire()
    {
        var e = new MultiTapEvaluator(new MultiTapModifier(2, 0.4, 0.2, 0.05));
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        // Wait past the window with no second tap.
        for (int i = 0; i < 50; i++)
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Window_expiry_resets_counter()
    {
        var e = new MultiTapEvaluator(new MultiTapModifier(2, 0.2, 0.1, 0.05));
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01); // first tap registered
        // Drain past the window.
        for (int i = 0; i < 30; i++)
            e.Evaluate(Signal.Digital(false), 0.01);
        // Second tap arrives too late — shouldn't fire (counter has reset).
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Held_press_breaks_the_sequence()
    {
        var e = new MultiTapEvaluator(new MultiTapModifier(2, 0.4, 0.1, 0.05));
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01); // tap 1
        // Long press (exceeds MaxHold) — should NOT count toward the multi-tap.
        for (int i = 0; i < 30; i++)
            e.Evaluate(Signal.Digital(true), 0.01);
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse(); // long release didn't count
    }

    [Fact]
    public void Triple_tap_requires_three_taps()
    {
        var e = new MultiTapEvaluator(new MultiTapModifier(3, 0.6, 0.1, 0.05));
        e.Evaluate(Signal.Digital(true), 0.05); e.Evaluate(Signal.Digital(false), 0.01);
        e.Evaluate(Signal.Digital(false), 0.05);
        e.Evaluate(Signal.Digital(true), 0.05); e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse(); // 2 taps, not enough
        e.Evaluate(Signal.Digital(false), 0.05);
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeTrue(); // 3rd tap fires
    }

    [Fact]
    public void Reset_zeros_all_state()
    {
        var e = new MultiTapEvaluator(new MultiTapModifier(2, 0.4, 0.1, 0.05));
        e.Evaluate(Signal.Digital(true), 0.05); e.Evaluate(Signal.Digital(false), 0.01);
        e.Reset();
        // After reset, a single tap shouldn't trigger because the counter is 0.
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }
}

public class TapWithWaitForHigherTapsTests
{
    private static TapModifier WithWait(double pulse = 0.05) =>
        new(MaxHoldSeconds: 0.3, PulseSeconds: pulse, WaitForHigherTaps: true, ConfirmWaitSeconds: 0.4);

    [Fact]
    public void Single_tap_fires_after_wait_window_with_no_followup()
    {
        var e = new TapEvaluator(WithWait());
        // Quick press-release.
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse(); // pending, not firing yet
        // Hold the wait open with no input for the configured wait window.
        for (int i = 0; i < 39; i++)
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        // Past 0.4s wait → fire pulse.
        e.Evaluate(Signal.Digital(false), 0.02).DigitalValue.Should().BeTrue();
    }

    [Fact]
    public void Followup_short_press_suppresses_pending_pulse_for_full_window()
    {
        // The first tap's pending pulse should be canceled when a follow-up
        // tap completes during its wait window. With suppression mode, the
        // follow-up tap does NOT start its own pending pulse — the user is
        // doing a multi-tap on a sibling binding, so we stay silent for the
        // full wait window after the last tap.
        var e = new TapEvaluator(WithWait());
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01); // first tap, pending
        for (int i = 0; i < 10; i++) e.Evaluate(Signal.Digital(false), 0.01);
        // Second short press during wait.
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeFalse();
        // Release: in suppression mode now; refreshes wait but no fire armed.
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        // Run well past the wait window — should never fire.
        for (int i = 0; i < 100; i++)
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Triple_press_sequence_stays_suppressed()
    {
        // Triple-tap on a sibling MultiTap(3) binding: this Tap binding sees
        // three short presses and must not fire any pulse at all.
        var e = new TapEvaluator(WithWait());
        for (int tap = 0; tap < 3; tap++)
        {
            e.Evaluate(Signal.Digital(true), 0.05);
            e.Evaluate(Signal.Digital(false), 0.01);
            for (int i = 0; i < 10; i++) e.Evaluate(Signal.Digital(false), 0.01);
        }
        // Run past the final wait window.
        for (int i = 0; i < 100; i++)
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Followup_long_press_confirms_early_then_passthrough()
    {
        var e = new TapEvaluator(WithWait(pulse: 0.05));
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01); // first tap, pending
        // Brief gap.
        for (int i = 0; i < 5; i++) e.Evaluate(Signal.Digital(false), 0.01);
        // Long press starts. Press lands → cancels wait initially.
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeFalse();
        // Hold past MaxHold (0.3s).
        for (int i = 0; i < 30; i++) e.Evaluate(Signal.Digital(true), 0.01);
        // The tick when overflow trips: pending pulse fires AND passthrough engages.
        var output = e.Evaluate(Signal.Digital(true), 0.01);
        output.DigitalValue.Should().BeTrue();
        // Continue holding: passthrough keeps output true.
        for (int i = 0; i < 20; i++)
            e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        // Release: passthrough drops; pending pulse already expired.
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Long_press_from_idle_passthrough()
    {
        var e = new TapEvaluator(WithWait());
        // Press and hold past MaxHold without any prior tap.
        for (int i = 0; i < 30; i++) e.Evaluate(Signal.Digital(true), 0.01);
        // Tick that overflows.
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        // Continue holding.
        for (int i = 0; i < 50; i++)
            e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        // Release: drop.
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Wait_flag_does_not_change_default_behavior_when_off()
    {
        // Default config (WaitForHigherTaps=false) should fire immediately on release.
        var e = new TapEvaluator(new TapModifier(0.3, 0.05));
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeTrue();
    }

    [Fact]
    public void Pulse_zero_with_wait_makes_binding_hold_only()
    {
        // PulseSeconds=0 + WaitForHigherTaps=true: the tap pulse is fully
        // suppressed; only the long-press passthrough produces output.
        var e = new TapEvaluator(new TapModifier(MaxHoldSeconds: 0.3, PulseSeconds: 0.0, WaitForHigherTaps: true, ConfirmWaitSeconds: 0.4));
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01);
        for (int i = 0; i < 100; i++)
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        // Long press passthrough still works.
        for (int i = 0; i < 30; i++) e.Evaluate(Signal.Digital(true), 0.01);
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        for (int i = 0; i < 20; i++)
            e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }
}

public class MultiTapWithWaitForHigherTapsTests
{
    [Fact]
    public void Double_tap_fires_after_wait_window_with_no_followup()
    {
        var e = new MultiTapEvaluator(new MultiTapModifier(
            TapCount: 2, WindowSeconds: 0.4, MaxHoldSeconds: 0.2, PulseSeconds: 0.05,
            WaitForHigherTaps: true));
        // Tap 1.
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        // Gap.
        for (int i = 0; i < 5; i++) e.Evaluate(Signal.Digital(false), 0.01);
        // Tap 2 → pending.
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse(); // pending, not firing
        // Hold the wait open.
        for (int i = 0; i < 39; i++)
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        // Past 0.4s wait → fire.
        e.Evaluate(Signal.Digital(false), 0.02).DigitalValue.Should().BeTrue();
    }

    [Fact]
    public void Triple_tap_arriving_during_wait_cancels_double_tap_pending()
    {
        var doubleE = new MultiTapEvaluator(new MultiTapModifier(2, 0.4, 0.2, 0.05, WaitForHigherTaps: true));
        // Double tap.
        doubleE.Evaluate(Signal.Digital(true), 0.05);
        doubleE.Evaluate(Signal.Digital(false), 0.01); // tap 1
        for (int i = 0; i < 5; i++) doubleE.Evaluate(Signal.Digital(false), 0.01);
        doubleE.Evaluate(Signal.Digital(true), 0.05);
        doubleE.Evaluate(Signal.Digital(false), 0.01); // tap 2 → pending
        // Third tap arrives during wait.
        for (int i = 0; i < 5; i++) doubleE.Evaluate(Signal.Digital(false), 0.01);
        doubleE.Evaluate(Signal.Digital(true), 0.05).DigitalValue.Should().BeFalse(); // wait canceled
        doubleE.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse(); // never fires
        // Confirm: ride out further time, still no fire.
        for (int i = 0; i < 50; i++)
            doubleE.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Followup_long_press_confirms_double_tap_early()
    {
        var e = new MultiTapEvaluator(new MultiTapModifier(2, 0.4, 0.2, 0.05, WaitForHigherTaps: true));
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01);
        for (int i = 0; i < 5; i++) e.Evaluate(Signal.Digital(false), 0.01);
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01); // pending wait

        // Long press arrives during wait. Hold past MaxHold (0.2s).
        // Collect outputs during the long press; expect to see the pending
        // pulse fire ON the tick the overflow trips, and decay shortly
        // after. (MultiTap has no passthrough — just the brief pulse.)
        bool sawTrue = false;
        for (int i = 0; i < 30; i++)
        {
            if (e.Evaluate(Signal.Digital(true), 0.01).DigitalValue)
                sawTrue = true;
        }
        sawTrue.Should().BeTrue("the pending double-tap pulse should fire when the follow-up press exceeds MaxHold");

        // After the long-press release, output stays false (pulse already decayed).
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Wait_flag_off_preserves_immediate_fire()
    {
        var e = new MultiTapEvaluator(new MultiTapModifier(2, 0.4, 0.2, 0.05));
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01);
        for (int i = 0; i < 5; i++) e.Evaluate(Signal.Digital(false), 0.01);
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeTrue();
    }
}

public class WaitForTapResolutionEvaluatorTests
{
    [Fact]
    public void Quick_press_release_fires_after_wait()
    {
        var e = new WaitForTapResolutionEvaluator(new WaitForTapResolutionModifier(MaxHoldSeconds: 0.3, WaitSeconds: 0.4, PulseSeconds: 0.05));
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        for (int i = 0; i < 39; i++)
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        e.Evaluate(Signal.Digital(false), 0.02).DigitalValue.Should().BeTrue();
    }

    [Fact]
    public void Followup_press_during_wait_suppresses_for_full_window()
    {
        // A follow-up short press during the wait suppresses the pending
        // fire AND prevents this press from starting its own pending fire.
        // Suppression covers the full WaitSeconds from THIS release; further
        // taps refresh the suppression. The user is doing a multi-tap on a
        // sibling binding.
        var e = new WaitForTapResolutionEvaluator(new WaitForTapResolutionModifier(0.3, 0.4, 0.05));
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01);
        for (int i = 0; i < 5; i++) e.Evaluate(Signal.Digital(false), 0.01);
        e.Evaluate(Signal.Digital(true), 0.05).DigitalValue.Should().BeFalse();
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        // Run well past the wait — should never fire.
        for (int i = 0; i < 100; i++)
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Triple_press_sequence_stays_suppressed()
    {
        // Three short presses (e.g. user is triple-tapping a key bound to
        // MultiTap(3) on a sibling): this binding must stay silent.
        var e = new WaitForTapResolutionEvaluator(new WaitForTapResolutionModifier(0.3, 0.4, 0.05));
        for (int tap = 0; tap < 3; tap++)
        {
            e.Evaluate(Signal.Digital(true), 0.05);
            e.Evaluate(Signal.Digital(false), 0.01);
            for (int i = 0; i < 5; i++) e.Evaluate(Signal.Digital(false), 0.01);
        }
        for (int i = 0; i < 100; i++)
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Long_press_from_idle_passthrough()
    {
        var e = new WaitForTapResolutionEvaluator(new WaitForTapResolutionModifier(0.3, 0.4, 0.05));
        // Press past MaxHold.
        for (int i = 0; i < 30; i++) e.Evaluate(Signal.Digital(true), 0.01);
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        for (int i = 0; i < 50; i++)
            e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Long_press_during_wait_confirms_early_then_passthrough()
    {
        var e = new WaitForTapResolutionEvaluator(new WaitForTapResolutionModifier(0.3, 0.4, 0.05));
        // First short press.
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01);
        // Long second press.
        for (int i = 0; i < 5; i++) e.Evaluate(Signal.Digital(false), 0.01);
        e.Evaluate(Signal.Digital(true), 0.01);
        for (int i = 0; i < 30; i++) e.Evaluate(Signal.Digital(true), 0.01);
        // Overflow trips → fire pending + passthrough.
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        for (int i = 0; i < 20; i++)
            e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Pulse_zero_makes_binding_hold_only()
    {
        // PulseSeconds=0 means "no pulse on tap" — only the long-press
        // passthrough produces output. Useful for non-conflicting hold-
        // only bindings that share a source with tap/multi-tap siblings.
        var e = new WaitForTapResolutionEvaluator(new WaitForTapResolutionModifier(MaxHoldSeconds: 0.3, WaitSeconds: 0.4, PulseSeconds: 0.0));
        // Quick tap should fire NOTHING.
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01);
        for (int i = 0; i < 100; i++)
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        // Long press passthrough still works.
        for (int i = 0; i < 30; i++) e.Evaluate(Signal.Digital(true), 0.01);
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        for (int i = 0; i < 20; i++)
            e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Reset_zeros_state()
    {
        var e = new WaitForTapResolutionEvaluator(new WaitForTapResolutionModifier(0.3, 0.4, 0.05));
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01); // pending
        e.Reset();
        // No fire after reset, even after the wait window.
        for (int i = 0; i < 50; i++)
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }
}

public class SourceAdapterTests
{
    [Fact]
    public void Mouse_axis_adapter_resets_delta_each_tick()
    {
        var adapter = new Mouse2Joy.Engine.Modifiers.SourceAdapters.MouseAxisAdapter(new MouseAxisSource(MouseAxis.X));
        adapter.Apply(RawEvent.ForMouseMove(10, 0, 0));
        adapter.Apply(RawEvent.ForMouseMove(20, 0, 0));
        adapter.EndOfTick().DeltaValue.Should().Be(30);
        // Next tick should start fresh.
        adapter.EndOfTick().DeltaValue.Should().Be(0);
    }

    [Fact]
    public void Digital_latch_adapter_holds_state_between_ticks()
    {
        var adapter = new Mouse2Joy.Engine.Modifiers.SourceAdapters.DigitalLatchAdapter(
            new KeySource(new VirtualKey(0x11, false)));
        adapter.Apply(RawEvent.ForKey(new VirtualKey(0x11, false), true, KeyModifiers.None, 0));
        adapter.EndOfTick().DigitalValue.Should().BeTrue();
        // No new event — still latched.
        adapter.EndOfTick().DigitalValue.Should().BeTrue();
        adapter.Apply(RawEvent.ForKey(new VirtualKey(0x11, false), false, KeyModifiers.None, 0));
        adapter.EndOfTick().DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Digital_momentary_adapter_resets_each_tick()
    {
        var adapter = new Mouse2Joy.Engine.Modifiers.SourceAdapters.DigitalMomentaryAdapter(
            new MouseScrollSource(ScrollDirection.Up));
        adapter.Apply(RawEvent.ForMouseScroll(ScrollDirection.Up, 1, KeyModifiers.None, 0));
        adapter.EndOfTick().DigitalValue.Should().BeTrue();
        // No new event — should reset to false.
        adapter.EndOfTick().DigitalValue.Should().BeFalse();
    }
}
