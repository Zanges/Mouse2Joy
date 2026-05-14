using FluentAssertions;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Persistence.Tests;

public class ChainValidatorTests
{
    private static InputSource MouseAxisSrc() => new MouseAxisSource(MouseAxis.X);
    private static InputSource KeySrc() => new KeySource(new VirtualKey(0x11, false));
    private static OutputTarget StickTgt() => new StickAxisTarget(Stick.Left, AxisComponent.X);
    private static OutputTarget TriggerTgt() => new TriggerTarget(Trigger.Right);
    private static OutputTarget ButtonTgt() => new ButtonTarget(GamepadButton.A);

    [Fact]
    public void Empty_chain_key_to_button_is_valid()
    {
        var r = ChainValidator.Validate(KeySrc(), Array.Empty<Modifier>(), ButtonTgt());
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_chain_mouse_axis_to_stick_is_invalid()
    {
        var r = ChainValidator.Validate(MouseAxisSrc(), Array.Empty<Modifier>(), StickTgt());
        r.IsValid.Should().BeFalse();
        r.ErrorMessage.Should().Contain("Delta").And.Contain("Scalar");
    }

    [Fact]
    public void Empty_chain_key_to_stick_is_invalid()
    {
        var r = ChainValidator.Validate(KeySrc(), Array.Empty<Modifier>(), StickTgt());
        r.IsValid.Should().BeFalse();
        r.ErrorMessage.Should().Contain("Digital").And.Contain("Scalar");
    }

    [Fact]
    public void Mouse_axis_to_stick_with_stick_dynamics_is_valid()
    {
        var r = ChainValidator.Validate(
            MouseAxisSrc(),
            new Modifier[] { StickDynamicsModifier.DefaultVelocity },
            StickTgt());
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Key_to_stick_with_digital_to_scalar_is_valid()
    {
        var r = ChainValidator.Validate(
            KeySrc(),
            new Modifier[] { DigitalToScalarModifier.Default },
            StickTgt());
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Key_to_button_with_digital_to_scalar_is_invalid()
    {
        // Adding the converter pushes us into Scalar but the target wants Digital.
        var r = ChainValidator.Validate(
            KeySrc(),
            new Modifier[] { DigitalToScalarModifier.Default },
            ButtonTgt());
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Sensitivity_on_digital_source_is_invalid()
    {
        var r = ChainValidator.Validate(
            KeySrc(),
            new Modifier[] { OutputScaleModifier.Default },
            StickTgt());
        r.IsValid.Should().BeFalse();
        r.ErrorIndex.Should().Be(0);
    }

    [Fact]
    public void Disabled_modifier_still_validates_types()
    {
        // Disabled DigitalToScalar still counts as a Digital-to-Scalar edge.
        var r = ChainValidator.Validate(
            KeySrc(),
            new Modifier[] { DigitalToScalarModifier.Default with { Enabled = false } },
            StickTgt());
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Mouse_axis_to_button_via_threshold_is_valid()
    {
        var r = ChainValidator.Validate(
            MouseAxisSrc(),
            new Modifier[]
            {
                StickDynamicsModifier.DefaultVelocity,
                ScalarToDigitalThresholdModifier.Default
            },
            ButtonTgt());
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Mouse_axis_to_stick_with_chain_of_shape_modifiers_is_valid()
    {
        var r = ChainValidator.Validate(
            MouseAxisSrc(),
            new Modifier[]
            {
                StickDynamicsModifier.DefaultVelocity,
                new OutputScaleModifier(1.5),
                new InnerDeadzoneModifier(0.1),
                new OuterSaturationModifier(0.05),
                new ResponseCurveModifier(1.5),
                new InvertModifier(),
            },
            StickTgt());
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Digital_chain_with_toggle_and_autofire_is_valid()
    {
        var r = ChainValidator.Validate(
            KeySrc(),
            new Modifier[]
            {
                new ToggleModifier(),
                new HoldToActivateModifier(0.5),
                new AutoFireModifier(10.0),
            },
            ButtonTgt());
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Limiter_in_scalar_chain_is_valid()
    {
        var r = ChainValidator.Validate(
            MouseAxisSrc(),
            new Modifier[]
            {
                StickDynamicsModifier.DefaultVelocity,
                new LimiterModifier(0.5, 0.5),
                new SmoothingModifier(0.05),
            },
            StickTgt());
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Toggle_on_scalar_signal_is_invalid()
    {
        var r = ChainValidator.Validate(
            MouseAxisSrc(),
            new Modifier[]
            {
                StickDynamicsModifier.DefaultVelocity,
                new ToggleModifier(),  // expects Digital, gets Scalar
            },
            StickTgt());
        r.IsValid.Should().BeFalse();
        r.ErrorIndex.Should().Be(1);
    }

    [Fact]
    public void Two_converters_in_a_row_invalidates_chain()
    {
        var r = ChainValidator.Validate(
            KeySrc(),
            new Modifier[]
            {
                DigitalToScalarModifier.Default,
                DigitalToScalarModifier.Default, // Second one expects Digital, gets Scalar.
            },
            StickTgt());
        r.IsValid.Should().BeFalse();
        r.ErrorIndex.Should().Be(1);
    }
}

