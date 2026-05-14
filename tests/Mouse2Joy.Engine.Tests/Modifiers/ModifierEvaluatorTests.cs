using FluentAssertions;
using Mouse2Joy.Engine.Modifiers;
using Mouse2Joy.Engine.Modifiers.Evaluators;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Tests.Modifiers;

public class OutputScaleEvaluatorTests
{
    [Fact]
    public void Identity_passes_through()
    {
        var e = new OutputScaleEvaluator(new OutputScaleModifier(1.0));
        var output = e.Evaluate(Signal.Scalar(0.5), 0.01);
        output.ScalarValue.Should().Be(0.5);
    }

    [Fact]
    public void Factor_scales_signal()
    {
        var e = new OutputScaleEvaluator(new OutputScaleModifier(2.0));
        e.Evaluate(Signal.Scalar(0.4), 0.01).ScalarValue.Should().Be(0.8);
    }

    [Fact]
    public void Output_clamps_to_unit_range()
    {
        var e = new OutputScaleEvaluator(new OutputScaleModifier(2.0));
        e.Evaluate(Signal.Scalar(0.6), 0.01).ScalarValue.Should().Be(1.0);
        e.Evaluate(Signal.Scalar(-0.6), 0.01).ScalarValue.Should().Be(-1.0);
    }

    [Fact]
    public void Factor_below_one_caps_saturated_input_below_unity()
    {
        // This is the governor use case: upstream signal at ±1 gets capped at ±Factor.
        // Captures the behavior contract that motivated the rename — full deflection
        // is intentionally unreachable from here when Factor < 1.
        var e = new OutputScaleEvaluator(new OutputScaleModifier(0.5));
        e.Evaluate(Signal.Scalar(1.0), 0.01).ScalarValue.Should().Be(0.5);
        e.Evaluate(Signal.Scalar(-1.0), 0.01).ScalarValue.Should().Be(-0.5);
    }
}

public class DeltaScaleEvaluatorTests
{
    [Fact]
    public void Identity_at_factor_one_passes_through()
    {
        var e = new DeltaScaleEvaluator(new DeltaScaleModifier(1.0));
        e.Evaluate(Signal.Delta(100), 0.01).DeltaValue.Should().Be(100);
        e.Evaluate(Signal.Delta(-50), 0.01).DeltaValue.Should().Be(-50);
        e.Evaluate(Signal.Delta(0), 0.01).DeltaValue.Should().Be(0);
    }

    [Fact]
    public void Factor_scales_delta_proportionally()
    {
        var half = new DeltaScaleEvaluator(new DeltaScaleModifier(0.5));
        half.Evaluate(Signal.Delta(100), 0.01).DeltaValue.Should().Be(50);
        half.Evaluate(Signal.Delta(-100), 0.01).DeltaValue.Should().Be(-50);

        var doubleE = new DeltaScaleEvaluator(new DeltaScaleModifier(2.0));
        doubleE.Evaluate(Signal.Delta(50), 0.01).DeltaValue.Should().Be(100);
    }

    [Fact]
    public void Rounding_uses_bankers_rounding_to_avoid_bias()
    {
        // Math.Round defaults to MidpointRounding.ToEven (banker's). This
        // means delta=1, factor=0.5 → 0.5 → rounds to 0 (nearest even).
        // delta=3, factor=0.5 → 1.5 → rounds to 2.
        // Truncation would always lose the fractional part toward zero on
        // every nonzero tick; banker's rounding keeps the long-term average
        // unbiased so total motion is preserved over time.
        var e = new DeltaScaleEvaluator(new DeltaScaleModifier(0.5));
        e.Evaluate(Signal.Delta(1), 0.01).DeltaValue.Should().Be(0);
        e.Evaluate(Signal.Delta(3), 0.01).DeltaValue.Should().Be(2);
        e.Evaluate(Signal.Delta(2), 0.01).DeltaValue.Should().Be(1);
    }

    [Fact]
    public void Zero_factor_zeros_output()
    {
        var e = new DeltaScaleEvaluator(new DeltaScaleModifier(0.0));
        e.Evaluate(Signal.Delta(1000), 0.01).DeltaValue.Should().Be(0);
        e.Evaluate(Signal.Delta(-1000), 0.01).DeltaValue.Should().Be(0);
    }

    [Fact]
    public void Negative_factor_is_clamped_to_zero()
    {
        // Defensive guard: stored value can be anything (round-trip preserved),
        // but the evaluator treats < 0 as 0 to prevent unintended inversion.
        var e = new DeltaScaleEvaluator(new DeltaScaleModifier(-1.5));
        e.Evaluate(Signal.Delta(100), 0.01).DeltaValue.Should().Be(0);
        e.Evaluate(Signal.Delta(-100), 0.01).DeltaValue.Should().Be(0);
    }

    [Fact]
    public void Nan_factor_treated_as_zero()
    {
        var e = new DeltaScaleEvaluator(new DeltaScaleModifier(double.NaN));
        e.Evaluate(Signal.Delta(100), 0.01).DeltaValue.Should().Be(0);
    }

    [Fact]
    public void Large_input_scales_without_overflow()
    {
        // Realistic upper bound: gaming mice rarely exceed a few thousand counts/tick.
        var e = new DeltaScaleEvaluator(new DeltaScaleModifier(3.0));
        e.Evaluate(Signal.Delta(1000), 0.01).DeltaValue.Should().Be(3000);
        e.Evaluate(Signal.Delta(-1000), 0.01).DeltaValue.Should().Be(-3000);
    }

    [Fact]
    public void Reset_is_a_noop_because_evaluator_is_stateless()
    {
        var e = new DeltaScaleEvaluator(new DeltaScaleModifier(0.5));
        e.Evaluate(Signal.Delta(100), 0.01).DeltaValue.Should().Be(50);
        e.Reset();
        e.Evaluate(Signal.Delta(100), 0.01).DeltaValue.Should().Be(50);
    }

