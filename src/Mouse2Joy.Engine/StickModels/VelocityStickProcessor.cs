using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.StickModels;

/// <summary>
/// Velocity model: deflection follows recent mouse velocity, decays
/// exponentially toward zero when the mouse stops. Feels like a console FPS
/// camera stick. Per tick:
///   velocity = sum(deltas-this-tick) / dt
///   target   = clamp(velocity / MaxVelocityCounts, -1, 1)
///   deflection = lerp(deflection, target, decayWeight)  via mix with exp(-decay*dt)
/// </summary>
public sealed class VelocityStickProcessor : IStickProcessor
{
    private readonly VelocityStickModel _config;
    private double _accumulatedDelta;
    private double _deflection;

    public VelocityStickProcessor(VelocityStickModel config)
    {
        _config = config;
    }

    public StickModel Model => _config;

    public void Reset()
    {
        _accumulatedDelta = 0;
        _deflection = 0;
    }

    public void AddDelta(double delta)
    {
        _accumulatedDelta += delta;
    }

    public double Advance(double dt)
    {
        if (dt <= 0)
            return _deflection;

        var maxVel = _config.MaxVelocityCounts <= 0 ? 1.0 : _config.MaxVelocityCounts;
        var velocity = _accumulatedDelta / dt;
        var target = velocity / maxVel;
        if (target > 1.0) target = 1.0;
        else if (target < -1.0) target = -1.0;

        // Exponential mix toward target. With decay rate r, we want
        // deflection -> target as r -> infty (instantaneous tracking),
        // and deflection -> 0 when target == 0 (decay to center).
        // Implement as: deflection = target + (deflection - target) * exp(-r*dt).
        var decay = _config.DecayPerSecond < 0 ? 0 : _config.DecayPerSecond;
        var weight = Math.Exp(-decay * dt);
        _deflection = target + (_deflection - target) * weight;

        if (_deflection > 1.0) _deflection = 1.0;
        else if (_deflection < -1.0) _deflection = -1.0;

        _accumulatedDelta = 0;
        return _deflection;
    }
}
