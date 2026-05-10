using Mouse2Joy.Engine.StickModels;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Mapping;

/// <summary>
/// Two responsibilities:
/// 1. Predicate <see cref="ShouldSwallow"/> — used by the input backend's
///    suppression decision. Pure over the active profile snapshot.
/// 2. Apply an event to output state buckets — called on the engine thread
///    after the event is dequeued.
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
        buckets.ResetForIdleReport();
        // Drop processors that no longer belong to any binding to avoid leaks,
        // OR whose StickModel has changed so the next event lazily rebuilds
        // from the new config. StickModel is a record (value equality), so a
        // single != check covers both kind switches and parameter tweaks.
        // Bindings with a null StickModel use a factory default and are not
        // evicted — there is nothing to compare against.
        var bindingById = profile.Bindings.ToDictionary(b => b.Id);
        foreach (var id in buckets.StickProcessors.Keys.ToList())
        {
            if (!bindingById.TryGetValue(id, out var b))
            {
                buckets.StickProcessors.Remove(id);
                continue;
            }
            if (b.StickModel is { } model && model != buckets.StickProcessors[id].Model)
                buckets.StickProcessors.Remove(id);
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
            if (!b.Enabled || !b.SuppressInput) continue;
            if (Matches(b.Source, ev))
                return true;
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
            if (!b.Enabled) continue;
            if (!Matches(b.Source, ev)) continue;
            ApplyBinding(b, in ev, buckets);
        }
    }

    /// <summary>End-of-tick: advance stick processors and produce final stick deflections.</summary>
    public void AdvanceTick(double dt, OutputStateBuckets buckets, Dictionary<(Stick, AxisComponent), double> stickFinal)
    {
        stickFinal.Clear();

        foreach (var binding in _profile.Bindings)
        {
            if (!binding.Enabled) continue;
            if (binding.Target is not StickAxisTarget stick) continue;

            double contribution = 0;

            // Mouse-axis sources route through a stateful processor.
            if (binding.Source is MouseAxisSource && buckets.StickProcessors.TryGetValue(binding.Id, out var processor))
            {
                contribution += processor.Advance(dt);
            }

            // Direct contributions (key-held +/-1, scroll, etc.) layer on top.
            if (buckets.StickDirect.TryGetValue((stick.Stick, stick.Component), out var direct))
                contribution += direct;

            contribution = CurveEvaluator.Evaluate(contribution, binding.Curve);

            var key = (stick.Stick, stick.Component);
            if (stickFinal.TryGetValue(key, out var existing))
                stickFinal[key] = Clamp1(existing + contribution);
            else
                stickFinal[key] = Clamp1(contribution);
        }

        // Direct StickDirect entries clear at end of tick — they represent
        // momentary contributions like scroll pulses. Held-key contributions
        // are re-asserted via key-up clearing them on release; for held
        // keys we keep the value across ticks. To support both, we rely on
        // ApplyBinding to set/unset StickDirect on key down/up.
        // Therefore: do NOT clear here.
    }

    private static double Clamp1(double v) => v > 1.0 ? 1.0 : v < -1.0 ? -1.0 : v;

    private static bool Matches(InputSource source, in RawEvent ev) => source switch
    {
        MouseAxisSource ma => ev.Kind == RawEventKind.MouseMove && (ma.Axis == MouseAxis.X || ma.Axis == MouseAxis.Y),
        MouseButtonSource mb => ev.Kind == RawEventKind.MouseButton && ev.MouseButton == mb.Button,
        MouseScrollSource ms => ev.Kind == RawEventKind.MouseScroll && ev.Scroll == ms.Direction,
        KeySource ks => ev.Kind == RawEventKind.Key && ev.Key.Equals(ks.Key),
        _ => false
    };

    private static void ApplyBinding(Binding binding, in RawEvent ev, OutputStateBuckets buckets)
    {
        switch (binding.Target)
        {
            case ButtonTarget bt:
                buckets.Buttons[bt.Button] = ReadDigitalDown(binding.Source, in ev);
                break;
            case DPadTarget dp:
                buckets.DPad[dp.Direction] = ReadDigitalDown(binding.Source, in ev);
                break;
            case TriggerTarget tt:
                buckets.Triggers[tt.Trigger] = ReadDigitalDown(binding.Source, in ev) ? 1.0 : 0.0;
                break;
            case StickAxisTarget sa:
                ApplyStick(binding, sa, in ev, buckets);
                break;
        }
    }

    private static bool ReadDigitalDown(InputSource source, in RawEvent ev) => source switch
    {
        MouseButtonSource => ev.ButtonDown,
        KeySource => ev.KeyDown,
        MouseScrollSource => true, // scroll is treated as a momentary press; release happens via tick reset
        _ => false
    };

    private static void ApplyStick(Binding binding, StickAxisTarget sa, in RawEvent ev, OutputStateBuckets buckets)
    {
        var key = (sa.Stick, sa.Component);

        switch (binding.Source)
        {
            case MouseAxisSource ma:
            {
                if (!buckets.StickProcessors.TryGetValue(binding.Id, out var processor))
                {
                    processor = StickProcessorFactory.Create(binding.StickModel);
                    buckets.StickProcessors[binding.Id] = processor;
                }
                var delta = ma.Axis == MouseAxis.X ? ev.MouseDeltaX : ev.MouseDeltaY;
                if (delta != 0)
                    processor.AddDelta(delta);
                break;
            }
            case KeySource:
            {
                // A key bound to a stick axis acts as +1 or -1 while held.
                // Sign is encoded in the curve's sensitivity (negative = inverted).
                // The simplest convention: down -> +1, up -> 0; users invert with negative sensitivity.
                buckets.StickDirect[key] = ev.KeyDown ? 1.0 : 0.0;
                break;
            }
            case MouseButtonSource:
            {
                buckets.StickDirect[key] = ev.ButtonDown ? 1.0 : 0.0;
                break;
            }
            case MouseScrollSource:
            {
                // A scroll click contributes a momentary +1 (cleared at end of tick by InputEngine).
                buckets.StickDirect[key] = 1.0;
                break;
            }
        }
    }
}
