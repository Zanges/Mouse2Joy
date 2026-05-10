using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

internal sealed class DigitalToScalarEvaluator : IModifierEvaluator
{
    private readonly DigitalToScalarModifier _config;

    public DigitalToScalarEvaluator(DigitalToScalarModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;
    public void Reset() { }

    public Signal Evaluate(in Signal input, double dt)
        => Signal.Scalar(input.DigitalValue ? _config.OnValue : _config.OffValue);
}
