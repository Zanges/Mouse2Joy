using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers.Evaluators;

/// <summary>
/// Delta → Scalar integrator with three modes (Velocity / Accumulator /
/// Persistent). Mode-by-mode behavior preserves the v1 stick processors
/// exactly — same formulas, same clamping, same parameter semantics.
/// </summary>
internal sealed class StickDynamicsEvaluator : IModifierEvaluator
{
    private StickDynamicsModifier _config;
    private double _deflection;

    public StickDynamicsEvaluator(StickDynamicsModifier config)
    {
        _config = config;
    }

    public Modifier Config => _config;

    public void Reset() => _deflection = 0;

    public Signal Evaluate(in Signal input, double dt)
    {
        var delta = input.DeltaValue;

        if (dt <= 0)
            return Signal.Scalar(_deflection);

        switch (_config.Mode)
        {
            case StickDynamicsMode.Velocity:
            {
                // Param1 = DecayPerSecond, Param2 = MaxVelocityCounts
                var maxVel = _config.Param2 <= 0 ? 1.0 : _config.Param2;
                var velocity = delta / dt;
                var target = velocity / maxVel;
                if (target > 1.0) target = 1.0;
                else if (target < -1.0) target = -1.0;

                var decay = _config.Param1 < 0 ? 0 : _config.Param1;
                var weight = Math.Exp(-decay * dt);
                _deflection = target + (_deflection - target) * weight;
                break;
            }
            case StickDynamicsMode.Accumulator:
            {
                // Param1 = SpringPerSecond, Param2 = CountsPerFullDeflection
                var perFull = _config.Param2 <= 0 ? 1.0 : _config.Param2;
                _deflection += delta / perFull;
                var spring = _config.Param1 < 0 ? 0 : _config.Param1;
                _deflection *= Math.Exp(-spring * dt);
                break;
            }
            case StickDynamicsMode.Persistent:
            {
                // Param1 = CountsPerFullDeflection, Param2 ignored.
                var perFull = _config.Param1 <= 0 ? 1.0 : _config.Param1;
                _deflection += delta / perFull;
                break;
            }
        }

        if (_deflection > 1.0) _deflection = 1.0;
        else if (_deflection < -1.0) _deflection = -1.0;

        return Signal.Scalar(_deflection);
    }
}
