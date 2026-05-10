using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

/// <summary>
/// Limits how fast |output| can shrink. Increases pass through unchanged.
/// SecondsFromFull is the time to ramp from ±1 back to 0 if upstream
/// snaps to 0. State: tracks last output across ticks.
/// </summary>
internal sealed class RampDownEvaluator : IModifierEvaluator
{
    private readonly RampDownModifier _config;
    private double _last;

    public RampDownEvaluator(RampDownModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;
    public void Reset() => _last = 0;

    public Signal Evaluate(in Signal input, double dt)
    {
        var target = input.ScalarValue;
        if (double.IsNaN(target)) target = 0;
        if (target > 1.0) target = 1.0;
        else if (target < -1.0) target = -1.0;

        var seconds = _config.SecondsFromFull <= 0 ? 0 : _config.SecondsFromFull;
        if (seconds <= 0 || dt <= 0)
        {
            _last = target;
            return Signal.Scalar(_last);
        }

        var maxStep = dt / seconds;
        var newVal = _last;

        // Same-sign + magnitude shrinking → ramp; otherwise instant.
        var sameSign = (target >= 0 && _last >= 0) || (target <= 0 && _last <= 0);
        if (sameSign && Math.Abs(target) < Math.Abs(_last))
        {
            var diff = target - _last;
            var step = Math.Sign(diff) * Math.Min(Math.Abs(diff), maxStep);
            newVal = _last + step;
        }
        else
        {
            newVal = target;
        }

        if (newVal > 1.0) newVal = 1.0;
        else if (newVal < -1.0) newVal = -1.0;
        _last = newVal;
        return Signal.Scalar(newVal);
    }
}
