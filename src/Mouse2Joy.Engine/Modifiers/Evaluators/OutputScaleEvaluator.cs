using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

internal sealed class OutputScaleEvaluator : IModifierEvaluator
{
    private readonly OutputScaleModifier _config;

    public OutputScaleEvaluator(OutputScaleModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;
    public void Reset() { }

    public Signal Evaluate(in Signal input, double dt)
    {
        var v = input.ScalarValue * _config.Factor;
        if (double.IsNaN(v)) v = 0;
        if (v > 1.0) v = 1.0;
        else if (v < -1.0) v = -1.0;
        return Signal.Scalar(v);
    }
}
