using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Mouse2Joy.Persistence.Migration;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Persistence.Tests;

public class V2ToV3MigrationTests
{
    [Fact]
    public void V2_sensitivity_modifier_migrates_to_output_scale()
    {
        var v2Json = """
        {
          "schemaVersion": 2,
          "name": "Test",
          "tickRateHz": 250,
          "bindings": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "source": {"$kind": "mouseAxis", "axis": "x"},
              "target": {"$kind": "stickAxis", "stick": "left", "component": "x"},
              "modifiers": [
                {"$kind": "stickDynamics", "mode": "velocity", "param1": 8.0, "param2": 800.0},
                {"$kind": "sensitivity", "multiplier": 0.63}
              ],
              "enabled": true,
              "suppressInput": false
            }
          ]
        }
        """;

        var profile = ProfileStore.DeserializeProfile(v2Json);
        profile.Should().NotBeNull();
        profile!.SchemaVersion.Should().Be(Profile.CurrentSchemaVersion);
        profile.Bindings.Should().HaveCount(1);
        var mods = profile.Bindings[0].Modifiers;
        mods.Should().HaveCount(2);
        mods[0].Should().BeOfType<StickDynamicsModifier>();
        var os = mods[1].Should().BeOfType<OutputScaleModifier>().Subject;
        os.Factor.Should().Be(0.63);
    }

    [Fact]
    public void V2_profile_without_sensitivity_still_loads()
    {
        // Pure no-op migration path: V2→V3 should not break profiles that
        // don't contain a sensitivity modifier.
        var v2Json = """
        {
          "schemaVersion": 2,
          "name": "Test",
          "tickRateHz": 250,
          "bindings": [
            {
              "id": "22222222-2222-2222-2222-222222222222",
              "source": {"$kind": "key", "key": {"keyCode": 17, "isExtended": false}},
              "target": {"$kind": "trigger", "trigger": "right"},
              "modifiers": [
                {"$kind": "digitalToScalar", "onValue": 1.0, "offValue": 0.0}
              ],
              "enabled": true,
              "suppressInput": false
            }
          ]
        }
        """;

        var profile = ProfileStore.DeserializeProfile(v2Json);
        profile.Should().NotBeNull();
        profile!.SchemaVersion.Should().Be(Profile.CurrentSchemaVersion);
        profile.Bindings[0].Modifiers.Should().HaveCount(1);
        profile.Bindings[0].Modifiers[0].Should().BeOfType<DigitalToScalarModifier>();
    }

    [Fact]
    public void Current_version_profile_skips_v2_to_v3_migration()
    {
        // A profile already at the current version should round-trip without
        // V2ToV3 touching it (no sensitivity → outputScale rename needed).
        var current = new Profile
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
                        new StickDynamicsModifier(StickDynamicsMode.Velocity, 8.0, 800.0),
                        new OutputScaleModifier(0.5),
                    }
                }
            }
        };
        var json = JsonSerializer.Serialize(current, JsonOptions.Default);
        var rt = ProfileStore.DeserializeProfile(json);
        rt.Should().NotBeNull();
        rt!.SchemaVersion.Should().Be(Profile.CurrentSchemaVersion);
        rt.Bindings[0].Modifiers[1].Should().BeOfType<OutputScaleModifier>()
            .Which.Factor.Should().Be(0.5);
    }

    [Fact]
    public void V1_profile_chains_through_full_pipeline_to_current_version()
    {
        // End-to-end coverage of the chained migration: v1 → V1ToV2 → later steps → Profile.
        // The v1 doc has Sensitivity = 0.5 in the curve. V1ToV2 emits the
        // current OutputScaleModifier directly (since it always stamps the
        // current schema version), so the V2ToV3 step is effectively a no-op
        // for v1-sourced profiles — but the pipeline still runs cleanly and
        // produces the expected current-version output.
        var v1Json = """
        {
          "schemaVersion": 1,
          "name": "Test",
          "tickRateHz": 250,
          "bindings": [
            {
              "id": "33333333-3333-3333-3333-333333333333",
              "source": {"$kind": "mouseAxis", "axis": "x"},
              "target": {"$kind": "stickAxis", "stick": "left", "component": "x"},
              "curve": {"sensitivity": 0.5, "innerDeadzone": 0.0, "outerSaturation": 0.0, "exponent": 1.0},
              "stickModel": {"$kind": "velocity", "decayPerSecond": 8.0, "maxVelocityCounts": 800.0},
              "enabled": true,
              "suppressInput": false
            }
          ]
        }
        """;

        var profile = ProfileStore.DeserializeProfile(v1Json);
        profile.Should().NotBeNull();
        profile!.SchemaVersion.Should().Be(Profile.CurrentSchemaVersion);
        var mods = profile.Bindings[0].Modifiers;
        mods.Should().HaveCount(2);
        mods[0].Should().BeOfType<StickDynamicsModifier>();
        // V1ToV2 emitted SensitivityModifier(0.5); V2ToV3 renamed it to OutputScale(0.5).
        mods[1].Should().BeOfType<OutputScaleModifier>().Which.Factor.Should().Be(0.5);
    }

    [Fact]
    public void V2ToV3_apply_directly_rewrites_kind_and_property()
    {
        // Direct test of the V2ToV3 transform without going through the full
        // ProfileStore pipeline.
        var node = JsonNode.Parse("""
        {
          "schemaVersion": 2,
          "bindings": [
            {
              "modifiers": [
                {"$kind": "sensitivity", "multiplier": 0.7, "enabled": true}
              ]
            }
          ]
        }
        """)!;

        V2ToV3.Apply(node);

        node["schemaVersion"]!.GetValue<int>().Should().Be(3);
        var mod = node["bindings"]![0]!["modifiers"]![0]!;
        mod["$kind"]!.GetValue<string>().Should().Be("outputScale");
        mod["factor"]!.GetValue<double>().Should().Be(0.7);
        mod["multiplier"].Should().BeNull("the old property must be removed");
        // Unrelated properties pass through.
        mod["enabled"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void V2ToV3_apply_is_idempotent_no_op_on_empty_profile()
    {
        var node = JsonNode.Parse("""{"schemaVersion": 2}""")!;
        V2ToV3.Apply(node);
        // No bindings = nothing to rewrite, but version still stamps to 3.
        node["schemaVersion"]!.GetValue<int>().Should().Be(3);
    }
}
