using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

/// <summary>
/// Caps-lock-style toggle. Tracks the previous input level so we can detect
/// a rising edge (false→true) and flip the held output. Falling edges are
/// ignored. Reset zeros both the latched output and the edge tracker so the
/// modifier behaves predictably after profile change / soft-mute.
/// </summary>
internal sealed class ToggleEvaluator : IModifierEvaluator
{
    private readonly ToggleModifier _config;
    private bool _outputHeld;
    private bool _prevInput;

    public ToggleEvaluator(ToggleModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;

    public void Reset()
    {
        _outputHeld = false;
        _prevInput = false;
    }

    public Signal Evaluate(in Signal input, double dt)
    {
        var current = input.DigitalValue;
        if (current && !_prevInput)
        {
            _outputHeld = !_outputHeld;
        }

        _prevInput = current;
        return Signal.Digital(_outputHeld);
    }
}
