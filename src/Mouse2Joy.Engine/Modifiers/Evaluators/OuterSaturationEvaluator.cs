using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

internal sealed class OuterSaturationEvaluator : IModifierEvaluator
{
    private readonly OuterSaturationModifier _config;

    public OuterSaturationEvaluator(OuterSaturationModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;
    public void Reset() { }

    public Signal Evaluate(in Signal input, double dt)
    {
        var x = input.ScalarValue;
        if (double.IsNaN(x)) return Signal.ZeroScalar;
        var o = Math.Clamp(_config.Threshold, 0.0, 0.95);
        var sign = Math.Sign(x);
        var a = Math.Abs(x);
        var capped = Math.Min(a, 1.0 - o);
        var shaped = capped / (1.0 - o);
        if (shaped > 1.0) shaped = 1.0;
        return Signal.Scalar(sign * shaped);
    }
}
