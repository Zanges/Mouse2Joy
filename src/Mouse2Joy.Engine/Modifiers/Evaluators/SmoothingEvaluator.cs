using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

/// <summary>
/// First-order exponential moving average. The discrete EMA constant is
/// <c>alpha = 1 - exp(-dt / tau)</c>, which makes the filter cleanly
/// dt-independent — sample at any tick rate, get the same time-domain
/// response. tau &lt;= 0 is treated as passthrough.
/// </summary>
internal sealed class SmoothingEvaluator : IModifierEvaluator
{
    private readonly SmoothingModifier _config;
    private double _smoothed;
    private bool _hasSeed;

    public SmoothingEvaluator(SmoothingModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;

    public void Reset()
    {
        _smoothed = 0;
        _hasSeed = false;
    }

    public Signal Evaluate(in Signal input, double dt)
    {
        var x = input.ScalarValue;
        if (double.IsNaN(x))
        {
            x = 0;
        }

        var tau = _config.TimeConstantSeconds;
        if (tau <= 0 || dt <= 0)
        {
            _smoothed = x;
            _hasSeed = true;
            return Signal.Scalar(x);
        }

        if (!_hasSeed)
        {
            _smoothed = x;
            _hasSeed = true;
        }
        else
        {
            var alpha = 1.0 - Math.Exp(-dt / tau);
            _smoothed += alpha * (x - _smoothed);
        }
        return Signal.Scalar(_smoothed);
    }
}
