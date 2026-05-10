using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.StickModels;

/// <summary>
/// Stateful translator from accumulated mouse-delta input over a tick to a
/// signed deflection in [-1, 1]. Each instance is owned by exactly one
/// binding and is mutated only on the engine tick thread.
/// </summary>
public interface IStickProcessor
{
    /// <summary>The configuration this processor was built from. Used by the
    /// resolver to detect when a binding's stick model has changed and the
    /// cached processor must be rebuilt. Records have value equality, so a
    /// simple inequality check suffices.</summary>
    StickModel Model { get; }

    /// <summary>Reset internal state to zero (e.g. on profile switch or soft-mute resume).</summary>
    void Reset();

    /// <summary>Add a single input delta as it arrives during a tick.</summary>
    void AddDelta(double delta);

    /// <summary>Advance the processor by <paramref name="dt"/> seconds and return the current deflection.</summary>
    double Advance(double dt);
}
