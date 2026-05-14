using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

internal sealed class LimiterEvaluator : IModifierEvaluator
{
    private readonly LimiterModifier _config;

    public LimiterEvaluator(LimiterModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;
    public void Reset() { }

    public Signal Evaluate(in Signal input, double dt)
    {
        var x = input.ScalarValue;
        if (double.IsNaN(x))
        {
            return Signal.ZeroScalar;
        }
        // Maxima are stored as non-negative magnitudes; clamp negatives to 0
        // so a misconfigured negative value can't invert the cap.
        var pos = _config.MaxPositive < 0 ? 0 : _config.MaxPositive;
        var neg = _config.MaxNegative < 0 ? 0 : _config.MaxNegative;
        if (x > pos)
        {
            x = pos;
        }
        else if (x < -neg)
        {
            x = -neg;
        }

        return Signal.Scalar(x);
    }
}
