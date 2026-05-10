using Mouse2Joy.Engine.State;

namespace Mouse2Joy.Engine;

public interface IEngineStateSource
{
    /// <summary>Latest snapshot. Lock-free read; may be at most one tick stale.</summary>
    EngineStateSnapshot Current { get; }

    /// <summary>Raised on a coalesced UI-cadence timer (~60 Hz), NOT on every engine tick.</summary>
    event Action<EngineStateSnapshot>? Tick;
}
