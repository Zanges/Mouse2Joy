using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

/// <summary>
/// Limits how fast |output| can grow. Decreases pass through unchanged.
/// SecondsToFull is the time to ramp from 0 to ±1 if upstream stays pegged.
/// State: tracks last output across ticks.
/// </summary>
internal sealed class RampUpEvaluator : IModifierEvaluator
{
    private readonly RampUpModifier _config;
    private double _last;

    public RampUpEvaluator(RampUpModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;
    public void Reset() => _last = 0;

    public Signal Evaluate(in Signal input, double dt)
    {
        var target = input.ScalarValue;
        if (double.IsNaN(target))
        {
            target = 0;
        }

        if (target > 1.0)
        {
            target = 1.0;
        }
        else if (target < -1.0)
        {
            target = -1.0;
        }

        // Decreases (toward 0 in magnitude, or sign change) pass through.
        // Increases in |x| are rate-limited.
        var seconds = _config.SecondsToFull <= 0 ? 0 : _config.SecondsToFull;
        if (seconds <= 0 || dt <= 0)
        {
            _last = target;
            return Signal.Scalar(_last);
        }

        var maxStep = dt / seconds; // fraction of full-deflection per tick
        var newVal = _last;

        // Determine if this is an "increase" — magnitude growing in the same
        // sign — or a "decrease" / sign-flip (passes through).
        var sameSign = (target >= 0 && _last >= 0) || (target <= 0 && _last <= 0);
        if (sameSign && Math.Abs(target) > Math.Abs(_last))
        {
            // Walk toward target, capped by maxStep.
            var diff = target - _last;
            var step = Math.Sign(diff) * Math.Min(Math.Abs(diff), maxStep);
            newVal = _last + step;
        }
        else
        {
            // Decrease or sign-flip → instant.
            newVal = target;
        }

        if (newVal > 1.0)
        {
            newVal = 1.0;
        }
        else if (newVal < -1.0)
        {
            newVal = -1.0;
        }

        _last = newVal;
        return Signal.Scalar(newVal);
    }
}
