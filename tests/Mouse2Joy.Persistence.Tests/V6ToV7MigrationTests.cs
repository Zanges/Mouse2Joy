using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Mouse2Joy.Persistence.Migration;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Persistence.Tests;

public class V6ToV7MigrationTests
{
    [Fact]
    public void V6_profile_loads_with_version_stamped_to_current()
    {
        var v6Json = """
        {
          "schemaVersion": 6,
          "name": "Test",
          "tickRateHz": 250,
          "bindings": []
        }
        """;

        var profile = ProfileStore.DeserializeProfile(v6Json);
        profile.Should().NotBeNull();
        profile!.SchemaVersion.Should().Be(Profile.CurrentSchemaVersion);
    }

    [Fact]
    public void V6ToV7_apply_directly_stamps_version()
    {
        var node = JsonNode.Parse("""{"schemaVersion": 6}""")!;
        V6ToV7.Apply(node);
        node["schemaVersion"]!.GetValue<int>().Should().Be(7);
    }

    [Fact]
    public void V7_profile_with_curve_editor_round_trips()
    {
        // A profile authored at v7 with a curveEditor modifier persists and
        // reloads with the points + symmetric flag intact.
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
                        new CurveEditorModifier
                        {
                            Symmetric = false,
                            Points = new[]
                            {
                                new CurvePoint(-1.0, -1.0),
                                new CurvePoint(0.0, 0.0),
                                new CurvePoint(0.6, 0.3),
                                new CurvePoint(1.0, 1.0),
                            }
                        }
                    }
                }
            }
        };
        var json = JsonSerializer.Serialize(profile, JsonOptions.Default);
        json.Should().Contain("curveEditor", "v7 JSON must include the new modifier kind");

        var rt = ProfileStore.DeserializeProfile(json);
        rt.Should().NotBeNull();
        rt!.SchemaVersion.Should().Be(Profile.CurrentSchemaVersion);
        var ce = rt.Bindings[0].Modifiers[1].Should().BeOfType<CurveEditorModifier>().Subject;
        ce.Symmetric.Should().BeFalse();
        ce.Points.Should().HaveCount(4);
        ce.Points[2].X.Should().Be(0.6);
        ce.Points[2].Y.Should().Be(0.3);
    }

    [Fact]
    public void V7_default_factory_produces_3_point_identity_symmetric()
    {
        var def = CurveEditorModifier.Default;
        def.Symmetric.Should().BeTrue();
        def.Points.Should().HaveCount(3);
        def.Points[0].X.Should().Be(0.0);
        def.Points[2].X.Should().Be(1.0);
    }
}
