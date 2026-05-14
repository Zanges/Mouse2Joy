using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

/// <summary>
/// Evaluates a user-defined response curve via monotone cubic Hermite
/// interpolation (Fritsch-Carlson algorithm). The algorithm derives tangents
/// at each control point that guarantee monotonicity — output never decreases
/// as input increases — which prevents "reverse" regions that would feel bad
/// in a stick response.
///
/// <para>Points are sorted by X and adjacent X values are clamped to differ
/// by at least <c>1e-6</c> at construction so the algorithm doesn't divide
/// by zero. Tangents are computed once per evaluator instance; per-tick
/// evaluation is just segment lookup + Hermite basis evaluation.</para>
/// </summary>
internal sealed class ParametricCurveEvaluator : IModifierEvaluator
{
    private readonly Modifier _config;
    private readonly ICurveData _data;
    private readonly CurvePoint[] _sortedPoints;
    private readonly double[] _tangents;

    /// <summary>
    /// Construct with explicit config + data. The config is what
    /// <see cref="IModifierEvaluator.Config"/> returns (used by callers for
    /// type checks); the data drives the math. In practice, a modifier
    /// implementing <see cref="ICurveData"/> passes itself as both
    /// arguments — see the convenience constructors.
    /// </summary>
    public ParametricCurveEvaluator(Modifier config, ICurveData data)
    {
        _config = config;
        _data = data;

        // Sort points by X (defensive — UI shouldn't reorder, but the user
        // may type values that put X out of order).
        var sorted = data.Points.OrderBy(p => p.X).ToArray();

        // Snap adjacent X values to differ by at least 1e-6 so the
        // Fritsch-Carlson math never divides by zero.
        for (int i = 1; i < sorted.Length; i++)
        {
            if (sorted[i].X - sorted[i - 1].X < 1e-6)
            {
                sorted[i] = sorted[i] with { X = sorted[i - 1].X + 1e-6 };
            }
        }

        _sortedPoints = sorted;
        _tangents = ComputeFritschCarlsonTangents(_sortedPoints);
    }

    /// <summary>Convenience constructor for <see cref="ParametricCurveModifier"/>.</summary>
    public ParametricCurveEvaluator(ParametricCurveModifier config) : this(config, config) { }

    /// <summary>Convenience constructor for <see cref="CurveEditorModifier"/>.</summary>
    public ParametricCurveEvaluator(CurveEditorModifier config) : this(config, config) { }

    public Modifier Config => _config;
    public void Reset() { }

    public Signal Evaluate(in Signal input, double dt)
    {
        var x = input.ScalarValue;
        if (double.IsNaN(x))
        {
            return Signal.ZeroScalar;
        }

        // Defensive fallback: with fewer than 2 points there's nothing to
        // interpolate. Pass the input through (clamped).
        if (_sortedPoints.Length < 2)
        {
            var clamped = x;
            if (clamped > 1.0)
            {
                clamped = 1.0;
            }
            else if (clamped < -1.0)
            {
                clamped = -1.0;
            }

            return Signal.Scalar(clamped);
        }

        double sample;
        double sign;
        if (_data.Symmetric)
        {
            sign = x < 0 ? -1.0 : 1.0;
            sample = Math.Abs(x);
            if (sample > 1.0)
            {
                sample = 1.0;
            }
        }
        else
        {
            sign = 1.0;
            sample = x;
            if (sample > 1.0)
            {
                sample = 1.0;
            }
            else if (sample < -1.0)
            {
                sample = -1.0;
            }
        }

        var output = EvaluateSpline(sample);

        // Clamp final output to legal scalar range.
        if (output > 1.0)
        {
            output = 1.0;
        }
        else if (output < -1.0)
        {
            output = -1.0;
        }

        return Signal.Scalar(sign * output);
    }

