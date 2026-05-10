using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Mapping;

public static class CurveEvaluator
{
    /// <summary>
    /// Apply sensitivity → inner deadzone → outer saturation → power exponent.
    /// Input is in [-1, 1] (post-sensitivity scaling for direct sources).
    /// Sign is preserved.
    /// </summary>
    public static double Evaluate(double x, Curve curve)
    {
        // Sensitivity scales the input first; for indirect sources (mouse delta
        // through a stick model) sensitivity is already applied upstream, so
        // most callers pass Sensitivity=1.0 here. Keep the field for callers
        // that *do* want pre-curve scaling.
        x *= curve.Sensitivity;

        if (double.IsNaN(x))
            return 0;

        // Clamp first so the deadzone math doesn't blow up on overshoot.
        if (x > 1.0) x = 1.0;
        else if (x < -1.0) x = -1.0;

        var sign = Math.Sign(x);
        var a = Math.Abs(x);

        var d = Math.Clamp(curve.InnerDeadzone, 0.0, 0.95);
        var o = Math.Clamp(curve.OuterSaturation, 0.0, 0.95);
        if (d + o >= 1.0)
        {
            // Degenerate: collapse to a step.
            return a > d ? sign : 0.0;
        }

        if (a <= d)
            return 0.0;
        if (a >= 1.0 - o)
            return sign;

        var normalized = (a - d) / (1.0 - d - o);
        var n = curve.Exponent <= 0 ? 1.0 : curve.Exponent;
        var shaped = Math.Pow(normalized, n);
        return sign * shaped;
    }
}
