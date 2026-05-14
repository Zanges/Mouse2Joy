using FluentAssertions;
using Mouse2Joy.Engine.Mapping;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Tests;

public class BindingResolverTests
{
    private static readonly VirtualKey W = new(0x11, false);
    private static readonly VirtualKey S = new(0x1F, false);

    [Fact]
    public void Should_swallow_event_matching_enabled_binding()
    {
        var profile = new Profile
        {
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
        var profile = new Profile
        {
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
        var profile = new Profile
        {
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
        var profile = new Profile
        {
            Name = "p",
            Bindings = { new Binding { Source = new KeySource(W), Target = new ButtonTarget(GamepadButton.A) } }
        };
        var r = new BindingResolver(profile);
        var buckets = new OutputStateBuckets();
        r.Apply(RawEvent.ForKey(W, true, KeyModifiers.None, 0), buckets);
        var stickFinal = new Dictionary<(Stick, AxisComponent), double>();
        r.AdvanceTick(0.01, buckets, stickFinal);
        buckets.Buttons.Should().ContainKey(GamepadButton.A);
        buckets.Buttons[GamepadButton.A].Should().BeTrue();

        r.Apply(RawEvent.ForKey(W, false, KeyModifiers.None, 0), buckets);
        r.AdvanceTick(0.01, buckets, stickFinal);
        buckets.Buttons[GamepadButton.A].Should().BeFalse();
    }

    [Fact]
    public void Apply_sets_dpad_state()
    {
        var profile = new Profile
        {
            Name = "p",
            Bindings = { new Binding { Source = new KeySource(W), Target = new DPadTarget(DPadDirection.Up) } }
        };
        var r = new BindingResolver(profile);
        var buckets = new OutputStateBuckets();
        r.Apply(RawEvent.ForKey(W, true, KeyModifiers.None, 0), buckets);
        var stickFinal = new Dictionary<(Stick, AxisComponent), double>();
        r.AdvanceTick(0.01, buckets, stickFinal);
        buckets.DPad[DPadDirection.Up].Should().BeTrue();
    }

    [Fact]
    public void Mouse_axis_routed_through_velocity_stick_dynamics()
    {
        var profile = new Profile
        {
            Name = "p",
            Bindings =
            {
                new Binding
                {
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    Modifiers = new Modifier[]
                    {
                        new StickDynamicsModifier(StickDynamicsMode.Velocity, 1000.0, 100.0)
                    }
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
    public void Key_to_stick_via_digital_to_scalar_routes_correctly()
    {
        var profile = new Profile
        {
            Name = "p",
            Bindings =
            {
                new Binding
                {
                    Source = new KeySource(W),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    Modifiers = new Modifier[] { DigitalToScalarModifier.Default }
                }
            }
        };
        var r = new BindingResolver(profile);
        var buckets = new OutputStateBuckets();
        var stickFinal = new Dictionary<(Stick, AxisComponent), double>();

        r.Apply(RawEvent.ForKey(W, true, KeyModifiers.None, 0), buckets);
        r.AdvanceTick(0.01, buckets, stickFinal);
        stickFinal[(Stick.Left, AxisComponent.X)].Should().Be(1.0);

        r.Apply(RawEvent.ForKey(W, false, KeyModifiers.None, 0), buckets);
        r.AdvanceTick(0.01, buckets, stickFinal);
        stickFinal[(Stick.Left, AxisComponent.X)].Should().Be(0.0);
    }

    [Fact]
    public void SetProfile_rebuilds_chain_on_modifier_change()
    {
        var bindingId = Guid.NewGuid();
        var profile = new Profile
        {
            Name = "p",
            Bindings =
            {
                new Binding
                {
                    Id = bindingId,
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    Modifiers = new Modifier[] { new StickDynamicsModifier(StickDynamicsMode.Velocity, 8.0, 800.0) }
                }
            }
        };
        var r = new BindingResolver(profile);
        var buckets = new OutputStateBuckets();

        r.Apply(RawEvent.ForMouseMove(10, 0, 0), buckets);
        buckets.Chains[bindingId].Modifiers[0].Should().BeOfType<StickDynamicsModifier>()
            .Which.Mode.Should().Be(StickDynamicsMode.Velocity);

        var newProfile = new Profile
        {
            Name = "p",
            Bindings =
            {
                new Binding
                {
                    Id = bindingId,
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    Modifiers = new Modifier[] { new StickDynamicsModifier(StickDynamicsMode.Accumulator, 5.0, 200.0) }
                }
            }
        };
        r.SetProfile(newProfile, buckets);
        buckets.Chains.ContainsKey(bindingId).Should().BeFalse(
            "the cached chain should be evicted so the next event rebuilds with the new modifier");

        r.Apply(RawEvent.ForMouseMove(10, 0, 0), buckets);
        buckets.Chains[bindingId].Modifiers[0].Should().BeOfType<StickDynamicsModifier>()
            .Which.Mode.Should().Be(StickDynamicsMode.Accumulator);
    }

    [Fact]
    public void SetProfile_keeps_chain_when_modifier_list_unchanged()
    {
        // Regression guard: editing an unrelated property (e.g. SuppressInput)
        // must not churn the chain cache. Records compare by value, so an
        // equivalent modifier list is preserved.
        var bindingId = Guid.NewGuid();
        var profile = new Profile
        {
            Name = "p",
            Bindings =
            {
                new Binding
                {
                    Id = bindingId,
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    Modifiers = new Modifier[] { new StickDynamicsModifier(StickDynamicsMode.Accumulator, 4.0, 150.0) },
                    SuppressInput = false,
                }
            }
        };
        var r = new BindingResolver(profile);
        var buckets = new OutputStateBuckets();

        r.Apply(RawEvent.ForMouseMove(10, 0, 0), buckets);
        var originalChain = buckets.Chains[bindingId];

        var newProfile = new Profile
        {
            Name = "p",
            Bindings =
            {
                new Binding
                {
                    Id = bindingId,
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    Modifiers = new Modifier[] { new StickDynamicsModifier(StickDynamicsMode.Accumulator, 4.0, 150.0) },
                    SuppressInput = true,
                }
            }
        };
        r.SetProfile(newProfile, buckets);
        buckets.Chains.Should().ContainKey(bindingId);
        buckets.Chains[bindingId].Should().BeSameAs(originalChain);
    }

    [Fact]
    public void Two_bindings_to_same_stick_axis_sum_independently()
    {
        // Behavior change from v1: each chain holds independent state,
        // so two key-bindings to the same stick axis sum (clamped at the
        // target). Previously they shared a StickDirect bucket and last-
        // write-wins. See MODIFIER_CHAIN_REWORK key decisions.
        var profile = new Profile
        {
            Name = "p",
            Bindings =
            {
                new Binding
                {
                    Source = new KeySource(W),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    Modifiers = new Modifier[]
                    {
                        DigitalToScalarModifier.Default,
                        new OutputScaleModifier(0.5),
                    }
                },
                new Binding
                {
                    Source = new KeySource(S),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    Modifiers = new Modifier[]
                    {
                        DigitalToScalarModifier.Default,
                        new OutputScaleModifier(0.5),
                    }
                }
            }
        };
        var r = new BindingResolver(profile);
        var buckets = new OutputStateBuckets();
        r.Apply(RawEvent.ForKey(W, true, KeyModifiers.None, 0), buckets);
        r.Apply(RawEvent.ForKey(S, true, KeyModifiers.None, 0), buckets);
        var stickFinal = new Dictionary<(Stick, AxisComponent), double>();
        r.AdvanceTick(0.01, buckets, stickFinal);
        // Each chain produces 1.0 * 0.5 = 0.5; sum = 1.0 (clamped).
        stickFinal[(Stick.Left, AxisComponent.X)].Should().BeApproximately(1.0, 1e-6);
    }

    [Fact]
    public void Invalid_chain_is_skipped()
    {
        var profile = new Profile
        {
            Name = "p",
            Bindings =
            {
                new Binding
                {
                    Source = new KeySource(W),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    // Missing DigitalToScalar converter — chain is invalid.
                    Modifiers = Array.Empty<Modifier>()
                }
            }
        };
        var r = new BindingResolver(profile);
        var buckets = new OutputStateBuckets();
        r.Apply(RawEvent.ForKey(W, true, KeyModifiers.None, 0), buckets);
        var stickFinal = new Dictionary<(Stick, AxisComponent), double>();
        r.AdvanceTick(0.01, buckets, stickFinal);
        // Invalid chain produces no output; resolver must not throw.
        stickFinal.Should().NotContainKey((Stick.Left, AxisComponent.X));
    }

    [Fact]
    public void Soft_mute_reset_clears_chain_state()
    {
        var profile = new Profile
        {
            Name = "p",
            Bindings =
            {
                new Binding
                {
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    Modifiers = new Modifier[] { new StickDynamicsModifier(StickDynamicsMode.Persistent, 100.0, 0.0) }
                }
            }
        };
        var r = new BindingResolver(profile);
        var buckets = new OutputStateBuckets();
        var stickFinal = new Dictionary<(Stick, AxisComponent), double>();

        r.Apply(RawEvent.ForMouseMove(50, 0, 0), buckets);
        r.AdvanceTick(0.01, buckets, stickFinal);
        stickFinal[(Stick.Left, AxisComponent.X)].Should().BeApproximately(0.5, 1e-6);

        // Soft-mute resumes from clean state.
        buckets.ResetForIdleReport();
        r.AdvanceTick(0.01, buckets, stickFinal);
        stickFinal[(Stick.Left, AxisComponent.X)].Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void Disabled_modifier_is_passthrough()
    {
        var profile = new Profile
        {
            Name = "p",
            Bindings =
            {
                new Binding
                {
                    Source = new KeySource(W),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    Modifiers = new Modifier[]
                    {
                        DigitalToScalarModifier.Default,
                        new InvertModifier { Enabled = false },
                    }
                }
            }
        };
        var r = new BindingResolver(profile);
        var buckets = new OutputStateBuckets();
        var stickFinal = new Dictionary<(Stick, AxisComponent), double>();

        r.Apply(RawEvent.ForKey(W, true, KeyModifiers.None, 0), buckets);
        r.AdvanceTick(0.01, buckets, stickFinal);
        // Invert is disabled — output should be +1, not -1.
        stickFinal[(Stick.Left, AxisComponent.X)].Should().Be(1.0);
    }
}
