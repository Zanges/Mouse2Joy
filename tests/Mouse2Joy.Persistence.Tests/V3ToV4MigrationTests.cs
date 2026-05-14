using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Mouse2Joy.Persistence.Migration;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Persistence.Tests;

public class V3ToV4MigrationTests
{
    [Fact]
    public void V3_segmented_response_curve_without_transition_style_loads_with_hard_default()
    {
        // v3 documents authored before TransitionStyle existed should
        // deserialize with TransitionStyle == Hard (constructor default),
        // preserving the original kinked-curve behavior exactly.
        var v3Json = """
        {
          "schemaVersion": 3,
          "name": "Test",
          "tickRateHz": 250,
          "bindings": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "source": {"$kind": "mouseAxis", "axis": "x"},
              "target": {"$kind": "stickAxis", "stick": "left", "component": "x"},
              "modifiers": [
                {"$kind": "stickDynamics", "mode": "velocity", "param1": 8.0, "param2": 800.0},
                {"$kind": "segmentedResponseCurve", "threshold": 0.3, "exponent": 2.0, "region": "aboveThreshold"}
              ],
              "enabled": true,
              "suppressInput": false
            }
          ]
        }
        """;

        var profile = ProfileStore.DeserializeProfile(v3Json);
        profile.Should().NotBeNull();
        profile!.SchemaVersion.Should().Be(Profile.CurrentSchemaVersion);
        var mods = profile.Bindings[0].Modifiers;
        var srk = mods[1].Should().BeOfType<SegmentedResponseCurveModifier>().Subject;
        srk.Threshold.Should().Be(0.3);
        srk.Exponent.Should().Be(2.0);
        srk.Region.Should().Be(SegmentedCurveRegion.AboveThreshold);
        srk.TransitionStyle.Should().Be(SegmentedCurveTransitionStyle.Hard,
            "v3 documents without the field must preserve original kinked behavior");
    }

    [Fact]
    public void V3_profile_loads_with_version_stamped_to_current()
    {
        var v3Json = """
        {
          "schemaVersion": 3,
          "name": "Test",
          "tickRateHz": 250,
          "bindings": []
        }
        """;

        var profile = ProfileStore.DeserializeProfile(v3Json);
        profile.Should().NotBeNull();
        profile!.SchemaVersion.Should().Be(Profile.CurrentSchemaVersion);
    }

    [Fact]
    public void V3ToV4_apply_directly_stamps_version()
    {
        var node = JsonNode.Parse("""{"schemaVersion": 3}""")!;
        V3ToV4.Apply(node);
        node["schemaVersion"]!.GetValue<int>().Should().Be(4);
    }

    [Fact]
    public void V4_profile_with_explicit_transition_style_round_trips_unchanged()
    {
        // A profile authored at v4 with an explicit TransitionStyle persists
        // and reloads without losing the field.
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
                            SegmentedCurveTransitionStyle.HermiteSpline),
                    }
                }
            }
        };
        var json = JsonSerializer.Serialize(profile, JsonOptions.Default);
        json.Should().Contain("transitionStyle", "v4 JSON must contain the new field");

        var rt = ProfileStore.DeserializeProfile(json);
        rt.Should().NotBeNull();
        rt!.SchemaVersion.Should().Be(Profile.CurrentSchemaVersion);
        var srk = rt.Bindings[0].Modifiers[1].Should().BeOfType<SegmentedResponseCurveModifier>().Subject;
        srk.TransitionStyle.Should().Be(SegmentedCurveTransitionStyle.HermiteSpline);
    }

    [Fact]
    public void Default_factory_uses_a_smooth_style_for_new_instances()
    {
        // Confirms the "catalog default is smooth, JSON default is Hard"
        // split: the .Default factory used by the modifier catalog produces
        // a smooth-style instance, even though the constructor's parameter
        // default for TransitionStyle is Hard for backward compatibility on
        // load. The specific smooth style chosen for the default may evolve
        // (was HermiteSpline in v4, became QuinticSmooth in v5); this test
        // just asserts it's not Hard.
        SegmentedResponseCurveModifier.Default.TransitionStyle
            .Should().NotBe(SegmentedCurveTransitionStyle.Hard);
    }
}
