using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

internal sealed class SegmentedResponseCurveEvaluator : IModifierEvaluator
{
    private readonly SegmentedResponseCurveModifier _config;

    public SegmentedResponseCurveEvaluator(SegmentedResponseCurveModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;
    public void Reset() { }

    public Signal Evaluate(in Signal input, double dt)
    {
        var x = input.ScalarValue;
        if (double.IsNaN(x)) return Signal.ZeroScalar;
        var sign = Math.Sign(x);
        var a = Math.Abs(x);
        if (a > 1.0) a = 1.0;

        var n = _config.Exponent <= 0 ? 1.0 : _config.Exponent;
        // Clamp threshold strictly inside (0, 1) so segment remap is safe at the boundary.
        var t = _config.Threshold;
        if (t < 1e-6) t = 1e-6;
        if (t > 1.0 - 1e-6) t = 1.0 - 1e-6;

        var above = _config.Region == SegmentedCurveRegion.AboveThreshold;
        var shape = _config.Shape;
        double shaped = _config.TransitionStyle switch
        {
            SegmentedCurveTransitionStyle.Hard => above ? HardAbove(a, t, n, shape) : HardBelow(a, t, n, shape),
            SegmentedCurveTransitionStyle.SmoothStep => above ? SmoothStepAbove(a, t, n, shape) : SmoothStepBelow(a, t, n, shape),
            SegmentedCurveTransitionStyle.HermiteSpline => above ? HermiteAbove(a, t, n, shape) : HermiteBelow(a, t, n, shape),
            SegmentedCurveTransitionStyle.QuinticSmooth => above ? QuinticAbove(a, t, n, shape) : QuinticBelow(a, t, n, shape),
            SegmentedCurveTransitionStyle.PowerCurve => above ? PowerAbove(a, t, n, shape) : PowerBelow(a, t, n, shape),
            _ => a
        };

        return Signal.Scalar(sign * shaped);
    }

    // ====================================================================
    // Helper math: power-curve fundamentals
    // ====================================================================

    /// <summary>
    /// Convex unit curve: maps u ∈ [0, 1] → v ∈ [0, 1] with v(0)=0, v(1)=1,
    /// sitting below the chord v=u for u ∈ (0, 1). Standard power formula.
    /// </summary>
    private static double UnitConvex(double u, double n) => Math.Pow(u, n);

    /// <summary>
    /// Concave unit curve: reflection of the convex curve across the chord.
    /// v(0)=0, v(1)=1, sits above the chord for u ∈ (0, 1).
    /// </summary>
    private static double UnitConcave(double u, double n) => 1.0 - Math.Pow(1.0 - u, n);

    private static double UnitCurve(double u, double n, SegmentedCurveShape shape)
        => shape == SegmentedCurveShape.Convex ? UnitConvex(u, n) : UnitConcave(u, n);

    // ====================================================================
    // Hard: original kinked math, generalized to support both shapes.
    // ====================================================================

    private static double HardAbove(double a, double t, double n, SegmentedCurveShape shape)
    {
        if (a <= t) return a;
        var u = (a - t) / (1.0 - t);
        var v = UnitCurve(u, n, shape);
        return t + v * (1.0 - t);
    }

    private static double HardBelow(double a, double t, double n, SegmentedCurveShape shape)
    {
        if (a >= t) return a;
        var u = a / t;
        var v = UnitCurve(u, n, shape);
        return v * t;
    }

    // ====================================================================
    // SmoothStep: blend factor w = 3u² − 2u³ between linear and curved
    // formulas. Slope-continuous at both ends of the curved segment.
    // ====================================================================

    private static double SmoothStepAbove(double a, double t, double n, SegmentedCurveShape shape)
    {
        if (a <= t) return a;
        var u = (a - t) / (1.0 - t);
        var w = 3.0 * u * u - 2.0 * u * u * u;
        var linearPart = a;
        var v = UnitCurve(u, n, shape);
        var curvePart = t + v * (1.0 - t);
        return (1.0 - w) * linearPart + w * curvePart;
    }

    private static double SmoothStepBelow(double a, double t, double n, SegmentedCurveShape shape)
    {
        if (a >= t) return a;
        var u = a / t;
        var w = 3.0 * u * u - 2.0 * u * u * u;          // w(0)=0 at tip, w(1)=1 at threshold
        var linearPart = a;
        var v = UnitCurve(u, n, shape);
        var curvePart = v * t;
        return w * linearPart + (1.0 - w) * curvePart;
    }

    // ====================================================================
    // Cubic Hermite: tangent to linear segment at threshold (slope=1) and
    // reaching the endpoint with the requested terminal slope. C¹ smooth at
    // the threshold, but the cubic constraints force a dip (convex) or
    // bulge (concave) when terminal slope ≠ chord slope. Use QuinticSmooth
    // to avoid this. Preserved for backward compatibility.
    //
    // Hermite basis on u ∈ [0, 1]:
    //   h1(u) = 2u³ - 3u² + 1   (start basis)
    //   h2(u) = -2u³ + 3u²      (end basis)
    //   h3(u) = u³ - 2u² + u    (start-tangent basis)
    //   h4(u) = u³ - u²         (end-tangent basis)
    // ====================================================================

    private static (double h1, double h2, double h3, double h4) CubicHermite(double u)
    {
        var u2 = u * u;
        var u3 = u2 * u;
        return (
            2.0 * u3 - 3.0 * u2 + 1.0,
            -2.0 * u3 + 3.0 * u2,
            u3 - 2.0 * u2 + u,
            u3 - u2
        );
    }

    private static double HermiteAboveConvex(double a, double t, double s)
    {
        var L = 1.0 - t;
        var u = (a - t) / L;
        var (h1, h2, h3, h4) = CubicHermite(u);
        // Start slope = 1 (matches linear), end slope = s.
        return t * h1 + 1.0 * h2 + L * 1.0 * h3 + L * s * h4;
    }

    private static double HermiteBelowConvex(double a, double t, double s)
    {
        var L = t;
        var u = a / L;
        var (h1, h2, h3, h4) = CubicHermite(u);
        // Below-threshold convex: gentle tip (slope 1/s), C¹ match with
        // linear at the threshold (end slope 1). Curve sits below chord.
        return 0.0 * h1 + t * h2 + L * (1.0 / s) * h3 + L * 1.0 * h4;
    }

    private static double HermiteAbove(double a, double t, double s, SegmentedCurveShape shape)
    {
        if (a <= t) return a;
        var convex = HermiteAboveConvex(a, t, s);
        // Concave is the reflection across the chord y = a (both endpoints
        // lie on y = x, so reflecting a point at (a, convex) across the
        // chord gives (a, 2a − convex)). This preserves the C¹ smoothness
        // at the threshold because reflection is a rigid transform of the
        // local shape.
        return shape == SegmentedCurveShape.Convex ? convex : 2.0 * a - convex;
    }

    private static double HermiteBelow(double a, double t, double s, SegmentedCurveShape shape)
    {
        if (a >= t) return a;
        var convex = HermiteBelowConvex(a, t, s);
        // Below-threshold chord is also y = a (since endpoints are (0,0)
        // and (t,t), both on the diagonal). Concave reflects to 2a − convex.
        return shape == SegmentedCurveShape.Convex ? convex : 2.0 * a - convex;
    }

    // ====================================================================
    // Quintic Hermite: cubic-Hermite plus zero-curvature constraint at both
    // ends. C² smooth at the threshold AND at the endpoint. The zero-
    // curvature constraint at the threshold eliminates the dip/bulge that
    // plagues cubic Hermite — the curve emerges from the linear segment
    // value-, slope-, AND curvature-matched, so it CAN'T immediately curl
    // below/above the chord.
    //
    // Quintic Hermite basis on u ∈ [0, 1] (Wikipedia "Hermite interpolation"):
    //   H1(u) = 1 − 10u³ + 15u⁴ − 6u⁵   (start value)
    //   H2(u) = 10u³ − 15u⁴ + 6u⁵       (end value)
    //   H3(u) = u − 6u³ + 8u⁴ − 3u⁵     (start slope)
    //   H4(u) = −4u³ + 7u⁴ − 3u⁵        (end slope)
    //   H5(u) = ½u² − 1.5u³ + 1.5u⁴ − ½u⁵   (start curvature)
    //   H6(u) = ½u³ − u⁴ + ½u⁵          (end curvature)
    // With our curvature=0 at both ends, H5 and H6 contributions drop out.
    // ====================================================================

    private static (double H1, double H2, double H3, double H4) QuinticHermite(double u)
    {
        var u2 = u * u;
        var u3 = u2 * u;
        var u4 = u3 * u;
        var u5 = u4 * u;
        return (
            1.0 - 10.0 * u3 + 15.0 * u4 - 6.0 * u5,
            10.0 * u3 - 15.0 * u4 + 6.0 * u5,
            u - 6.0 * u3 + 8.0 * u4 - 3.0 * u5,
            -4.0 * u3 + 7.0 * u4 - 3.0 * u5
        );
    }

    private static double QuinticAboveConvex(double a, double t, double s)
    {
        var L = 1.0 - t;
        var u = (a - t) / L;
        var (H1, H2, H3, H4) = QuinticHermite(u);
        return t * H1 + 1.0 * H2 + L * 1.0 * H3 + L * s * H4;
    }

    private static double QuinticBelowConvex(double a, double t, double s)
    {
        var L = t;
        var u = a / L;
        var (H1, H2, H3, H4) = QuinticHermite(u);
        return 0.0 * H1 + t * H2 + L * (1.0 / s) * H3 + L * 1.0 * H4;
    }

    private static double QuinticAbove(double a, double t, double s, SegmentedCurveShape shape)
    {
        if (a <= t) return a;
        var convex = QuinticAboveConvex(a, t, s);
        // Reflect across chord y = a for concave. Preserves C² smoothness
        // at the threshold (rigid reflection of a C² curve is still C²).
        return shape == SegmentedCurveShape.Convex ? convex : 2.0 * a - convex;
    }

    private static double QuinticBelow(double a, double t, double s, SegmentedCurveShape shape)
    {
        if (a >= t) return a;
        var convex = QuinticBelowConvex(a, t, s);
        return shape == SegmentedCurveShape.Convex ? convex : 2.0 * a - convex;
    }

    // ====================================================================
    // PowerCurve: additive form raw(u) = u + (n-1)·u², renormalized to hit
    // the endpoint cleanly. The added quadratic term is monotonically
    // positive for n > 1 (and zero for n = 1), so the curve sits at or
    // above the chord — no dip by construction.
    //
    // Small documented quirk: renormalization induces a slope mismatch at
    // the threshold. Linear-side slope is 1; curved-side starts at 1/n.
    // For n=2, curved-side starts at slope 0.5 — visibly less smooth than
    // Hermite/Quintic. Acceptable trade-off for a simpler formula.
    //
    // For concave, reflect: raw_concave(u) = (2u − u²) + (n−1)·u² for the
    // mirror-around-chord property. Or equivalently use UnitConcave and
    // renormalize. I'll use the unit-curve approach for symmetry with the
    // other styles.
    // ====================================================================

    private static double PowerAbove(double a, double t, double n, SegmentedCurveShape shape)
    {
        if (a <= t) return a;
        var u = (a - t) / (1.0 - t);
        // raw_convex(u) = u + (n-1)·u². raw_convex(1) = n. raw_convex(0) = 0.
        // raw_concave(u) = mirror = 1 − raw_convex(1 − u) = 1 − ((1−u) + (n−1)(1−u)²)
        //                = 1 − (1−u) − (n−1)(1−u)² = u − (n−1)(1−u)² + (n−1)
        //                Hmm wait — easier to just renormalize the unit curve.
        // Approach: scale UnitConvex/Concave so its value at u=1 is 1 (which
        // it already is — UnitConvex(1, n) = 1) but its *shape* matches the
        // PowerCurve aesthetic. The PowerCurve aesthetic is the additive
        // form, not a pure power. Let me think differently.
        //
        // The PowerCurve style's character is "linear + quadratic kick."
        // The unit curve for convex is raw(u)/raw(1) where raw(u) = u + (n-1)u².
        // raw(1) = n. So unit_convex_power(u) = (u + (n-1)u²) / n.
        // For concave, mirror around y = u: 1 − unit_convex_power(1-u).
        double v;
        if (shape == SegmentedCurveShape.Convex)
        {
            v = (u + (n - 1.0) * u * u) / n;
        }
        else
        {
            var um = 1.0 - u;
            var mirror = (um + (n - 1.0) * um * um) / n;
            v = 1.0 - mirror;
        }
        return t + v * (1.0 - t);
    }

    private static double PowerBelow(double a, double t, double n, SegmentedCurveShape shape)
    {
        if (a >= t) return a;
        var u = a / t;
        double v;
        if (shape == SegmentedCurveShape.Convex)
        {
            v = (u + (n - 1.0) * u * u) / n;
        }
        else
        {
            var um = 1.0 - u;
            var mirror = (um + (n - 1.0) * um * um) / n;
            v = 1.0 - mirror;
        }
        return v * t;
    }
}