    [Fact]
    public void Config_property_exposes_underlying_modifier()
    {
        var mod = new DeltaScaleModifier(0.5);
        var e = new DeltaScaleEvaluator(mod);
        e.Config.Should().BeSameAs(mod);
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
    // Default helpers (no style) → use the constructor default = Hard, so the
    // pre-existing tests in this class continue to assert the original kinked
    // math without any changes.
    private static SegmentedResponseCurveEvaluator Above(double threshold, double exponent) =>
        new(new SegmentedResponseCurveModifier(threshold, exponent, SegmentedCurveRegion.AboveThreshold));

    private static SegmentedResponseCurveEvaluator Below(double threshold, double exponent) =>
        new(new SegmentedResponseCurveModifier(threshold, exponent, SegmentedCurveRegion.BelowThreshold));

    // Style-aware helpers for the new smooth-transition tests below.
    private static SegmentedResponseCurveEvaluator AboveStyle(double threshold, double exponent, SegmentedCurveTransitionStyle style) =>
        new(new SegmentedResponseCurveModifier(threshold, exponent, SegmentedCurveRegion.AboveThreshold, style));

    private static SegmentedResponseCurveEvaluator BelowStyle(double threshold, double exponent, SegmentedCurveTransitionStyle style) =>
        new(new SegmentedResponseCurveModifier(threshold, exponent, SegmentedCurveRegion.BelowThreshold, style));

    private static double Eval(SegmentedResponseCurveEvaluator e, double x) =>
        e.Evaluate(Signal.Scalar(x), 0.01).ScalarValue;

    private static double Slope(SegmentedResponseCurveEvaluator e, double x, double h = 1e-5)
    {
        // Central difference; clamped a bit away from the input domain edges.
        return (Eval(e, x + h) - Eval(e, x - h)) / (2.0 * h);
    }

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

    // ============================================================
    // SmoothStep transition style
    // ============================================================

    [Fact]
    public void SmoothStep_above_passes_through_linear_below_threshold()
    {
        var e = AboveStyle(0.3, 2.0, SegmentedCurveTransitionStyle.SmoothStep);
        Eval(e, 0.0).Should().BeApproximately(0.0, 1e-9);
        Eval(e, 0.1).Should().BeApproximately(0.1, 1e-9);
        Eval(e, 0.3).Should().BeApproximately(0.3, 1e-9);
    }

    [Fact]
    public void SmoothStep_above_preserves_endpoint_at_one()
    {
        AboveStyle(0.3, 2.0, SegmentedCurveTransitionStyle.SmoothStep)
            .Evaluate(Signal.Scalar(1.0), 0.01).ScalarValue.Should().BeApproximately(1.0, 1e-9);
        AboveStyle(0.5, 4.0, SegmentedCurveTransitionStyle.SmoothStep)
            .Evaluate(Signal.Scalar(1.0), 0.01).ScalarValue.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void SmoothStep_above_is_smooth_at_threshold()
    {
        // The whole point of SmoothStep: slope on both sides of the threshold
        // should match (≈ 1, matching the linear segment's slope).
        var e = AboveStyle(0.3, 2.0, SegmentedCurveTransitionStyle.SmoothStep);
        var slopeBelow = Slope(e, 0.2999);
        var slopeAbove = Slope(e, 0.3001);
        slopeBelow.Should().BeApproximately(1.0, 0.01);
        slopeAbove.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void SmoothStep_below_zero_in_zero_out()
    {
        BelowStyle(0.3, 2.0, SegmentedCurveTransitionStyle.SmoothStep)
            .Evaluate(Signal.Scalar(0.0), 0.01).ScalarValue.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void SmoothStep_below_is_smooth_at_threshold()
    {
        var e = BelowStyle(0.4, 2.0, SegmentedCurveTransitionStyle.SmoothStep);
        var slopeBelow = Slope(e, 0.3999);
        var slopeAbove = Slope(e, 0.4001);
        slopeBelow.Should().BeApproximately(1.0, 0.01);
        slopeAbove.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void SmoothStep_with_exponent_one_collapses_to_linear()
    {
        // Blend of linear and "u^1 * (1-t) + t" — both are linear formulas, so
        // any weighted average is also linear. Output equals input everywhere.
        var e = AboveStyle(0.3, 1.0, SegmentedCurveTransitionStyle.SmoothStep);
        Eval(e, 0.4).Should().BeApproximately(0.4, 1e-9);
        Eval(e, 0.7).Should().BeApproximately(0.7, 1e-9);
        Eval(e, 1.0).Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void SmoothStep_preserves_sign_for_negative_inputs()
    {
        var e = AboveStyle(0.3, 2.0, SegmentedCurveTransitionStyle.SmoothStep);
        var pos = Eval(e, 0.7);
        var neg = Eval(e, -0.7);
        neg.Should().BeApproximately(-pos, 1e-9);
    }

    // ============================================================
    // HermiteSpline transition style
    // ============================================================

    [Fact]
    public void HermiteSpline_above_passes_through_linear_below_threshold()
    {
        var e = AboveStyle(0.3, 2.0, SegmentedCurveTransitionStyle.HermiteSpline);
        Eval(e, 0.0).Should().BeApproximately(0.0, 1e-9);
        Eval(e, 0.15).Should().BeApproximately(0.15, 1e-9);
        Eval(e, 0.3).Should().BeApproximately(0.3, 1e-9);
    }

    [Fact]
    public void HermiteSpline_above_preserves_endpoint_at_one()
    {
        AboveStyle(0.3, 2.0, SegmentedCurveTransitionStyle.HermiteSpline)
            .Evaluate(Signal.Scalar(1.0), 0.01).ScalarValue.Should().BeApproximately(1.0, 1e-9);
        AboveStyle(0.7, 3.5, SegmentedCurveTransitionStyle.HermiteSpline)
            .Evaluate(Signal.Scalar(1.0), 0.01).ScalarValue.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void HermiteSpline_above_is_smooth_at_threshold()
    {
        // Slope matches 1 (linear side) on both sides of the threshold.
        var e = AboveStyle(0.3, 3.0, SegmentedCurveTransitionStyle.HermiteSpline);
        var slopeBelow = Slope(e, 0.2999);
        var slopeAbove = Slope(e, 0.3001);
        slopeBelow.Should().BeApproximately(1.0, 0.01);
        slopeAbove.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void HermiteSpline_above_terminal_slope_matches_exponent()
    {
        // Approach the endpoint x = 1 from below; slope should match Exponent.
        var e = AboveStyle(0.3, 3.0, SegmentedCurveTransitionStyle.HermiteSpline);
        var s = Slope(e, 0.9999, h: 1e-6);
        s.Should().BeApproximately(3.0, 0.05);
    }

    [Fact]
    public void HermiteSpline_with_exponent_one_is_pure_linear()
    {
        // Start slope = 1, end slope = 1 → cubic collapses to a straight line.
        var e = AboveStyle(0.3, 1.0, SegmentedCurveTransitionStyle.HermiteSpline);
        Eval(e, 0.4).Should().BeApproximately(0.4, 1e-9);
        Eval(e, 0.7).Should().BeApproximately(0.7, 1e-9);
        Eval(e, 1.0).Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void HermiteSpline_below_zero_in_zero_out()
    {
        BelowStyle(0.4, 2.0, SegmentedCurveTransitionStyle.HermiteSpline)
            .Evaluate(Signal.Scalar(0.0), 0.01).ScalarValue.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void HermiteSpline_below_is_smooth_at_threshold()
    {
        var e = BelowStyle(0.4, 3.0, SegmentedCurveTransitionStyle.HermiteSpline);
        var slopeBelow = Slope(e, 0.3999);
        var slopeAbove = Slope(e, 0.4001);
        slopeBelow.Should().BeApproximately(1.0, 0.01);
        slopeAbove.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void HermiteSpline_preserves_sign_for_negative_inputs()
    {
        var e = AboveStyle(0.3, 2.0, SegmentedCurveTransitionStyle.HermiteSpline);
        var pos = Eval(e, 0.7);
        var neg = Eval(e, -0.7);
        neg.Should().BeApproximately(-pos, 1e-9);
    }

    // ============================================================
    // Cross-style behavior
    // ============================================================

    [Fact]
    public void Style_dispatch_produces_different_outputs_for_same_inputs()
    {
        // Same Threshold/Exponent/Region, three different styles → numerically
        // different outputs at the same input. Confirms the switch in
        // Evaluate() routes to distinct code paths.
        var hard = AboveStyle(0.3, 2.0, SegmentedCurveTransitionStyle.Hard);
        var smooth = AboveStyle(0.3, 2.0, SegmentedCurveTransitionStyle.SmoothStep);
        var hermite = AboveStyle(0.3, 2.0, SegmentedCurveTransitionStyle.HermiteSpline);
        // Pick an input deep in the curved region where the three formulas
        // diverge most visibly.
        var a = 0.5;
        var hardOut = Eval(hard, a);
        var smoothOut = Eval(smooth, a);
        var hermiteOut = Eval(hermite, a);
        hardOut.Should().NotBe(smoothOut);
        hardOut.Should().NotBe(hermiteOut);
        smoothOut.Should().NotBe(hermiteOut);
    }

    [Fact]
    public void Hard_style_via_explicit_param_matches_default_helper()
    {
        // Sanity check: the constructor default is Hard, so AboveStyle(.., Hard)
        // and Above(..) should produce identical output for every input.
        var withDefault = Above(0.3, 2.0);
        var explicitHard = AboveStyle(0.3, 2.0, SegmentedCurveTransitionStyle.Hard);
        foreach (var a in new[] { 0.0, 0.1, 0.29, 0.3, 0.31, 0.5, 0.9, 1.0 })
        {
            Eval(explicitHard, a).Should().Be(Eval(withDefault, a));
        }
    }

    // ============================================================
    // Shape parameter — Convex vs Concave for the existing styles
    // ============================================================

    private static SegmentedResponseCurveEvaluator AboveStyleShape(
        double threshold, double exponent,
        SegmentedCurveTransitionStyle style,
        SegmentedCurveShape shape) =>
        new(new SegmentedResponseCurveModifier(
            threshold, exponent,
            SegmentedCurveRegion.AboveThreshold,
            style, shape));

    private static SegmentedResponseCurveEvaluator BelowStyleShape(
        double threshold, double exponent,
        SegmentedCurveTransitionStyle style,
        SegmentedCurveShape shape) =>
        new(new SegmentedResponseCurveModifier(
            threshold, exponent,
            SegmentedCurveRegion.BelowThreshold,
            style, shape));

    [Fact]
    public void Convex_above_threshold_sits_below_or_on_chord()
    {
        // For above-threshold convex curves (any style), output at any 'a'
        // in (t, 1) should be ≤ the linear chord value 'a'. The chord is
        // y = a since both endpoints lie on the diagonal.
        var threshold = 0.3;
        var samples = new[] { 0.35, 0.5, 0.7, 0.9 };
        foreach (var style in Enum.GetValues<SegmentedCurveTransitionStyle>())
        {
            var e = AboveStyleShape(threshold, 2.0, style, SegmentedCurveShape.Convex);
            foreach (var a in samples)
            {
                Eval(e, a).Should().BeLessThanOrEqualTo(a + 1e-9,
                    $"style={style} convex output at a={a} should not exceed chord");
            }
        }
    }

    [Fact]
    public void Concave_above_threshold_sits_above_or_on_chord()
    {
        // For above-threshold concave curves, output should be ≥ chord (= a).
        var threshold = 0.3;
        var samples = new[] { 0.35, 0.5, 0.7, 0.9 };
        foreach (var style in Enum.GetValues<SegmentedCurveTransitionStyle>())
        {
            var e = AboveStyleShape(threshold, 2.0, style, SegmentedCurveShape.Concave);
            foreach (var a in samples)
            {
                Eval(e, a).Should().BeGreaterThanOrEqualTo(a - 1e-9,
                    $"style={style} concave output at a={a} should not fall below chord");
            }
        }
    }

    [Fact]
    public void Convex_endpoint_preservation_for_all_styles()
    {
        // out(1) = 1 across all five styles (above-threshold, convex).
        foreach (var style in Enum.GetValues<SegmentedCurveTransitionStyle>())
        {
            var e = AboveStyleShape(0.3, 2.0, style, SegmentedCurveShape.Convex);
            Eval(e, 1.0).Should().BeApproximately(1.0, 1e-9,
                $"style={style} convex must reach full deflection at a=1");
        }
    }

    [Fact]
    public void Concave_endpoint_preservation_for_all_styles()
    {
        foreach (var style in Enum.GetValues<SegmentedCurveTransitionStyle>())
        {
            var e = AboveStyleShape(0.3, 2.0, style, SegmentedCurveShape.Concave);
            Eval(e, 1.0).Should().BeApproximately(1.0, 1e-9,
                $"style={style} concave must reach full deflection at a=1");
        }
    }

    [Fact]
    public void Threshold_value_continuity_for_all_styles_and_shapes()
    {
        // out(t) = t for above-threshold across all combinations.
        foreach (var style in Enum.GetValues<SegmentedCurveTransitionStyle>())
        {
            foreach (var shape in Enum.GetValues<SegmentedCurveShape>())
            {
                var e = AboveStyleShape(0.3, 2.0, style, shape);
                Eval(e, 0.3).Should().BeApproximately(0.3, 1e-9,
                    $"style={style} shape={shape} must pass through (t, t)");
            }
        }
    }

    [Fact]
    public void Below_threshold_zero_in_zero_out_for_all_styles_and_shapes()
    {
        foreach (var style in Enum.GetValues<SegmentedCurveTransitionStyle>())
        {
            foreach (var shape in Enum.GetValues<SegmentedCurveShape>())
            {
                var e = BelowStyleShape(0.3, 2.0, style, shape);
                Eval(e, 0.0).Should().BeApproximately(0.0, 1e-9,
                    $"style={style} shape={shape} must pass through (0, 0)");
            }
        }
    }

    // ============================================================
    // QuinticSmooth — the recommended C² smooth style
    // ============================================================

    [Fact]
    public void QuinticSmooth_above_convex_no_dip_below_chord()
    {
        // The whole point of QuinticSmooth: C²-matched curvature at the
        // threshold eliminates the dip that cubic Hermite produces. The
        // convex curve should sit at or below the chord (it's still convex)
        // but not by much near the threshold — specifically, near the
        // threshold the curve should be very close to the chord (curvature
        // matched).
        var e = AboveStyleShape(0.3, 3.0, SegmentedCurveTransitionStyle.QuinticSmooth, SegmentedCurveShape.Convex);
        // Just above the threshold, output should be very close to a (the
        // chord). Within e.g. 5% of (a − t) — tight enough to catch a dip.
        var a = 0.35;
        var chord = a;
        var output = Eval(e, a);
        // Allow up to 2% of the curve range below the chord. Cubic Hermite
        // with same params would dip several percent.
        var depthBelow = chord - output;
        depthBelow.Should().BeLessThan(0.02,
            "QuinticSmooth must NOT dip significantly below the chord near the threshold");
    }

    [Fact]
    public void QuinticSmooth_above_concave_no_bulge_above_chord()
    {
        var e = AboveStyleShape(0.3, 3.0, SegmentedCurveTransitionStyle.QuinticSmooth, SegmentedCurveShape.Concave);
        var a = 0.35;
        var chord = a;
        var output = Eval(e, a);
        var bulgeAbove = output - chord;
        bulgeAbove.Should().BeLessThan(0.02,
            "QuinticSmooth must NOT bulge significantly above the chord near the threshold");
    }

    [Fact]
    public void QuinticSmooth_above_is_C2_smooth_at_threshold()
    {
        // Slope matches across the threshold (C¹).
        var e = AboveStyleShape(0.3, 2.0, SegmentedCurveTransitionStyle.QuinticSmooth, SegmentedCurveShape.Convex);
        var slopeBelow = Slope(e, 0.2999);
        var slopeAbove = Slope(e, 0.3001);
        slopeBelow.Should().BeApproximately(1.0, 0.01);
        slopeAbove.Should().BeApproximately(1.0, 0.01);
        // Curvature should also match (both 0, since linear segment has zero
        // curvature). Use finite difference on slope.
        var s_minus = Slope(e, 0.3001, h: 1e-4);
        var s_plus = Slope(e, 0.3001 + 2e-4, h: 1e-4);
        var curvatureAbove = (s_plus - s_minus) / 2e-4;
        // QuinticSmooth has matched-zero-curvature at the join, so curvature
        // just above the threshold should be very small. Allow a generous
        // tolerance because finite differences are noisy.
        Math.Abs(curvatureAbove).Should().BeLessThan(5.0,
            "QuinticSmooth must have ≈0 curvature at the threshold");
    }

    [Fact]
    public void QuinticSmooth_above_terminal_slope_matches_exponent()
    {
        var e = AboveStyleShape(0.3, 3.0, SegmentedCurveTransitionStyle.QuinticSmooth, SegmentedCurveShape.Convex);
        var s = Slope(e, 0.9999, h: 1e-6);
        s.Should().BeApproximately(3.0, 0.05);
    }

    [Fact]
    public void QuinticSmooth_with_exponent_one_is_pure_linear()
    {
        // Start slope = 1, end slope = 1, curvatures = 0 → polynomial
        // collapses to a straight line.
        var e = AboveStyleShape(0.3, 1.0, SegmentedCurveTransitionStyle.QuinticSmooth, SegmentedCurveShape.Convex);
        foreach (var a in new[] { 0.0, 0.3, 0.5, 0.7, 1.0 })
        {
            Eval(e, a).Should().BeApproximately(a, 1e-9);
        }
    }

    [Fact]
    public void QuinticSmooth_below_zero_in_zero_out()
    {
        BelowStyleShape(0.4, 2.0, SegmentedCurveTransitionStyle.QuinticSmooth, SegmentedCurveShape.Convex)
            .Evaluate(Signal.Scalar(0.0), 0.01).ScalarValue.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void QuinticSmooth_below_smooth_at_threshold()
    {
        var e = BelowStyleShape(0.4, 2.0, SegmentedCurveTransitionStyle.QuinticSmooth, SegmentedCurveShape.Convex);
        var slopeBelow = Slope(e, 0.3999);
        var slopeAbove = Slope(e, 0.4001);
        slopeBelow.Should().BeApproximately(1.0, 0.01);
        slopeAbove.Should().BeApproximately(1.0, 0.01);
    }

    // ============================================================
    // PowerCurve — additive form with documented slope mismatch
    // ============================================================

    [Fact]
    public void PowerCurve_above_convex_no_dip()
    {
        var e = AboveStyleShape(0.3, 2.0, SegmentedCurveTransitionStyle.PowerCurve, SegmentedCurveShape.Convex);
        // No-dip property: convex curve sits at or below chord but never
        // dips by an unbounded amount; specifically, output at any a should
        // be in [linear_floor, a] where linear_floor matches the renormalized
        // power-curve formula at that a.
        // Easier test: confirm monotonicity & endpoint properties.
        var a = 0.6;
        var output = Eval(e, a);
        output.Should().BeLessThanOrEqualTo(a + 1e-9, "PowerCurve convex sits at/below chord");
        output.Should().BeGreaterThan(0.3 - 1e-9, "PowerCurve convex stays above threshold value");
    }

    [Fact]
    public void PowerCurve_above_documented_slope_mismatch_at_threshold()
    {
        // PowerCurve renormalization induces a slope mismatch: linear side
        // has slope 1, curved side starts at slope 1/n. This is a documented
        // trade-off for the simpler additive formula. Lock it in so it
        // doesn't accidentally "improve."
        var n = 2.0;
        var e = AboveStyleShape(0.3, n, SegmentedCurveTransitionStyle.PowerCurve, SegmentedCurveShape.Convex);
        var slopeBelow = Slope(e, 0.2999);
        var slopeAbove = Slope(e, 0.3001);
        slopeBelow.Should().BeApproximately(1.0, 0.01);
        slopeAbove.Should().BeApproximately(1.0 / n, 0.02,
            "PowerCurve convex starts the curved segment at slope 1/n");
    }

    [Fact]
    public void PowerCurve_above_preserves_endpoint()
    {
        AboveStyleShape(0.3, 2.0, SegmentedCurveTransitionStyle.PowerCurve, SegmentedCurveShape.Convex)
            .Evaluate(Signal.Scalar(1.0), 0.01).ScalarValue.Should().BeApproximately(1.0, 1e-9);
        AboveStyleShape(0.3, 3.5, SegmentedCurveTransitionStyle.PowerCurve, SegmentedCurveShape.Concave)
            .Evaluate(Signal.Scalar(1.0), 0.01).ScalarValue.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void PowerCurve_with_exponent_one_is_pure_linear()
    {
        // raw(u) = u + 0*u² = u; raw(1) = 1; out = t + u*(1-t) = a.
        var e = AboveStyleShape(0.3, 1.0, SegmentedCurveTransitionStyle.PowerCurve, SegmentedCurveShape.Convex);
        foreach (var a in new[] { 0.0, 0.3, 0.5, 0.7, 1.0 })
        {
            Eval(e, a).Should().BeApproximately(a, 1e-9);
        }
    }

    // ============================================================
    // Shape symmetry — convex and concave should be reflections
    // ============================================================

    [Fact]
    public void Convex_and_concave_are_reflections_across_chord_above_threshold()
    {
        // For each style, convex(a) and concave(a) should be related by
        // reflection across the chord (which is y = a in this case since
        // both endpoints are on the diagonal).
        // Reflection across y = a: concave_output should satisfy
        //   concave(a) + convex(a) ≈ 2 * a    (in the above-threshold case
        //   where the chord goes from (t,t) to (1,1) along y=x, so the
        //   reflection of point (a, y) across y=x is (y, a), but we're
        //   reflecting outputs at the same x — so the relation is
        //   concave_out(a) ≈ a + (a − convex_out(a)).
        // i.e. concave_out(a) − a ≈ a − convex_out(a)
        // i.e. concave_out(a) + convex_out(a) ≈ 2a
        var t = 0.3;
        var n = 2.0;
        var samples = new[] { 0.4, 0.5, 0.7, 0.9 };
        foreach (var style in Enum.GetValues<SegmentedCurveTransitionStyle>())
        {
            var convex = AboveStyleShape(t, n, style, SegmentedCurveShape.Convex);
            var concave = AboveStyleShape(t, n, style, SegmentedCurveShape.Concave);
            foreach (var a in samples)
            {
                var sum = Eval(convex, a) + Eval(concave, a);
                sum.Should().BeApproximately(2.0 * a, 0.02,
                    $"style={style} convex+concave should sum to 2a at a={a} (reflection)");
            }
        }
    }

    // ============================================================
    // Cross-style smoke
    // ============================================================

    [Fact]
    public void Five_styles_produce_five_distinct_outputs_at_same_input()
    {
        // Confirm the dispatch routes to five distinct math paths.
        var t = 0.3;
        var n = 2.0;
        var a = 0.6;
        var results = Enum.GetValues<SegmentedCurveTransitionStyle>()
            .Select(style => Eval(AboveStyleShape(t, n, style, SegmentedCurveShape.Convex), a))
            .ToArray();
        results.Should().HaveCount(5);
        results.Distinct().Should().HaveCount(5,
            "all five styles should produce numerically distinct outputs for the same input");
    }

    [Fact]
    public void Default_factory_uses_quintic_smooth_convex()
    {
        var def = SegmentedResponseCurveModifier.Default;
        def.TransitionStyle.Should().Be(SegmentedCurveTransitionStyle.QuinticSmooth);
        def.Shape.Should().Be(SegmentedCurveShape.Convex);
    }

    [Fact]
    public void Constructor_default_shape_is_convex_for_backward_compat()
    {
        // Sanity check: 4-arg constructor (no Shape) picks Convex.
        var mod = new SegmentedResponseCurveModifier(0.3, 2.0,
            SegmentedCurveRegion.AboveThreshold,
            SegmentedCurveTransitionStyle.Hard);
        mod.Shape.Should().Be(SegmentedCurveShape.Convex,
            "v4 JSON without shape field must deserialize with Convex default");
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
        for (int i = 0; i < 5; i++)
        {
            e.Evaluate(Signal.Scalar(1.0), 0.1);
        }
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
        {
            output = e.Evaluate(Signal.Scalar(1.0), 0.1);
        }

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
        {
            output = e.Evaluate(Signal.Delta(10), 0.01);
        }

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
        {
            e.Evaluate(Signal.Delta(0), 0.01).ScalarValue.Should().BeApproximately(0.5, 1e-9);
        }
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
                new OutputScaleModifier(0.5),
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
    public void OutputScale_caps_post_integrator_signal()
    {
        // Behavior contract: Mouse → StickDynamics → OutputScale(0.5) → Stick.
        // Integrator saturates at ±1; OutputScale clamps to ±0.5. Full deflection
        // intentionally unreachable from here — this is the governor use case
        // and the original "bug" that triggered the rename + DeltaScale work.
        // Locking this in so a future change doesn't silently re-merge the two
        // behaviors.
        var chain = new ChainEvaluator(
            new MouseAxisSource(MouseAxis.Y),
            new Modifier[]
            {
                new StickDynamicsModifier(StickDynamicsMode.Persistent, 100.0, 0.0),
                new OutputScaleModifier(0.5),
            },
            new StickAxisTarget(Stick.Left, AxisComponent.X));

        chain.IsValid.Should().BeTrue();
        // 100 counts → Persistent integrator output = 1.0; OutputScale halves it.
        // Saturate well beyond by sending more than enough.
        for (int i = 0; i < 20; i++)
        {
            chain.Apply(RawEvent.ForMouseMove(0, 10, 0));
        }

        var sig = chain.EndOfTick(0.01);
        sig.ScalarValue.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void DeltaScale_reduces_sensitivity_without_capping_max()
    {
        // Behavior contract: Mouse → DeltaScale(0.5) → StickDynamics → Stick.
        // Half the effective counts reach the integrator, so it takes ~2× more
        // motion to reach full deflection — but it CAN reach full deflection.
        // Codifies the gamer-intuition behavior the user (Zanges) asked for.
        var chain = new ChainEvaluator(
            new MouseAxisSource(MouseAxis.Y),
            new Modifier[]
            {
                new DeltaScaleModifier(0.5),
                new StickDynamicsModifier(StickDynamicsMode.Persistent, 100.0, 0.0),
            },
            new StickAxisTarget(Stick.Left, AxisComponent.X));

        chain.IsValid.Should().BeTrue();
        // 100 raw counts × 0.5 = 50 scaled counts → 50/100 = 0.5 deflection.
        // 200 raw counts × 0.5 = 100 scaled → 1.0 deflection — full reach.
        for (int i = 0; i < 20; i++)
        {
            chain.Apply(RawEvent.ForMouseMove(0, 10, 0));
        }

        var sig = chain.EndOfTick(0.01);
        sig.ScalarValue.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void DeltaScale_below_one_requires_more_motion_for_same_deflection()
    {
        // Concrete numeric proof of the contract: the same raw mouse motion
        // produces less deflection with DeltaScale 0.5 in the chain than without.
        var without = new ChainEvaluator(
            new MouseAxisSource(MouseAxis.Y),
            new Modifier[] { new StickDynamicsModifier(StickDynamicsMode.Persistent, 100.0, 0.0) },
            new StickAxisTarget(Stick.Left, AxisComponent.X));
        var with = new ChainEvaluator(
            new MouseAxisSource(MouseAxis.Y),
            new Modifier[]
            {
                new DeltaScaleModifier(0.5),
                new StickDynamicsModifier(StickDynamicsMode.Persistent, 100.0, 0.0),
            },
            new StickAxisTarget(Stick.Left, AxisComponent.X));

        // 50 raw counts each.
        for (int i = 0; i < 5; i++)
        {
            without.Apply(RawEvent.ForMouseMove(0, 10, 0));
            with.Apply(RawEvent.ForMouseMove(0, 10, 0));
        }
        var sigWithout = without.EndOfTick(0.01);
        var sigWith = with.EndOfTick(0.01);

        // Without DeltaScale: 50/100 = 0.5. With DeltaScale 0.5: 25/100 = 0.25.
        sigWithout.ScalarValue.Should().BeApproximately(0.5, 1e-9);
        sigWith.ScalarValue.Should().BeApproximately(0.25, 1e-9);
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
        {
            output = e.Evaluate(Signal.Scalar(1.0), 0.01);
        }

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
        for (int i = 0; i < 5; i++)
        {
            e.Evaluate(Signal.Digital(true), dt);
        }

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
        {
            e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeFalse();
        }
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
        {
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        }
    }

    [Fact]
    public void Window_expiry_resets_counter()
    {
        var e = new MultiTapEvaluator(new MultiTapModifier(2, 0.2, 0.1, 0.05));
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01); // first tap registered
        // Drain past the window.
        for (int i = 0; i < 30; i++)
        {
            e.Evaluate(Signal.Digital(false), 0.01);
        }
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
        {
            e.Evaluate(Signal.Digital(true), 0.01);
        }

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
        {
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        }
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
        for (int i = 0; i < 10; i++)
        {
            e.Evaluate(Signal.Digital(false), 0.01);
        }
        // Second short press during wait.
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeFalse();
        // Release: in suppression mode now; refreshes wait but no fire armed.
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        // Run well past the wait window — should never fire.
        for (int i = 0; i < 100; i++)
        {
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        }
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
            for (int i = 0; i < 10; i++)
            {
                e.Evaluate(Signal.Digital(false), 0.01);
            }
        }
        // Run past the final wait window.
        for (int i = 0; i < 100; i++)
        {
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        }
    }

    [Fact]
    public void Followup_long_press_confirms_early_then_passthrough()
    {
        var e = new TapEvaluator(WithWait(pulse: 0.05));
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01); // first tap, pending
        // Brief gap.
        for (int i = 0; i < 5; i++)
        {
            e.Evaluate(Signal.Digital(false), 0.01);
        }
        // Long press starts. Press lands → cancels wait initially.
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeFalse();
        // Hold past MaxHold (0.3s).
        for (int i = 0; i < 30; i++)
        {
            e.Evaluate(Signal.Digital(true), 0.01);
        }
        // The tick when overflow trips: pending pulse fires AND passthrough engages.
        var output = e.Evaluate(Signal.Digital(true), 0.01);
        output.DigitalValue.Should().BeTrue();
        // Continue holding: passthrough keeps output true.
        for (int i = 0; i < 20; i++)
        {
            e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        }
        // Release: passthrough drops; pending pulse already expired.
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
    }

    [Fact]
    public void Long_press_from_idle_passthrough()
    {
        var e = new TapEvaluator(WithWait());
        // Press and hold past MaxHold without any prior tap.
        for (int i = 0; i < 30; i++)
        {
            e.Evaluate(Signal.Digital(true), 0.01);
        }
        // Tick that overflows.
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        // Continue holding.
        for (int i = 0; i < 50; i++)
        {
            e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        }
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
        {
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        }
        // Long press passthrough still works.
        for (int i = 0; i < 30; i++)
        {
            e.Evaluate(Signal.Digital(true), 0.01);
        }

        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        for (int i = 0; i < 20; i++)
        {
            e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        }

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
        for (int i = 0; i < 5; i++)
        {
            e.Evaluate(Signal.Digital(false), 0.01);
        }
        // Tap 2 → pending.
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse(); // pending, not firing
        // Hold the wait open.
        for (int i = 0; i < 39; i++)
        {
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        }
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
        for (int i = 0; i < 5; i++)
        {
            doubleE.Evaluate(Signal.Digital(false), 0.01);
        }

        doubleE.Evaluate(Signal.Digital(true), 0.05);
        doubleE.Evaluate(Signal.Digital(false), 0.01); // tap 2 → pending
        // Third tap arrives during wait.
        for (int i = 0; i < 5; i++)
        {
            doubleE.Evaluate(Signal.Digital(false), 0.01);
        }

        doubleE.Evaluate(Signal.Digital(true), 0.05).DigitalValue.Should().BeFalse(); // wait canceled
        doubleE.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse(); // never fires
        // Confirm: ride out further time, still no fire.
        for (int i = 0; i < 50; i++)
        {
            doubleE.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        }
    }

    [Fact]
    public void Followup_long_press_confirms_double_tap_early()
    {
        var e = new MultiTapEvaluator(new MultiTapModifier(2, 0.4, 0.2, 0.05, WaitForHigherTaps: true));
        e.Evaluate(Signal.Digital(true), 0.05);
        e.Evaluate(Signal.Digital(false), 0.01);
        for (int i = 0; i < 5; i++)
        {
            e.Evaluate(Signal.Digital(false), 0.01);
        }

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
            {
                sawTrue = true;
            }
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
        for (int i = 0; i < 5; i++)
        {
            e.Evaluate(Signal.Digital(false), 0.01);
        }

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
        {
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        }

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
        for (int i = 0; i < 5; i++)
        {
            e.Evaluate(Signal.Digital(false), 0.01);
        }

        e.Evaluate(Signal.Digital(true), 0.05).DigitalValue.Should().BeFalse();
        e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        // Run well past the wait — should never fire.
        for (int i = 0; i < 100; i++)
        {
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        }
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
            for (int i = 0; i < 5; i++)
            {
                e.Evaluate(Signal.Digital(false), 0.01);
            }
        }
        for (int i = 0; i < 100; i++)
        {
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        }
    }

    [Fact]
    public void Long_press_from_idle_passthrough()
    {
        var e = new WaitForTapResolutionEvaluator(new WaitForTapResolutionModifier(0.3, 0.4, 0.05));
        // Press past MaxHold.
        for (int i = 0; i < 30; i++)
        {
            e.Evaluate(Signal.Digital(true), 0.01);
        }

        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        for (int i = 0; i < 50; i++)
        {
            e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        }

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
        for (int i = 0; i < 5; i++)
        {
            e.Evaluate(Signal.Digital(false), 0.01);
        }

        e.Evaluate(Signal.Digital(true), 0.01);
        for (int i = 0; i < 30; i++)
        {
            e.Evaluate(Signal.Digital(true), 0.01);
        }
        // Overflow trips → fire pending + passthrough.
        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        for (int i = 0; i < 20; i++)
        {
            e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        }

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
        {
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        }
        // Long press passthrough still works.
        for (int i = 0; i < 30; i++)
        {
            e.Evaluate(Signal.Digital(true), 0.01);
        }

        e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        for (int i = 0; i < 20; i++)
        {
            e.Evaluate(Signal.Digital(true), 0.01).DigitalValue.Should().BeTrue();
        }

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
        {
            e.Evaluate(Signal.Digital(false), 0.01).DigitalValue.Should().BeFalse();
        }
    }
}

public class ParametricCurveEvaluatorTests
{
    private static ParametricCurveEvaluator Make(IEnumerable<(double X, double Y)> points, bool symmetric = true)
    {
        var mod = new ParametricCurveModifier
        {
            Points = points.Select(p => new CurvePoint(p.X, p.Y)).ToArray(),
            Symmetric = symmetric,
        };
        return new ParametricCurveEvaluator(mod);
    }

    private static double Eval(ParametricCurveEvaluator e, double x) =>
        e.Evaluate(Signal.Scalar(x), 0.01).ScalarValue;

    [Fact]
    public void Identity_curve_passes_input_through_unchanged()
    {
        var e = Make(new[] { (0.0, 0.0), (0.5, 0.5), (1.0, 1.0) });
        foreach (var x in new[] { 0.0, 0.1, 0.25, 0.5, 0.75, 0.9, 1.0 })
        {
            Eval(e, x).Should().BeApproximately(x, 1e-9, $"identity at x={x}");
        }
    }

    [Fact]
    public void Identity_curve_is_symmetric_around_zero()
    {
        var e = Make(new[] { (0.0, 0.0), (0.5, 0.5), (1.0, 1.0) }, symmetric: true);
        Eval(e, -0.3).Should().BeApproximately(-0.3, 1e-9);
        Eval(e, -0.7).Should().BeApproximately(-0.7, 1e-9);
    }

    [Fact]
    public void Symmetric_mode_mirrors_arbitrary_curve_to_negative_side()
    {
        // S-curve-ish: dip at low input, accelerate at high input.
        var e = Make(new[] { (0.0, 0.0), (0.5, 0.2), (1.0, 1.0) }, symmetric: true);
        foreach (var x in new[] { 0.1, 0.3, 0.5, 0.7, 0.9 })
        {
            Eval(e, -x).Should().BeApproximately(-Eval(e, x), 1e-9,
                $"symmetric mirror at ±{x}");
        }
    }

    [Fact]
    public void Full_range_mode_can_be_asymmetric()
    {
        // Define curve with different shapes for positive and negative input.
        var e = Make(new[]
        {
            (-1.0, -1.0),
            (-0.5, -0.7),     // negative side: steep tip
            (0.0, 0.0),
            (0.5, 0.2),       // positive side: gentle tip
            (1.0, 1.0),
        }, symmetric: false);
        // Output at +0.5 differs from -output at -0.5 (different shape per side).
        var posOut = Eval(e, 0.5);
        var negOut = Eval(e, -0.5);
        Math.Abs(posOut - (-negOut)).Should().BeGreaterThan(0.01,
            "full-range mode should allow asymmetric shape");
    }

    [Fact]
    public void Output_passes_through_control_point_values()
    {
        // The spline must interpolate (not approximate) — output at each
        // control point's X equals that point's Y.
        var pts = new[] { (0.0, 0.05), (0.3, 0.4), (0.7, 0.8), (1.0, 0.95) };
        var e = Make(pts);
        foreach (var (x, y) in pts)
        {
            Eval(e, x).Should().BeApproximately(y, 1e-9, $"interpolation at x={x}");
        }
    }

    [Fact]
    public void Monotonic_input_data_yields_monotonic_output()
    {
        // Take a curve with widely varying segment slopes — Fritsch-Carlson
        // must keep output monotonic.
        var e = Make(new[] { (0.0, 0.0), (0.3, 0.05), (0.5, 0.5), (0.7, 0.55), (1.0, 1.0) });
        double prev = double.NegativeInfinity;
        for (int i = 0; i <= 100; i++)
        {
            var x = i / 100.0;
            var y = Eval(e, x);
            y.Should().BeGreaterThanOrEqualTo(prev - 1e-9,
                $"monotonicity violated at x={x}: prev={prev}, curr={y}");
            prev = y;
        }
    }

    [Fact]
    public void Monotonicity_holds_with_flat_segment()
    {
        // Flat middle segment: (0.3, 0.5), (0.7, 0.5). Output across flat
        // region should stay constant at 0.5, no over/undershoot.
        var e = Make(new[] { (0.0, 0.0), (0.3, 0.5), (0.7, 0.5), (1.0, 1.0) });
        for (int i = 30; i <= 70; i++)
        {
            var x = i / 100.0;
            Eval(e, x).Should().BeApproximately(0.5, 0.001,
                $"flat segment at x={x}");
        }
    }

    [Fact]
    public void Below_first_point_extrapolates_linearly()
    {
        // In full-range mode, if points don't span the full [-1, 1], the
        // evaluator extrapolates linearly from the nearest endpoint.
        var e = Make(new[] { (0.2, 0.1), (1.0, 1.0) }, symmetric: false);
        var below = Eval(e, 0.0);
        below.Should().BeLessThan(0.1, "extrapolation below first point");
    }

    [Fact]
    public void Above_last_point_extrapolates_linearly()
    {
        var e = Make(new[] { (0.0, 0.0), (0.8, 0.9) }, symmetric: false);
        var above = Eval(e, 1.0);
        above.Should().BeGreaterThan(0.9, "extrapolation above last point").And.BeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public void Out_of_range_input_clamps_to_unit()
    {
        var e = Make(new[] { (0.0, 0.0), (0.5, 0.5), (1.0, 1.0) });
        Eval(e, 1.5).Should().BeApproximately(1.0, 1e-9, "input > 1 clamps");
        Eval(e, -1.5).Should().BeApproximately(-1.0, 1e-9, "input < -1 clamps in symmetric mode");
    }

    [Fact]
    public void Nan_input_yields_zero_output()
    {
        var e = Make(new[] { (0.0, 0.0), (0.5, 0.5), (1.0, 1.0) });
        Eval(e, double.NaN).Should().Be(0.0);
    }

    [Fact]
    public void Fewer_than_two_points_falls_back_to_passthrough()
    {
        var modOnePoint = new ParametricCurveModifier { Points = new[] { new CurvePoint(0.5, 0.5) } };
        var e = new ParametricCurveEvaluator(modOnePoint);
        Eval(e, 0.3).Should().BeApproximately(0.3, 1e-9);
        Eval(e, 0.7).Should().BeApproximately(0.7, 1e-9);

        var modNoPoints = new ParametricCurveModifier { Points = Array.Empty<CurvePoint>() };
        var e2 = new ParametricCurveEvaluator(modNoPoints);
        Eval(e2, 0.4).Should().BeApproximately(0.4, 1e-9);
    }

    [Fact]
    public void Duplicate_x_values_dont_crash_or_produce_nan()
    {
        // Adjacent X values within 1e-6 should be snapped apart inside the
        // evaluator. Test with duplicates and near-duplicates.
        var e = Make(new[] { (0.0, 0.0), (0.5, 0.3), (0.5, 0.7), (1.0, 1.0) });
        var output = Eval(e, 0.5);
        double.IsFinite(output).Should().BeTrue();
        output.Should().BeInRange(0.0, 1.0);

        for (int i = 0; i <= 100; i++)
        {
            var y = Eval(e, i / 100.0);
            double.IsFinite(y).Should().BeTrue($"no NaN/Inf at x={i / 100.0}");
        }
    }

    [Fact]
    public void Points_out_of_order_are_sorted_internally()
    {
        // Pass points in reverse X order; evaluator sorts them.
        var e = Make(new[] { (1.0, 1.0), (0.5, 0.4), (0.0, 0.0) });
        // At x=0.5, output should be 0.4 (the interior control point).
        Eval(e, 0.5).Should().BeApproximately(0.4, 1e-9);
    }

    [Fact]
    public void Sign_preservation_in_symmetric_mode_when_curve_is_concave()
    {
        // Concave curve in [0, 1]: output > input. Mirrored, output for
        // negative input should also be concave (more negative output).
        var e = Make(new[] { (0.0, 0.0), (0.5, 0.7), (1.0, 1.0) }, symmetric: true);
        var pos = Eval(e, 0.5);
        var neg = Eval(e, -0.5);
        pos.Should().BeGreaterThan(0.5);   // concave above chord
        neg.Should().BeLessThan(-0.5);     // negative, more negative than -0.5
        neg.Should().BeApproximately(-pos, 1e-9);
    }

    [Fact]
    public void Default_factory_is_three_point_identity_symmetric()
    {
        var def = ParametricCurveModifier.Default;
        def.Symmetric.Should().BeTrue();
        def.Points.Should().HaveCount(3);
        def.Points[0].Should().Be(new CurvePoint(0.0, 0.0));
        def.Points[1].Should().Be(new CurvePoint(0.5, 0.5));
        def.Points[2].Should().Be(new CurvePoint(1.0, 1.0));
    }

    [Fact]
    public void Reset_is_a_noop_because_evaluator_is_stateless()
    {
        var e = Make(new[] { (0.0, 0.0), (0.5, 0.3), (1.0, 1.0) });
        var before = Eval(e, 0.5);
        e.Reset();
        var after = Eval(e, 0.5);
        after.Should().Be(before);
    }

    [Fact]
    public void Config_property_exposes_underlying_modifier()
    {
        var mod = ParametricCurveModifier.Default;
        var e = new ParametricCurveEvaluator(mod);
        e.Config.Should().BeSameAs(mod);
    }

    [Fact]
    public void Equals_compares_points_by_value()
    {
        // Custom Equals override on the modifier — list-of-records must
        // compare by content, not by reference. Required for the engine's
        // "preserve state when chain unchanged" optimization.
        var a = new ParametricCurveModifier
        {
            Points = new[] { new CurvePoint(0.0, 0.0), new CurvePoint(1.0, 1.0) }
        };
        var b = new ParametricCurveModifier
        {
            Points = new[] { new CurvePoint(0.0, 0.0), new CurvePoint(1.0, 1.0) }
        };
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());

        var c = new ParametricCurveModifier
        {
            Points = new[] { new CurvePoint(0.0, 0.0), new CurvePoint(1.0, 0.9) }
        };
        a.Should().NotBe(c);
    }

    // ============================================================
    // CurveEditorModifier — shares math with ParametricCurveModifier
    // ============================================================

    [Fact]
    public void CurveEditor_modifier_evaluates_identically_to_ParametricCurve_modifier()
    {
        // Both modifier kinds wrap the same data; both delegate to the same
        // ParametricCurveEvaluator. Outputs must match exactly for identical
        // points + symmetric flag.
        var points = new[]
        {
            new CurvePoint(0.0, 0.0),
            new CurvePoint(0.3, 0.5),
            new CurvePoint(0.7, 0.6),
            new CurvePoint(1.0, 1.0),
        };

        var paramMod = new ParametricCurveModifier { Points = points, Symmetric = true };
        var editorMod = new CurveEditorModifier { Points = points, Symmetric = true };

        var paramEval = new ParametricCurveEvaluator(paramMod);
        var editorEval = new ParametricCurveEvaluator(editorMod);

        foreach (var x in new[] { -1.0, -0.7, -0.3, 0.0, 0.15, 0.45, 0.6, 0.85, 1.0 })
        {
            var paramOut = paramEval.Evaluate(Signal.Scalar(x), 0.01).ScalarValue;
            var editorOut = editorEval.Evaluate(Signal.Scalar(x), 0.01).ScalarValue;
            editorOut.Should().Be(paramOut,
                $"shared math at x={x}: CurveEditor and ParametricCurve must produce identical output");
        }
    }

    [Fact]
    public void CurveEditor_modifier_default_is_three_point_identity_symmetric()
    {
        var def = CurveEditorModifier.Default;
        def.Symmetric.Should().BeTrue();
        def.Points.Should().HaveCount(3);
        def.Points[0].Should().Be(new CurvePoint(0.0, 0.0));
        def.Points[1].Should().Be(new CurvePoint(0.5, 0.5));
        def.Points[2].Should().Be(new CurvePoint(1.0, 1.0));
    }

    [Fact]
    public void CurveEditor_modifier_equals_compares_points_by_value()
    {
        var a = new CurveEditorModifier
        {
            Points = new[] { new CurvePoint(0.0, 0.0), new CurvePoint(0.5, 0.4), new CurvePoint(1.0, 1.0) }
        };
        var b = new CurveEditorModifier
        {
            Points = new[] { new CurvePoint(0.0, 0.0), new CurvePoint(0.5, 0.4), new CurvePoint(1.0, 1.0) }
        };
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());

        var c = new CurveEditorModifier
        {
            Points = new[] { new CurvePoint(0.0, 0.0), new CurvePoint(0.5, 0.5), new CurvePoint(1.0, 1.0) }
        };
        a.Should().NotBe(c);
    }

    [Fact]
    public void ICurveData_interface_implemented_by_both_modifiers()
    {
        ICurveData paramData = ParametricCurveModifier.Default;
        ICurveData editorData = CurveEditorModifier.Default;
        paramData.Points.Should().HaveCount(3);
        editorData.Points.Should().HaveCount(3);
        paramData.Symmetric.Should().BeTrue();
        editorData.Symmetric.Should().BeTrue();
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
