using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.StickModels;

/// <summary>
/// Accumulator model: integrate mouse deltas into a virtual stick position
/// (scaled so CountsPerFullDeflection = 1.0), and apply an exponential
/// spring back toward center each tick.
/// </summary>
public sealed class AccumulatorStickProcessor : IStickProcessor
{
    private readonly AccumulatorStickModel _config;
    private double _pendingDelta;
    private double _deflection;

    public AccumulatorStickProcessor(AccumulatorStickModel config)
    {
        _config = config;
    }

    public StickModel Model => _config;

    public void Reset()
    {
        _pendingDelta = 0;
        _deflection = 0;
    }

    public void AddDelta(double delta)
    {
        _pendingDelta += delta;
    }

    public double Advance(double dt)
    {
        if (dt <= 0)
            return _deflection;

        var perFull = _config.CountsPerFullDeflection <= 0 ? 1.0 : _config.CountsPerFullDeflection;
        _deflection += _pendingDelta / perFull;
        _pendingDelta = 0;

        var spring = _config.SpringPerSecond < 0 ? 0 : _config.SpringPerSecond;
        _deflection *= Math.Exp(-spring * dt);

        if (_deflection > 1.0) _deflection = 1.0;
        else if (_deflection < -1.0) _deflection = -1.0;

        return _deflection;
    }
}
