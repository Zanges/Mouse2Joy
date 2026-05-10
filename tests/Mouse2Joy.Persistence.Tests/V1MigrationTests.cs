using System.Text.Json;
using FluentAssertions;
using Mouse2Joy.Persistence;
using Mouse2Joy.Persistence.Legacy.V1;
using Mouse2Joy.Persistence.Migration;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Persistence.Tests;

public class V1MigrationTests
{
    private static Profile MigrateAndGet(LegacyProfile v1) => V1ToV2.Migrate(v1);

    [Fact]
    public void Mouse_axis_to_stick_with_velocity_emits_stick_dynamics_velocity()
    {
        var v1 = new LegacyProfile
        {
            Name = "p",
            Bindings =
            {
                new LegacyBinding
                {
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    StickModel = new LegacyVelocityStickModel(8.0, 800.0)
                }
            }
        };
        var v2 = MigrateAndGet(v1);
        v2.SchemaVersion.Should().Be(2);
        v2.Bindings.Should().HaveCount(1);
        v2.Bindings[0].Modifiers.Should().HaveCount(1);
        var sd = v2.Bindings[0].Modifiers[0].Should().BeOfType<StickDynamicsModifier>().Subject;
        sd.Mode.Should().Be(StickDynamicsMode.Velocity);
        sd.Param1.Should().Be(8.0);
        sd.Param2.Should().Be(800.0);
    }

    [Fact]
    public void Mouse_axis_to_stick_with_null_stick_model_emits_explicit_default()
    {
        var v1 = new LegacyProfile
        {
            Name = "p",
            Bindings =
            {
                new LegacyBinding
                {
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    StickModel = null,
                }
            }
        };
        var v2 = MigrateAndGet(v1);
        var sd = v2.Bindings[0].Modifiers[0].Should().BeOfType<StickDynamicsModifier>().Subject;
        sd.Mode.Should().Be(StickDynamicsMode.Velocity);
        sd.Param1.Should().Be(8.0);
        sd.Param2.Should().Be(800.0);
    }

    [Fact]
    public void Accumulator_with_non_default_curve_emits_full_chain()
    {
        var v1 = new LegacyProfile
        {
            Name = "p",
            Bindings =
            {
                new LegacyBinding
                {
                    Source = new MouseAxisSource(MouseAxis.Y),
                    Target = new StickAxisTarget(Stick.Right, AxisComponent.Y),
                    StickModel = new LegacyAccumulatorStickModel(5.0, 200.0),
                    Curve = new LegacyCurve(Sensitivity: 1.5, InnerDeadzone: 0.1, OuterSaturation: 0.05, Exponent: 1.5)
                }
            }
        };
        var v2 = MigrateAndGet(v1);
        var mods = v2.Bindings[0].Modifiers;
        mods.Should().HaveCount(5);
        mods[0].Should().BeOfType<StickDynamicsModifier>()
            .Which.Mode.Should().Be(StickDynamicsMode.Accumulator);
        mods[1].Should().BeOfType<SensitivityModifier>().Which.Multiplier.Should().Be(1.5);
        mods[2].Should().BeOfType<InnerDeadzoneModifier>().Which.Threshold.Should().Be(0.1);
        mods[3].Should().BeOfType<OuterSaturationModifier>().Which.Threshold.Should().Be(0.05);
        mods[4].Should().BeOfType<ResponseCurveModifier>().Which.Exponent.Should().Be(1.5);
    }

    [Fact]
    public void Key_to_button_emits_no_modifiers()
    {
        var v1 = new LegacyProfile
        {
            Name = "p",
            Bindings =
            {
                new LegacyBinding
                {
                    Source = new KeySource(new VirtualKey(0x11, false)),
                    Target = new ButtonTarget(GamepadButton.A),
                    Curve = new LegacyCurve(0.5, 0.1, 0.1, 2.0) // ignored for digital target
                }
            }
        };
        var v2 = MigrateAndGet(v1);
        v2.Bindings[0].Modifiers.Should().BeEmpty();
    }

    [Fact]
    public void Key_to_trigger_emits_digital_to_scalar()
    {
        var v1 = new LegacyProfile
        {
            Name = "p",
            Bindings =
            {
                new LegacyBinding
                {
                    Source = new KeySource(new VirtualKey(0x11, false)),
                    Target = new TriggerTarget(Trigger.Right)
                }
            }
        };
        var v2 = MigrateAndGet(v1);
        v2.Bindings[0].Modifiers.Should().HaveCount(1);
        v2.Bindings[0].Modifiers[0].Should().BeOfType<DigitalToScalarModifier>()
            .Which.OnValue.Should().Be(1.0);
    }

    [Fact]
    public void Default_curve_fields_are_dropped()
    {
        var v1 = new LegacyProfile
        {
            Name = "p",
            Bindings =
            {
                new LegacyBinding
                {
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    StickModel = new LegacyVelocityStickModel(8.0, 800.0),
                    Curve = LegacyCurve.Default
                }
            }
        };
        var v2 = MigrateAndGet(v1);
        // Only the StickDynamics; no Sensitivity/Deadzone/Saturation/ResponseCurve.
        v2.Bindings[0].Modifiers.Should().HaveCount(1);
        v2.Bindings[0].Modifiers[0].Should().BeOfType<StickDynamicsModifier>();
    }

    [Fact]
    public void End_to_end_v1_json_roundtrips_through_profile_store()
    {
        // Hand-write a v1 JSON document (schemaVersion = 1) and confirm
        // ProfileStore.DeserializeProfile produces a v2 Profile with the
        // expected modifier chain.
        var v1Json = """
        {
          "schemaVersion": 1,
          "name": "Test",
          "tickRateHz": 250,
          "bindings": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "source": {"$kind": "mouseAxis", "axis": "x"},
              "target": {"$kind": "stickAxis", "stick": "left", "component": "x"},
              "curve": {"sensitivity": 1.0, "innerDeadzone": 0.1, "outerSaturation": 0.0, "exponent": 1.0},
              "stickModel": {"$kind": "velocity", "decayPerSecond": 8.0, "maxVelocityCounts": 800.0},
              "enabled": true,
              "suppressInput": true
            }
          ]
        }
        """;

        var profile = ProfileStore.DeserializeProfile(v1Json);
        profile.Should().NotBeNull();
        profile!.SchemaVersion.Should().Be(2);
        profile.Bindings.Should().HaveCount(1);
        var mods = profile.Bindings[0].Modifiers;
        mods.Should().HaveCount(2);
        mods[0].Should().BeOfType<StickDynamicsModifier>();
        mods[1].Should().BeOfType<InnerDeadzoneModifier>().Which.Threshold.Should().Be(0.1);
    }

    [Fact]
    public void V2_json_skips_migration()
    {
        var v2 = new Profile
        {
            Name = "Test",
            Bindings = new List<Binding>
            {
                new()
                {
                    Source = new KeySource(new VirtualKey(0x11, false)),
                    Target = new TriggerTarget(Trigger.Right),
                    Modifiers = new Modifier[] { DigitalToScalarModifier.Default }
                }
            }
        };
        var json = JsonSerializer.Serialize(v2, JsonOptions.Default);
        var rt = ProfileStore.DeserializeProfile(json);
        rt.Should().NotBeNull();
        rt!.SchemaVersion.Should().Be(2);
        rt.Bindings[0].Modifiers.Should().HaveCount(1);
        rt.Bindings[0].Modifiers[0].Should().BeOfType<DigitalToScalarModifier>();
    }
}
