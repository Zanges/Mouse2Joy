using System.Text.Json;
using FluentAssertions;
using Mouse2Joy.Persistence;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Persistence.Tests;

public class ModifierSerializationTests
{
    private static T Roundtrip<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions.Default);
        return JsonSerializer.Deserialize<T>(json, JsonOptions.Default)!;
    }

    [Fact]
    public void Every_modifier_kind_roundtrips_with_kind_discriminator()
    {
        Modifier[] modifiers =
        {
            new StickDynamicsModifier(StickDynamicsMode.Velocity, 8.0, 800.0),
            new StickDynamicsModifier(StickDynamicsMode.Accumulator, 5.0, 400.0),
            new StickDynamicsModifier(StickDynamicsMode.Persistent, 400.0, 0.0),
            new DigitalToScalarModifier(1.0, 0.0),
            new ScalarToDigitalThresholdModifier(0.5),
            new OutputScaleModifier(1.5),
            new InnerDeadzoneModifier(0.1),
            new OuterSaturationModifier(0.05),
            new ResponseCurveModifier(1.5),
            new InvertModifier(),
            new RampUpModifier(0.5),
            new RampDownModifier(0.25),
            new LimiterModifier(0.33, 0.66),
            new ToggleModifier(),
            new SmoothingModifier(0.05),
            new AutoFireModifier(12.5),
            new HoldToActivateModifier(0.75),
            new TapModifier(0.3, 0.05),
            new TapModifier(0.3, 0.05, WaitForHigherTaps: true, ConfirmWaitSeconds: 0.5),
            new MultiTapModifier(2, 0.4, 0.3, 0.05),
            new MultiTapModifier(2, 0.4, 0.3, 0.05, WaitForHigherTaps: true),
            new WaitForTapResolutionModifier(0.3, 0.4, 0.05),
            new ParametricCurveModifier
            {
                Symmetric = true,
                Points = new[]
                {
                    new CurvePoint(0.0, 0.0),
                    new CurvePoint(0.5, 0.3),
                    new CurvePoint(1.0, 1.0),
                }
            },
            new CurveEditorModifier
            {
                Symmetric = false,
                Points = new[]
                {
                    new CurvePoint(-1.0, -1.0),
                    new CurvePoint(0.0, 0.0),
                    new CurvePoint(0.4, 0.6),
                    new CurvePoint(1.0, 1.0),
                }
            },
        };

        foreach (var m in modifiers)
        {
            var json = JsonSerializer.Serialize(m, JsonOptions.Default);
            json.Should().Contain("$kind", because: $"polymorphism on {m.GetType().Name} requires the discriminator");
            var rt = JsonSerializer.Deserialize<Modifier>(json, JsonOptions.Default);
            rt.Should().NotBeNull();
            rt!.Should().BeOfType(m.GetType());
            rt.Should().Be(m);
        }
    }

    [Fact]
    public void Modifier_chain_in_binding_roundtrips_in_order()
    {
        var binding = new Binding
        {
            Source = new MouseAxisSource(MouseAxis.X),
            Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
            Modifiers = new Modifier[]
            {
                new StickDynamicsModifier(StickDynamicsMode.Velocity, 8.0, 800.0),
                new OutputScaleModifier(1.5),
                new InnerDeadzoneModifier(0.1),
                new ResponseCurveModifier(1.2),
            }
        };

        var rt = Roundtrip(binding);

        rt.Modifiers.Should().HaveCount(4);
        rt.Modifiers[0].Should().BeOfType<StickDynamicsModifier>();
        rt.Modifiers[1].Should().BeOfType<OutputScaleModifier>();
        rt.Modifiers[2].Should().BeOfType<InnerDeadzoneModifier>();
        rt.Modifiers[3].Should().BeOfType<ResponseCurveModifier>();
    }

    [Fact]
    public void Empty_modifier_list_roundtrips()
    {
        var binding = new Binding
        {
            Source = new KeySource(new VirtualKey(0x11, false)),
            Target = new ButtonTarget(GamepadButton.A),
            Modifiers = Array.Empty<Modifier>()
        };

        var rt = Roundtrip(binding);
        rt.Modifiers.Should().BeEmpty();
    }

    [Fact]
    public void Disabled_modifier_roundtrips_with_enabled_false()
    {
        var modifier = new OutputScaleModifier(1.5) { Enabled = false };
        var rt = Roundtrip<Modifier>(modifier);
        rt.Should().BeOfType<OutputScaleModifier>();
        rt.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Modifier_default_enabled_is_true()
    {
        var m = new OutputScaleModifier(1.0);
        m.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Profile_with_modifier_chain_roundtrips_in_full()
    {
        var profile = new Profile
        {
            Name = "Test",
            Bindings = new List<Binding>
            {
                new()
                {
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    Modifiers = new Modifier[]
                    {
                        StickDynamicsModifier.DefaultVelocity,
                        new InvertModifier(),
                        new OutputScaleModifier(1.5),
                    }
                },
                new()
                {
                    Source = new KeySource(new VirtualKey(0x11, false)),
                    Target = new TriggerTarget(Trigger.Right),
                    Modifiers = new Modifier[]
                    {
                        DigitalToScalarModifier.Default,
                        new RampUpModifier(0.5),
                    }
                }
            }
        };

        var rt = Roundtrip(profile);
        rt.Bindings.Should().HaveCount(2);
        rt.Bindings[0].Modifiers.Should().HaveCount(3);
        rt.Bindings[0].Modifiers[1].Should().BeOfType<InvertModifier>();
        rt.Bindings[1].Modifiers.Should().HaveCount(2);
        rt.Bindings[1].Modifiers[1].Should().BeOfType<RampUpModifier>()
            .Which.SecondsToFull.Should().Be(0.5);
    }
}
