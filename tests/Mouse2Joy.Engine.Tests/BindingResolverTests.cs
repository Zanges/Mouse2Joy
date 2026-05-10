using FluentAssertions;
using Mouse2Joy.Engine;
using Mouse2Joy.Engine.Mapping;
using Mouse2Joy.Engine.StickModels;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Tests;

public class BindingResolverTests
{
    private static readonly VirtualKey W = new(0x11, false);
    private static readonly VirtualKey S = new(0x1F, false);

    [Fact]
    public void Should_swallow_event_matching_enabled_binding()
    {
        var profile = new Profile {
            Name = "p",
            Bindings = { new Binding { Source = new KeySource(W), Target = new ButtonTarget(GamepadButton.A) } }
        };
        var r = new BindingResolver(profile);
        var ev = RawEvent.ForKey(W, true, KeyModifiers.None, 0);
        r.ShouldSwallow(in ev).Should().BeTrue();
    }

    [Fact]
    public void Should_not_swallow_unmatched_event()
    {
        var profile = new Profile {
            Name = "p",
            Bindings = { new Binding { Source = new KeySource(W), Target = new ButtonTarget(GamepadButton.A) } }
        };
        var r = new BindingResolver(profile);
        var ev = RawEvent.ForKey(S, true, KeyModifiers.None, 0);
        r.ShouldSwallow(in ev).Should().BeFalse();
    }

    [Fact]
    public void Disabled_bindings_are_ignored()
    {
        var profile = new Profile {
            Name = "p",
            Bindings = { new Binding { Source = new KeySource(W), Target = new ButtonTarget(GamepadButton.A), Enabled = false } }
        };
        var r = new BindingResolver(profile);
        var ev = RawEvent.ForKey(W, true, KeyModifiers.None, 0);
        r.ShouldSwallow(in ev).Should().BeFalse();
    }

    [Fact]
    public void Apply_sets_button_state_on_keydown()
    {
        var profile = new Profile {
            Name = "p",
            Bindings = { new Binding { Source = new KeySource(W), Target = new ButtonTarget(GamepadButton.A) } }
        };
        var r = new BindingResolver(profile);
        var buckets = new OutputStateBuckets();
        r.Apply(RawEvent.ForKey(W, true, KeyModifiers.None, 0), buckets);
        buckets.Buttons.Should().ContainKey(GamepadButton.A);
        buckets.Buttons[GamepadButton.A].Should().BeTrue();
        r.Apply(RawEvent.ForKey(W, false, KeyModifiers.None, 0), buckets);
        buckets.Buttons[GamepadButton.A].Should().BeFalse();
    }

    [Fact]
    public void Apply_sets_dpad_state()
    {
        var profile = new Profile {
            Name = "p",
            Bindings = { new Binding { Source = new KeySource(W), Target = new DPadTarget(DPadDirection.Up) } }
        };
        var r = new BindingResolver(profile);
        var buckets = new OutputStateBuckets();
        r.Apply(RawEvent.ForKey(W, true, KeyModifiers.None, 0), buckets);
        buckets.DPad[DPadDirection.Up].Should().BeTrue();
    }

    [Fact]
    public void Mouse_axis_routed_through_velocity_processor()
    {
        var profile = new Profile {
            Name = "p",
            Bindings = {
                new Binding {
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    StickModel = new VelocityStickModel(DecayPerSecond: 1000.0, MaxVelocityCounts: 100.0),
                }
            }
        };
        var r = new BindingResolver(profile);
        var buckets = new OutputStateBuckets();
        r.Apply(RawEvent.ForMouseMove(50, 0, 0), buckets);
        var stickFinal = new Dictionary<(Stick, AxisComponent), double>();
        r.AdvanceTick(0.01, buckets, stickFinal);
        stickFinal.Should().ContainKey((Stick.Left, AxisComponent.X));
        // High DecayPerSecond => near-instant tracking; deflection should reach unity.
        stickFinal[(Stick.Left, AxisComponent.X)].Should().BeApproximately(1.0, 1e-3);
    }

    [Fact]
    public void SetProfile_rebuilds_processor_on_stick_model_kind_change()
    {
        var bindingId = Guid.NewGuid();
        var profile = new Profile {
            Name = "p",
            Bindings = {
                new Binding {
                    Id = bindingId,
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    StickModel = new VelocityStickModel(DecayPerSecond: 8.0, MaxVelocityCounts: 800.0),
                }
            }
        };
        var r = new BindingResolver(profile);
        var buckets = new OutputStateBuckets();

        // Prime the cache.
        r.Apply(RawEvent.ForMouseMove(10, 0, 0), buckets);
        buckets.StickProcessors[bindingId].Should().BeOfType<VelocityStickProcessor>();

        // Swap to a different model kind on the same binding ID.
        var newProfile = new Profile {
            Name = "p",
            Bindings = {
                new Binding {
                    Id = bindingId,
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    StickModel = new AccumulatorStickModel(SpringPerSecond: 5.0, CountsPerFullDeflection: 200.0),
                }
            }
        };
        r.SetProfile(newProfile, buckets);
        buckets.StickProcessors.ContainsKey(bindingId).Should().BeFalse(
            "the cached processor should be evicted so the next event rebuilds with the new model");

        // Next event lazily rebuilds with the new kind.
        r.Apply(RawEvent.ForMouseMove(10, 0, 0), buckets);
        buckets.StickProcessors[bindingId].Should().BeOfType<AccumulatorStickProcessor>();
    }

