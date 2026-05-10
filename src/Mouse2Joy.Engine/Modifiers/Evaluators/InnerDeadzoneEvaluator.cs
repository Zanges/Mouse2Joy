using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

internal sealed class InnerDeadzoneEvaluator : IModifierEvaluator
{
    private readonly InnerDeadzoneModifier _config;

    public InnerDeadzoneEvaluator(InnerDeadzoneModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;
    public void Reset() { }

    public Signal Evaluate(in Signal input, double dt)
    {
        var x = input.ScalarValue;
        if (double.IsNaN(x)) return Signal.ZeroScalar;
        var d = Math.Clamp(_config.Threshold, 0.0, 0.95);
        var sign = Math.Sign(x);
        var a = Math.Abs(x);
        if (a <= d) return Signal.ZeroScalar;
        var shaped = (a - d) / (1.0 - d);
        if (shaped > 1.0) shaped = 1.0;
        return Signal.Scalar(sign * shaped);
    }
}
