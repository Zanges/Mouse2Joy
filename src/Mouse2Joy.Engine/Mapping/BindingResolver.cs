using Mouse2Joy.Engine.Modifiers;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Mapping;

/// <summary>
/// Two responsibilities:
/// 1. Predicate <see cref="ShouldSwallow"/> — used by the input backend's
///    suppression decision. Pure over the active profile snapshot.
/// 2. Apply an event to output state buckets — called on the engine thread
///    after the event is dequeued. Forwards to per-binding ChainEvaluators.
///
/// Stateful chain evaluators live in <see cref="OutputStateBuckets.Chains"/>.
/// They are created lazily on first event for a binding (via
/// <see cref="EnsureChain"/>) and evicted on profile change when a binding's
/// modifier list value-changes.
/// </summary>
internal sealed class BindingResolver
{
    private Profile _profile;

    public BindingResolver(Profile profile)
    {
        _profile = profile;
    }

    public Profile Profile => _profile;

    public void SetProfile(Profile profile, OutputStateBuckets buckets)
    {
        _profile = profile;
        // Reset transient outputs but DON'T blow away the chain cache —
        // we only evict the chains whose configuration actually changed.
        // Soft-mute uses ResetForIdleReport to zero per-chain state without
        // dropping the chains.
        buckets.Buttons.Clear();
        buckets.DPad.Clear();
        buckets.Triggers.Clear();

        var bindingById = profile.Bindings.ToDictionary(b => b.Id);
        foreach (var id in buckets.Chains.Keys.ToList())
        {
            if (!bindingById.TryGetValue(id, out var b))
            {
                buckets.Chains.Remove(id);
                continue;
            }
            var cached = buckets.Chains[id];
            // Source kind change must rebuild (different adapter); modifier
            // list value-equality covers the rest.
            if (!Equals(cached.Adapter.Source, b.Source) || !cached.ConfigMatches(b.Modifiers))
            {
                buckets.Chains.Remove(id);
            }
        }
    }

    /// <summary>
    /// True if the event matches any enabled binding source on the active
    /// profile that has SuppressInput set. The event is still applied to
    /// output state regardless — suppression only governs whether the real
    /// input is forwarded to the OS.
    /// </summary>
    public bool ShouldSwallow(in RawEvent ev)
    {
        var bindings = _profile.Bindings;
        for (int i = 0; i < bindings.Count; i++)
        {
            var b = bindings[i];
            if (!b.Enabled || !b.SuppressInput)
            {
                continue;
            }

            if (Matches(b.Source, in ev))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Apply the event to output state buckets, mutating them in place.</summary>
    public void Apply(in RawEvent ev, OutputStateBuckets buckets)
    {
        var bindings = _profile.Bindings;
        for (int i = 0; i < bindings.Count; i++)
        {
            var b = bindings[i];
            if (!b.Enabled)
            {
                continue;
            }

            if (!Matches(b.Source, in ev))
            {
                continue;
            }

            var chain = EnsureChain(b, buckets);
            if (!chain.IsValid)
            {
                continue;
            }

            chain.Apply(in ev);
        }
    }

    /// <summary>
    /// End-of-tick: walk every enabled binding's chain to produce its final
    /// signal, then route per target type. Stick axes sum and clamp to
    /// [-1, 1]; triggers fold via |x| then sum and clamp to [0, 1]; buttons
    /// and dpad use OR (any chain producing true wins).
    /// </summary>
    public void AdvanceTick(double dt, OutputStateBuckets buckets, Dictionary<(Stick, AxisComponent), double> stickFinal)
    {
        stickFinal.Clear();
        // Triggers and digital outputs are recomputed every tick from chain
        // values; clear them so a chain that no longer fires drops to zero.
        buckets.Triggers.Clear();
        buckets.Buttons.Clear();
        buckets.DPad.Clear();

        foreach (var binding in _profile.Bindings)
        {
            if (!binding.Enabled)
            {
                continue;
            }

            var chain = EnsureChain(binding, buckets);
            if (!chain.IsValid)
            {
                continue;
            }

            var sig = chain.EndOfTick(dt);

            switch (binding.Target)
            {
                case StickAxisTarget sa:
                    {
                        if (sig.Type != SignalType.Scalar)
                        {
                            continue;
                        }

                        var key = (sa.Stick, sa.Component);
                        var existing = stickFinal.TryGetValue(key, out var v) ? v : 0.0;
                        stickFinal[key] = Clamp1(existing + sig.ScalarValue);
                        break;
                    }
                case TriggerTarget tt:
                    {
                        if (sig.Type != SignalType.Scalar)
                        {
                            continue;
                        }

                        var folded = Math.Abs(sig.ScalarValue);
                        var existing = buckets.Triggers.TryGetValue(tt.Trigger, out var v) ? v : 0.0;
                        var sum = existing + folded;
                        if (sum > 1.0)
                        {
                            sum = 1.0;
                        }

                        buckets.Triggers[tt.Trigger] = sum;
                        break;
                    }
                case ButtonTarget bt:
                    {
                        if (sig.Type != SignalType.Digital)
                        {
                            continue;
                        }

                        if (!buckets.Buttons.TryGetValue(bt.Button, out var prev))
                        {
                            prev = false;
                        }

                        buckets.Buttons[bt.Button] = prev || sig.DigitalValue;
                        break;
                    }
                case DPadTarget dp:
                    {
                        if (sig.Type != SignalType.Digital)
                        {
                            continue;
                        }

                        if (!buckets.DPad.TryGetValue(dp.Direction, out var prev))
                        {
                            prev = false;
                        }

                        buckets.DPad[dp.Direction] = prev || sig.DigitalValue;
                        break;
                    }
            }
        }
    }

    private static ChainEvaluator EnsureChain(Binding binding, OutputStateBuckets buckets)
    {
        if (buckets.Chains.TryGetValue(binding.Id, out var existing)
            && Equals(existing.Adapter.Source, binding.Source)
            && existing.ConfigMatches(binding.Modifiers))
        {
            return existing;
        }
        var chain = new ChainEvaluator(binding.Source, binding.Modifiers, binding.Target);
        buckets.Chains[binding.Id] = chain;
        return chain;
    }

    private static double Clamp1(double v) => v > 1.0 ? 1.0 : v < -1.0 ? -1.0 : v;

    private static bool Matches(InputSource source, in RawEvent ev) => source switch
    {
        MouseAxisSource => ev.Kind == RawEventKind.MouseMove,
        MouseButtonSource mb => ev.Kind == RawEventKind.MouseButton && ev.MouseButton == mb.Button,
        MouseScrollSource ms => ev.Kind == RawEventKind.MouseScroll && ev.Scroll == ms.Direction,
        KeySource ks => ev.Kind == RawEventKind.Key && ev.Key.Equals(ks.Key),
        _ => false
    };
}
