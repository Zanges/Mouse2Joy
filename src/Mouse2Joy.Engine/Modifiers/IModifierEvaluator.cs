using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers;

/// <summary>
/// Stateful evaluator for a single modifier in a binding's chain. Built once
/// per binding/modifier pair and reused across ticks. Owned by exactly one
/// chain; mutated only on the engine tick thread.
/// </summary>
public interface IModifierEvaluator
{
    /// <summary>The configuration this evaluator was built from. Records compare by
    /// value, so the resolver can detect when a chain has changed and evict.</summary>
    Modifier Config { get; }

    /// <summary>Reset internal state to zero (e.g. on profile switch or soft-mute resume).</summary>
    void Reset();

    /// <summary>
    /// Apply the modifier to the input signal. <paramref name="dt"/> is the
    /// tick interval in seconds (used by stateful, time-dependent evaluators
    /// such as ramps and stick dynamics). Disabled modifiers are NOT called —
    /// the chain skips them and passes input through.
    /// </summary>
    Signal Evaluate(in Signal input, double dt);
}
