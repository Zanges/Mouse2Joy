using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers;

/// <summary>
/// Sits at the head of every chain. Consumes <see cref="RawEvent"/>s as they
/// arrive within a tick and produces an initial <see cref="Signal"/> at
/// end-of-tick. Owns whatever state is needed to translate the source kind:
///
/// <list type="bullet">
///   <item>Mouse-axis: signed delta accumulator, reset on EndOfTick.</item>
///   <item>Mouse-button / Key: latched Digital, transitions on key-down/up.</item>
///   <item>Scroll: momentary Digital, latched true on event, reset at EndOfTick.</item>
/// </list>
/// </summary>
public interface ISourceAdapter
{
    InputSource Source { get; }

    /// <summary>The signal type this adapter emits. Constant per source kind.</summary>
    SignalType OutputType { get; }

    /// <summary>True if this raw event matches our source. Mirrors today's <c>BindingResolver.Matches</c>.</summary>
    bool Matches(in RawEvent ev);

    /// <summary>Apply a matching event to internal state.</summary>
    void Apply(in RawEvent ev);

    /// <summary>
    /// Read-and-finalize the per-tick signal. Resets per-tick-only state
    /// (delta accumulator, scroll momentary) but preserves latched state
    /// (key down).
    /// </summary>
    Signal EndOfTick();

    /// <summary>Zero all state (latched keys included). Used on profile change and soft-mute resume.</summary>
    void Reset();
}
