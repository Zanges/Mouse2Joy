using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

/// <summary>
/// While the input is held true, the output toggles between true/false at
/// the configured Hz. Period = 1 / Hz seconds; first half is true, second
/// half is false. When the input drops to false, the output goes false and
/// the phase resets so the next press starts at "true" cleanly.
/// </summary>
internal sealed class AutoFireEvaluator : IModifierEvaluator
{
    private readonly AutoFireModifier _config;
    private double _phase;

    public AutoFireEvaluator(AutoFireModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;

    public void Reset() => _phase = 0;

    public Signal Evaluate(in Signal input, double dt)
    {
        if (!input.DigitalValue)
        {
            _phase = 0;
            return Signal.Digital(false);
        }
        if (_config.Hz <= 0)
        {
            // Passthrough — held input passes straight through.
            return Signal.Digital(true);
        }
        var period = 1.0 / _config.Hz;
        _phase += dt;
        if (_phase >= period)
        {
            _phase -= period;
        }
        // First half of the period: true; second half: false. Net duty = 50%.
        return Signal.Digital(_phase < period * 0.5);
    }
}
