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

        double shaped;
        if (_config.Region == SegmentedCurveRegion.AboveThreshold)
        {
            if (a <= t)
            {
                shaped = a;
            }
            else
            {
                var u = (a - t) / (1.0 - t);
                var v = Math.Pow(u, n);
                shaped = t + v * (1.0 - t);
            }
        }
        else
        {
            if (a >= t)
            {
                shaped = a;
            }
            else
            {
                var u = a / t;
                var v = Math.Pow(u, n);
                shaped = v * t;
            }
        }

        return Signal.Scalar(sign * shaped);
    }
}
