using Mouse2Joy.Persistence;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers;

/// <summary>
/// One per binding. Owns a <see cref="ISourceAdapter"/> + ordered list of
/// <see cref="IModifierEvaluator"/>. The resolver feeds raw events to the
/// adapter during the tick, then calls <see cref="EndOfTick"/> to produce a
/// final signal that the resolver routes to the appropriate output bucket.
///
/// If the chain is invalid (type mismatch detected at construction time),
/// <see cref="IsValid"/> is false and <see cref="EndOfTick"/> returns the
/// adapter's signal verbatim — the resolver should treat the binding as
/// disabled in this case.
/// </summary>
public sealed class ChainEvaluator
{
    private readonly ISourceAdapter _adapter;
    private readonly List<IModifierEvaluator> _evaluators;
    private readonly IReadOnlyList<Modifier> _modifierConfigs;
    public bool IsValid { get; }
    public string? InvalidReason { get; }

    public ChainEvaluator(InputSource source, IReadOnlyList<Modifier> modifiers, OutputTarget target)
    {
        _adapter = ChainBuilder.BuildAdapter(source);
        _evaluators = new List<IModifierEvaluator>(modifiers.Count);
        _modifierConfigs = modifiers;
        foreach (var m in modifiers)
            _evaluators.Add(ChainBuilder.BuildEvaluator(m));
        var validation = ChainValidator.Validate(source, modifiers, target);
        IsValid = validation.IsValid;
        InvalidReason = validation.ErrorMessage;
    }

    public ISourceAdapter Adapter => _adapter;
    public IReadOnlyList<Modifier> Modifiers => _modifierConfigs;

    public bool MatchesEvent(in RawEvent ev) => _adapter.Matches(in ev);
    public void Apply(in RawEvent ev) => _adapter.Apply(in ev);

    public void Reset()
    {
        _adapter.Reset();
        foreach (var e in _evaluators)
            e.Reset();
    }

    public Signal EndOfTick(double dt)
    {
        var sig = _adapter.EndOfTick();
        for (int i = 0; i < _evaluators.Count; i++)
        {
            var modifier = _modifierConfigs[i];
            if (!modifier.Enabled)
                continue;
            sig = _evaluators[i].Evaluate(in sig, dt);
        }
        return sig;
    }

    /// <summary>
    /// True iff the cached chain matches the given binding's modifier list
    /// by value-equality (records compare structurally). Used by the
    /// resolver to decide whether to keep this evaluator (preserving state)
    /// or rebuild from scratch.
    /// </summary>
    public bool ConfigMatches(IReadOnlyList<Modifier> modifiers)
    {
        if (_modifierConfigs.Count != modifiers.Count) return false;
        for (int i = 0; i < modifiers.Count; i++)
            if (!Equals(_modifierConfigs[i], modifiers[i]))
                return false;
        return true;
    }
}
