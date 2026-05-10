using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.StickModels;

/// <summary>
/// Persistent model: integrate mouse deltas into a virtual stick position
/// (scaled so CountsPerFullDeflection = 1.0). No spring, no decay — the
/// stick stays where the mouse pushed it. To recenter, the user must move
/// the mouse the same distance in the opposite direction. Overshoot past
/// the unit range is clamped and discarded, so recovery distance is
/// measured from the clamped point, not from how far past the edge the
/// mouse was pushed.
/// </summary>
public sealed class PersistentStickProcessor : IStickProcessor
{
    private readonly PersistentStickModel _config;
    private double _pendingDelta;
    private double _deflection;

    public PersistentStickProcessor(PersistentStickModel config)
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

        if (_deflection > 1.0) _deflection = 1.0;
        else if (_deflection < -1.0) _deflection = -1.0;

        return _deflection;
    }
}
