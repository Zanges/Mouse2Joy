using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

internal sealed class DeltaScaleEvaluator : IModifierEvaluator
{
    private readonly DeltaScaleModifier _config;

    public DeltaScaleEvaluator(DeltaScaleModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;
    public void Reset() { }

    public Signal Evaluate(in Signal input, double dt)
    {
        var factor = _config.Factor;
        if (double.IsNaN(factor) || factor < 0) factor = 0;
        // Delta is integer-typed mouse counts; rounding (banker's, the .NET
        // default) keeps total motion unbiased over time. Truncation would
        // systematically lose ~0.5 counts per nonzero tick on slow motion.
        var scaled = (int)Math.Round(input.DeltaValue * factor);
        return Signal.Delta(scaled);
    }
}
