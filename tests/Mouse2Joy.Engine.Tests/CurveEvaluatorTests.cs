using FluentAssertions;
using Mouse2Joy.Engine.Mapping;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Tests;

public class CurveEvaluatorTests
{
    [Fact]
    public void Zero_input_yields_zero()
    {
        CurveEvaluator.Evaluate(0.0, Curve.Default).Should().Be(0.0);
    }

    [Fact]
    public void Default_curve_is_identity_for_signed_inputs()
    {
        CurveEvaluator.Evaluate(0.5, Curve.Default).Should().BeApproximately(0.5, 1e-9);
        CurveEvaluator.Evaluate(-0.5, Curve.Default).Should().BeApproximately(-0.5, 1e-9);
        CurveEvaluator.Evaluate(1.0, Curve.Default).Should().Be(1.0);
        CurveEvaluator.Evaluate(-1.0, Curve.Default).Should().Be(-1.0);
    }

    [Fact]
    public void Inputs_inside_inner_deadzone_are_zero()
    {
        var c = Curve.Default with { InnerDeadzone = 0.2 };
        CurveEvaluator.Evaluate(0.1, c).Should().Be(0.0);
        CurveEvaluator.Evaluate(-0.2, c).Should().Be(0.0);
    }

    [Fact]
    public void Inputs_inside_outer_saturation_are_full()
    {
        var c = Curve.Default with { OuterSaturation = 0.2 };
        CurveEvaluator.Evaluate(0.85, c).Should().Be(1.0);
        CurveEvaluator.Evaluate(-0.9, c).Should().Be(-1.0);
    }

    [Fact]
    public void Linear_exponent_is_remapped_into_full_range()
    {
        var c = Curve.Default with { InnerDeadzone = 0.1, OuterSaturation = 0.1 };
        // At x=0.55 (midway), normalized = (0.55-0.1)/(1-0.1-0.1) = 0.5625; n=1 -> 0.5625
        CurveEvaluator.Evaluate(0.55, c).Should().BeApproximately(0.5625, 1e-9);
    }

    [Fact]
    public void Convex_exponent_attenuates_small_inputs()
    {
        var c = Curve.Default with { Exponent = 2.0 };
        var lin = CurveEvaluator.Evaluate(0.5, Curve.Default);
        var sq = CurveEvaluator.Evaluate(0.5, c);
        sq.Should().BeLessThan(lin); // convex shape => smaller value at midpoint
        sq.Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void Concave_exponent_boosts_small_inputs()
    {
        var c = Curve.Default with { Exponent = 0.5 };
        var lin = CurveEvaluator.Evaluate(0.25, Curve.Default);
        var sqrt = CurveEvaluator.Evaluate(0.25, c);
        sqrt.Should().BeGreaterThan(lin);
        sqrt.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Sign_is_preserved()
    {
        var c = Curve.Default with { Exponent = 1.5 };
        var pos = CurveEvaluator.Evaluate(0.6, c);
        var neg = CurveEvaluator.Evaluate(-0.6, c);
        neg.Should().BeApproximately(-pos, 1e-12);
    }

    [Fact]
    public void Sensitivity_scales_input_pre_curve()
    {
        var c = Curve.Default with { Sensitivity = 2.0 };
        // Input 0.4 * 2 = 0.8 -> identity = 0.8
        CurveEvaluator.Evaluate(0.4, c).Should().BeApproximately(0.8, 1e-9);
        // Overshoot is clamped, not cumulative.
        CurveEvaluator.Evaluate(0.6, c).Should().Be(1.0);
    }

    [Fact]
    public void Out_of_range_inputs_clamp()
    {
        CurveEvaluator.Evaluate(2.5, Curve.Default).Should().Be(1.0);
        CurveEvaluator.Evaluate(-2.5, Curve.Default).Should().Be(-1.0);
    }

    [Fact]
    public void NaN_input_yields_zero()
    {
        CurveEvaluator.Evaluate(double.NaN, Curve.Default).Should().Be(0.0);
    }

    [Fact]
    public void Degenerate_deadzone_plus_saturation_reduces_to_step()
    {
        var c = Curve.Default with { InnerDeadzone = 0.6, OuterSaturation = 0.6 };
        CurveEvaluator.Evaluate(0.5, c).Should().Be(0.0);
        CurveEvaluator.Evaluate(0.7, c).Should().Be(1.0);
        CurveEvaluator.Evaluate(-0.7, c).Should().Be(-1.0);
    }
}
