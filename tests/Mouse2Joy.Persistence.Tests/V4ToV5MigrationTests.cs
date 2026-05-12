using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Mouse2Joy.Persistence;
using Mouse2Joy.Persistence.Migration;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Persistence.Tests;

public class V4ToV5MigrationTests
{
    [Fact]
    public void V4_segmented_response_curve_without_shape_loads_with_convex_default()
    {
        // v4 documents authored before Shape existed should deserialize with
        // Shape == Convex (constructor default), preserving the original
        // accelerating-curve behavior across all five styles.
        var v4Json = """
        {
          "schemaVersion": 4,
          "name": "Test",
          "tickRateHz": 250,
          "bindings": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "source": {"$kind": "mouseAxis", "axis": "x"},
              "target": {"$kind": "stickAxis", "stick": "left", "component": "x"},
              "modifiers": [
                {"$kind": "stickDynamics", "mode": "velocity", "param1": 8.0, "param2": 800.0},
                {"$kind": "segmentedResponseCurve", "threshold": 0.3, "exponent": 2.0, "region": "aboveThreshold", "transitionStyle": "hermiteSpline"}
              ],
              "enabled": true,
              "suppressInput": false
            }
          ]
        }
        """;

        var profile = ProfileStore.DeserializeProfile(v4Json);
        profile.Should().NotBeNull();
        profile!.SchemaVersion.Should().Be(Profile.CurrentSchemaVersion);
        var mods = profile.Bindings[0].Modifiers;
        var srk = mods[1].Should().BeOfType<SegmentedResponseCurveModifier>().Subject;
        srk.TransitionStyle.Should().Be(SegmentedCurveTransitionStyle.HermiteSpline);
        srk.Shape.Should().Be(SegmentedCurveShape.Convex,
            "v4 documents without shape must preserve original accelerating behavior");
    }

    [Fact]
    public void V4_profile_loads_with_version_stamped_to_current()
    {
        var v4Json = """
        {
          "schemaVersion": 4,
          "name": "Test",
          "tickRateHz": 250,
          "bindings": []
        }
        """;

        var profile = ProfileStore.DeserializeProfile(v4Json);
        profile.Should().NotBeNull();
        profile!.SchemaVersion.Should().Be(Profile.CurrentSchemaVersion);
    }

    [Fact]
    public void V4ToV5_apply_directly_stamps_version()
    {
        var node = JsonNode.Parse("""{"schemaVersion": 4}""")!;
        V4ToV5.Apply(node);
        node["schemaVersion"]!.GetValue<int>().Should().Be(5);
    }

    [Fact]
    public void V5_profile_with_explicit_shape_and_new_style_round_trips_unchanged()
    {
        // A profile authored at v5 with an explicit Shape AND one of the new
        // styles (QuinticSmooth) persists and reloads without losing either.
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
                        new StickDynamicsModifier(StickDynamicsMode.Velocity, 8.0, 800.0),
                        new SegmentedResponseCurveModifier(
                            0.3, 2.0,
                            SegmentedCurveRegion.AboveThreshold,
                            SegmentedCurveTransitionStyle.QuinticSmooth,
                            SegmentedCurveShape.Concave),
                    }
                }
            }
        };
        var json = JsonSerializer.Serialize(profile, JsonOptions.Default);
        json.Should().Contain("shape", "v5 JSON must contain the new shape field");
        json.Should().Contain("quinticSmooth", "v5 must support the new transitionStyle values");

        var rt = ProfileStore.DeserializeProfile(json);
        rt.Should().NotBeNull();
        rt!.SchemaVersion.Should().Be(Profile.CurrentSchemaVersion);
        var srk = rt.Bindings[0].Modifiers[1].Should().BeOfType<SegmentedResponseCurveModifier>().Subject;
        srk.TransitionStyle.Should().Be(SegmentedCurveTransitionStyle.QuinticSmooth);
        srk.Shape.Should().Be(SegmentedCurveShape.Concave);
    }

    [Fact]
    public void V5_default_factory_picks_quintic_smooth_convex()
    {
        // Confirms the "catalog default is QuinticSmooth + Convex" decision:
        // newly-added modifiers via the catalog get the smooth no-dip math.
        var def = SegmentedResponseCurveModifier.Default;
        def.TransitionStyle.Should().Be(SegmentedCurveTransitionStyle.QuinticSmooth);
        def.Shape.Should().Be(SegmentedCurveShape.Convex);
    }
}