    private double EvaluateSpline(double x)
    {
        // Below first point: linearly extrapolate at the start tangent.
        if (x <= _sortedPoints[0].X)
        {
            return _sortedPoints[0].Y + _tangents[0] * (x - _sortedPoints[0].X);
        }
        // Above last point: linearly extrapolate at the end tangent.
        var last = _sortedPoints.Length - 1;
        if (x >= _sortedPoints[last].X)
        {
            return _sortedPoints[last].Y + _tangents[last] * (x - _sortedPoints[last].X);
        }

        // Find the segment containing x. Linear search is fine for ≤7 points.
        int i = 0;
        while (i < last && x > _sortedPoints[i + 1].X)
        {
            i++;
        }

        var x0 = _sortedPoints[i].X;
        var x1 = _sortedPoints[i + 1].X;
        var y0 = _sortedPoints[i].Y;
        var y1 = _sortedPoints[i + 1].Y;
        var m0 = _tangents[i];
        var m1 = _tangents[i + 1];

        var h = x1 - x0;
        var t = (x - x0) / h;
        var t2 = t * t;
        var t3 = t2 * t;

        // Cubic Hermite basis functions.
        var h00 = 2.0 * t3 - 3.0 * t2 + 1.0;
        var h10 = t3 - 2.0 * t2 + t;
        var h01 = -2.0 * t3 + 3.0 * t2;
        var h11 = t3 - t2;

        return y0 * h00 + h * m0 * h10 + y1 * h01 + h * m1 * h11;
    }

    /// <summary>
    /// Computes monotonicity-preserving tangents at each control point per
    /// Fritsch &amp; Carlson 1980. Standard algorithm:
    ///
    /// <list type="number">
    ///   <item>Initial tangents are secant slopes (averaged at interior
    ///   points, one-sided at endpoints).</item>
    ///   <item>For each segment with non-zero secant slope, scale the
    ///   adjacent tangents down if α² + β² > 9 (where α, β are the
    ///   tangents normalized to the secant slope). This guarantees
    ///   monotonicity.</item>
    ///   <item>For each segment with zero secant slope (constant
    ///   region), force both adjacent tangents to zero so the spline
    ///   doesn't overshoot.</item>
    /// </list>
    /// </summary>
    private static double[] ComputeFritschCarlsonTangents(CurvePoint[] pts)
    {
        int n = pts.Length;
        var tangents = new double[n];
        if (n < 2)
        {
            return tangents;
        }

        // Step 1: secant slopes between consecutive points.
        var delta = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            delta[i] = (pts[i + 1].Y - pts[i].Y) / (pts[i + 1].X - pts[i].X);
        }

        // Step 2: initial tangents — averages of adjacent secants.
        tangents[0] = delta[0];
        for (int i = 1; i < n - 1; i++)
        {
            tangents[i] = (delta[i - 1] + delta[i]) / 2.0;
        }

        tangents[n - 1] = delta[n - 2];

        // Step 3: monotonicity correction. For each segment, scale the
        // adjacent tangents if necessary.
        for (int i = 0; i < n - 1; i++)
        {
            if (delta[i] == 0.0)
            {
                // Flat segment — force both tangents to 0.
                tangents[i] = 0.0;
                tangents[i + 1] = 0.0;
                continue;
            }

            var alpha = tangents[i] / delta[i];
            var beta = tangents[i + 1] / delta[i];

            // If either tangent has a sign opposite to delta, the spline
            // would overshoot; force the offending tangent to 0.
            if (alpha < 0.0)
            {
                tangents[i] = 0.0;
                alpha = 0.0;
            }
            if (beta < 0.0)
            {
                tangents[i + 1] = 0.0;
                beta = 0.0;
            }

            var s = alpha * alpha + beta * beta;
            if (s > 9.0)
            {
                var tau = 3.0 / Math.Sqrt(s);
                tangents[i] = tau * alpha * delta[i];
                tangents[i + 1] = tau * beta * delta[i];
            }
        }

        return tangents;
    }
}
