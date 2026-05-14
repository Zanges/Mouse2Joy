using Mouse2Joy.Engine.Modifiers;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Mapping;

/// <summary>
/// Per-tick output staging area, owned exclusively by the engine tick
/// thread. Holds the latest known state of every gamepad output so the
/// final <see cref="XInputReport"/> can be composed at end-of-tick.
///
/// Discrete and analog outputs are computed by walking each binding's
/// <see cref="ChainEvaluator"/> in <see cref="BindingResolver.AdvanceTick"/>;
/// the buckets here just buffer the result.
///
/// Stateful chain evaluators are cached in <see cref="Chains"/>, keyed by
/// binding id. The resolver's <c>SetProfile</c> evicts entries whose
/// modifier list has changed (record value-equality).
/// </summary>
internal sealed class OutputStateBuckets
{
    public readonly Dictionary<GamepadButton, bool> Buttons = new();
    public readonly Dictionary<DPadDirection, bool> DPad = new();
    public readonly Dictionary<Trigger, double> Triggers = new();

    /// <summary>Per-binding chain evaluators. Stateful — preserved across ticks; reset on soft-mute.</summary>
    public readonly Dictionary<Guid, ChainEvaluator> Chains = new();

    public void ResetForIdleReport()
    {
        Buttons.Clear();
        DPad.Clear();
        Triggers.Clear();
        // Keep chains but zero their internal state so resuming from a
        // soft-mute starts clean (no held key signal, no integrated stick
        // deflection, no ramp residue).
        foreach (var c in Chains.Values)
        {
            c.Reset();
        }
    }
}
