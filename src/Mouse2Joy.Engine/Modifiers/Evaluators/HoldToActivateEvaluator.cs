using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

/// <summary>
/// Output goes true only after the input has been continuously true for
/// HoldSeconds. The instant the input drops, the output drops and the
/// timer resets. HoldSeconds &lt;= 0 means passthrough.
/// </summary>
internal sealed class HoldToActivateEvaluator : IModifierEvaluator
{
    private readonly HoldToActivateModifier _config;
    private double _heldFor;

    public HoldToActivateEvaluator(HoldToActivateModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;

    public void Reset() => _heldFor = 0;

    public Signal Evaluate(in Signal input, double dt)
    {
        if (!input.DigitalValue)
        {
            _heldFor = 0;
            return Signal.Digital(false);
        }
        if (_config.HoldSeconds <= 0)
        {
            return Signal.Digital(true);
        }

        _heldFor += dt;
        return Signal.Digital(_heldFor >= _config.HoldSeconds);
    }
}
