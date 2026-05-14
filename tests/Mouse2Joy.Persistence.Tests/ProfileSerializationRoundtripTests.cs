using System.Text.Json;
using FluentAssertions;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Persistence.Tests;

public class ProfileSerializationRoundtripTests
{
    [Fact]
    public void Profile_with_every_source_and_target_kind_roundtrips()
    {
        var profile = new Profile
        {
            Name = "Test",
            TickRateHz = 240,
            Bindings = new List<Binding>
            {
                new() {
                    Source = new MouseAxisSource(MouseAxis.X),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
                    Modifiers = new Modifier[] { new StickDynamicsModifier(StickDynamicsMode.Velocity, 8.0, 800.0) }
                },
                new() {
                    Source = new MouseAxisSource(MouseAxis.Y),
                    Target = new StickAxisTarget(Stick.Left, AxisComponent.Y),
                    Modifiers = new Modifier[] { new StickDynamicsModifier(StickDynamicsMode.Accumulator, 4.0, 400.0) }
                },
                new() {
                    Source = new MouseButtonSource(MouseButton.Left),
                    Target = new TriggerTarget(Trigger.Right),
                    Modifiers = new Modifier[] { DigitalToScalarModifier.Default }
                },
                new() {
                    Source = new MouseScrollSource(ScrollDirection.Up),
                    Target = new ButtonTarget(GamepadButton.A)
                },
                new() {
                    Source = new KeySource(new VirtualKey(0x11, false)),
                    Target = new DPadTarget(DPadDirection.Up)
                }
            }
        };

        var json = JsonSerializer.Serialize(profile, JsonOptions.Default);
        json.Should().Contain("$kind");
        var roundtripped = JsonSerializer.Deserialize<Profile>(json, JsonOptions.Default);
        roundtripped.Should().NotBeNull();
        roundtripped!.Name.Should().Be("Test");
        roundtripped.TickRateHz.Should().Be(240);
        roundtripped.Bindings.Should().HaveCount(5);

        roundtripped.Bindings[0].Source.Should().BeOfType<MouseAxisSource>();
        roundtripped.Bindings[0].Modifiers[0].Should().BeOfType<StickDynamicsModifier>()
            .Which.Mode.Should().Be(StickDynamicsMode.Velocity);
        roundtripped.Bindings[1].Modifiers[0].Should().BeOfType<StickDynamicsModifier>()
            .Which.Mode.Should().Be(StickDynamicsMode.Accumulator);
        roundtripped.Bindings[2].Target.Should().BeOfType<TriggerTarget>();
        roundtripped.Bindings[2].Modifiers[0].Should().BeOfType<DigitalToScalarModifier>();
        roundtripped.Bindings[3].Source.Should().BeOfType<MouseScrollSource>();
        roundtripped.Bindings[4].Source.Should().BeOfType<KeySource>();
    }

    [Fact]
    public void AppSettings_roundtrips()
    {
        var settings = new AppSettings
        {
            LastProfileName = "Default",
            SoftToggleHotkey = new HotkeyBinding(new VirtualKey(0x42, false), KeyModifiers.Ctrl | KeyModifiers.Alt),
            HardToggleHotkey = new HotkeyBinding(new VirtualKey(0x58, false), KeyModifiers.None),
            ProfileSwitchHotkeys = new Dictionary<string, HotkeyBinding>
            {
                ["FPS"] = new(new VirtualKey(0x3B, false), KeyModifiers.None)
            },
            Overlay = new OverlayLayout
            {
                Enabled = true,
                Widgets = new List<WidgetConfig>
                {
                    new() { Id = "root1", Type = "TwoAxis", X = 100, Y = 200, Width = 96, Height = 96, LockAspect = true, MonitorIndex = 1 },
                    new() { Id = "child1", Type = "Status", X = 8, Y = 0, Width = 240, Height = 32, LockAspect = false, Visible = false, ParentId = "root1" }
                }
            }
        };

        var json = JsonSerializer.Serialize(settings, JsonOptions.Default);
        var rt = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions.Default);
        rt.Should().NotBeNull();
        rt!.SoftToggleHotkey!.Modifiers.Should().HaveFlag(KeyModifiers.Ctrl);
        rt.ProfileSwitchHotkeys.Should().ContainKey("FPS");
        rt.Overlay.Widgets.Should().HaveCount(2);
        rt.Overlay.Widgets[0].Id.Should().Be("root1");
        rt.Overlay.Widgets[0].MonitorIndex.Should().Be(1);
        rt.Overlay.Widgets[0].Width.Should().Be(96);
        rt.Overlay.Widgets[0].Height.Should().Be(96);
        rt.Overlay.Widgets[0].LockAspect.Should().BeTrue();
        rt.Overlay.Widgets[1].Visible.Should().BeFalse();
        rt.Overlay.Widgets[1].ParentId.Should().Be("root1");
        rt.Overlay.Widgets[1].LockAspect.Should().BeFalse();
    }

    [Fact]
    public void Schema_version_persisted()
    {
        var profile = new Profile { Name = "x" };
        var json = JsonSerializer.Serialize(profile, JsonOptions.Default);
        json.Should().Contain($"\"schemaVersion\": {Profile.CurrentSchemaVersion}");
    }
}
