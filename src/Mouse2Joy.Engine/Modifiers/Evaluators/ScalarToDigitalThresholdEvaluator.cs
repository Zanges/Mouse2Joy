using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

internal sealed class ScalarToDigitalThresholdEvaluator : IModifierEvaluator
{
    private readonly ScalarToDigitalThresholdModifier _config;

    public ScalarToDigitalThresholdEvaluator(ScalarToDigitalThresholdModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;
    public void Reset() { }

    public Signal Evaluate(in Signal input, double dt)
        => Signal.Digital(Math.Abs(input.ScalarValue) > _config.Threshold);
}