    [Fact]
    public void SetProfile_rebuilds_processor_on_stick_model_parameter_change()
    {
        var bindingId = Guid.NewGuid();
        var original = new VelocityStickModel(DecayPerSecond: 8.0, MaxVelocityCounts: 800.0);
        var profile = new Profile {
            Name = "p",
            Bindings = {
                new Binding {
                    Id = bindingId,
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    StickModel = original,
                }
            }
        };
        var r = new BindingResolver(profile);
        var buckets = new OutputStateBuckets();

        r.Apply(RawEvent.ForMouseMove(10, 0, 0), buckets);
        buckets.StickProcessors[bindingId].Model.Should().Be(original);

        var tweaked = new VelocityStickModel(DecayPerSecond: 16.0, MaxVelocityCounts: 800.0);
        var newProfile = new Profile {
            Name = "p",
            Bindings = {
                new Binding {
                    Id = bindingId,
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    StickModel = tweaked,
                }
            }
        };
        r.SetProfile(newProfile, buckets);
        buckets.StickProcessors.ContainsKey(bindingId).Should().BeFalse();

        r.Apply(RawEvent.ForMouseMove(10, 0, 0), buckets);
        buckets.StickProcessors[bindingId].Model.Should().Be(tweaked);
    }

    [Fact]
    public void SetProfile_keeps_processor_when_stick_model_unchanged()
    {
        // Regression guard: editing an unrelated property (e.g. SuppressInput)
        // must not churn the processor cache. Record value-equality means an
        // equivalent StickModel instance compares equal.
        var bindingId = Guid.NewGuid();
        var model = new AccumulatorStickModel(SpringPerSecond: 4.0, CountsPerFullDeflection: 150.0);
        var profile = new Profile {
            Name = "p",
            Bindings = {
                new Binding {
                    Id = bindingId,
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    StickModel = model,
                    SuppressInput = false,
                }
            }
        };
        var r = new BindingResolver(profile);
        var buckets = new OutputStateBuckets();

        r.Apply(RawEvent.ForMouseMove(10, 0, 0), buckets);
        var originalProcessor = buckets.StickProcessors[bindingId];

        var newProfile = new Profile {
            Name = "p",
            Bindings = {
                new Binding {
                    Id = bindingId,
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    StickModel = new AccumulatorStickModel(SpringPerSecond: 4.0, CountsPerFullDeflection: 150.0),
                    SuppressInput = true,
                }
            }
        };
        r.SetProfile(newProfile, buckets);
        buckets.StickProcessors.Should().ContainKey(bindingId);
        buckets.StickProcessors[bindingId].Should().BeSameAs(originalProcessor);
    }

    [Fact]
    public void Multiple_bindings_to_same_target_compose()
    {
        var profile = new Profile {
            Name = "p",
            Bindings = {
                // Two independent stick bindings sharing the same axis target.
                // We expect their contributions to sum (clamped).
                new Binding {
                    Source = new KeySource(W),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    Curve = new Curve(Sensitivity: 0.5, 0, 0, 1)
                },
                new Binding {
                    Source = new KeySource(S),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    Curve = new Curve(Sensitivity: 0.5, 0, 0, 1)
                }
            }
        };
        var r = new BindingResolver(profile);
        var buckets = new OutputStateBuckets();
        // Last one wins for direct (key-bound) contributions because both
        // write to the same StickDirect slot. This is intentional: digital
        // sources to the same axis form an exclusive selection, and users
        // who want bipolar behavior should bind +/- to two different axes
        // OR use scroll/mouse-button sources. Test confirms the documented
        // semantic.
        r.Apply(RawEvent.ForKey(W, true, KeyModifiers.None, 0), buckets);
        r.Apply(RawEvent.ForKey(S, true, KeyModifiers.None, 0), buckets);
        var stickFinal = new Dictionary<(Stick, AxisComponent), double>();
        r.AdvanceTick(0.01, buckets, stickFinal);
        // Each binding evaluates curve(direct) -> sens=0.5 * 1.0 = 0.5; both
        // produce 0.5 and AdvanceTick sums them -> 1.0 (clamped).
        stickFinal[(Stick.Left, AxisComponent.X)].Should().BeApproximately(1.0, 1e-6);
    }
}
