using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

internal sealed class InvertEvaluator : IModifierEvaluator
{
    private readonly InvertModifier _config;

    public InvertEvaluator(InvertModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;
    public void Reset() { }

    public Signal Evaluate(in Signal input, double dt)
        => Signal.Scalar(-input.ScalarValue);
}
