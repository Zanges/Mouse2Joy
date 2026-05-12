using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Mouse2Joy.Persistence;
using Mouse2Joy.Persistence.Migration;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Persistence.Tests;

public class V5ToV6MigrationTests
{
    [Fact]
    public void V5_profile_loads_with_version_stamped_to_current()
    {
        var v5Json = """
        {
          "schemaVersion": 5,
          "name": "Test",
          "tickRateHz": 250,
          "bindings": []
        }
        """;

        var profile = ProfileStore.DeserializeProfile(v5Json);
        profile.Should().NotBeNull();
        profile!.SchemaVersion.Should().Be(Profile.CurrentSchemaVersion);
    }

    [Fact]
    public void V5ToV6_apply_directly_stamps_version()
    {
        var node = JsonNode.Parse("""{"schemaVersion": 5}""")!;
        V5ToV6.Apply(node);
        node["schemaVersion"]!.GetValue<int>().Should().Be(6);
    }

    [Fact]
    public void V6_profile_with_parametric_curve_round_trips()
    {
        // A profile authored at v6 with a parametricCurve modifier persists
        // and reloads with the points + symmetric flag intact.
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
                        new ParametricCurveModifier
                        {
                            Symmetric = false,
                            Points = new[]
                            {
                                new CurvePoint(-1.0, -1.0),
                                new CurvePoint(0.0, 0.0),
                                new CurvePoint(0.4, 0.2),
                                new CurvePoint(1.0, 1.0),
                            }
                        }
                    }
                }
            }
        };
        var json = JsonSerializer.Serialize(profile, JsonOptions.Default);
        json.Should().Contain("parametricCurve", "v6 JSON must include the new modifier kind");
        json.Should().Contain("points", "the points list must serialize");

        var rt = ProfileStore.DeserializeProfile(json);
        rt.Should().NotBeNull();
        rt!.SchemaVersion.Should().Be(Profile.CurrentSchemaVersion);
        var pc = rt.Bindings[0].Modifiers[1].Should().BeOfType<ParametricCurveModifier>().Subject;
        pc.Symmetric.Should().BeFalse();
        pc.Points.Should().HaveCount(4);
        pc.Points[2].X.Should().Be(0.4);
        pc.Points[2].Y.Should().Be(0.2);
    }

    [Fact]
    public void V6_default_factory_produces_3_point_identity_symmetric()
    {
        var def = ParametricCurveModifier.Default;
        def.Symmetric.Should().BeTrue();
        def.Points.Should().HaveCount(3);
        def.Points[0].X.Should().Be(0.0);
        def.Points[0].Y.Should().Be(0.0);
        def.Points[2].X.Should().Be(1.0);
        def.Points[2].Y.Should().Be(1.0);
    }
}
