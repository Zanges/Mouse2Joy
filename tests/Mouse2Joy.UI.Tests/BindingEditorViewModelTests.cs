using FluentAssertions;
using Mouse2Joy.Persistence.Models;
using Mouse2Joy.UI.ViewModels.Editor;

namespace Mouse2Joy.UI.Tests;

public class BindingEditorViewModelTests
{
    [Fact]
    public void New_binding_with_default_source_target_auto_inserts_stick_dynamics()
    {
        var vm = new BindingEditorViewModel();
        vm.Modifiers.Should().HaveCount(1);
        vm.Modifiers[0].Modifier.Should().BeOfType<StickDynamicsModifier>();
        vm.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Switching_target_to_button_invalidates_chain_with_stick_dynamics()
    {
        var vm = new BindingEditorViewModel();
        // Default chain has StickDynamics expecting Delta input. Switching
        // target to button doesn't change that, but the output is Scalar,
        // which doesn't match Digital target.
        vm.Target = new ButtonTarget(GamepadButton.A);
        vm.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Switching_source_to_key_with_stick_target_auto_inserts_digital_to_scalar()
    {
        var vm = new BindingEditorViewModel(new Binding
        {
            Source = new KeySource(new VirtualKey(0x11, false)),
            Target = new ButtonTarget(GamepadButton.A),
        });
        vm.Modifiers.Should().BeEmpty();
        vm.IsValid.Should().BeTrue();

        // Switch target to a stick axis — should auto-insert DigitalToScalar.
        vm.Target = new StickAxisTarget(Stick.Left, AxisComponent.X);
        vm.Modifiers.Should().Contain(c => c.Modifier is DigitalToScalarModifier);
        vm.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Removing_a_modifier_remembers_user_intent_and_does_not_re_add_it()
    {
        var vm = new BindingEditorViewModel(); // default: mouse-axis → stick-axis with StickDynamics
        vm.Modifiers.Should().HaveCount(1);

        vm.RemoveAt(0);
        vm.Modifiers.Should().BeEmpty();
        vm.IsValid.Should().BeFalse();

        // Forcing a source-type change should NOT re-insert the same kind the user removed.
        vm.Source = new MouseAxisSource(MouseAxis.Y); // same source type, different axis
        vm.Modifiers.Should().BeEmpty(); // not re-inserted
    }

    [Fact]
    public void Move_up_reorders_correctly()
    {
        var vm = new BindingEditorViewModel(new Binding
        {
            Source = new MouseAxisSource(MouseAxis.X),
            Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
            Modifiers = new Modifier[]
            {
                StickDynamicsModifier.DefaultVelocity,
                new OutputScaleModifier(1.5),
                new InvertModifier(),
            }
        });
        vm.MoveUp(2);
        vm.Modifiers[1].Modifier.Should().BeOfType<InvertModifier>();
        vm.Modifiers[2].Modifier.Should().BeOfType<OutputScaleModifier>();
    }

    [Fact]
    public void Move_down_reorders_correctly()
    {
        var vm = new BindingEditorViewModel(new Binding
        {
            Source = new MouseAxisSource(MouseAxis.X),
            Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
            Modifiers = new Modifier[]
            {
                StickDynamicsModifier.DefaultVelocity,
                new InvertModifier(),
                new OutputScaleModifier(1.5),
            }
        });
        vm.MoveDown(1);
        vm.Modifiers[1].Modifier.Should().BeOfType<OutputScaleModifier>();
        vm.Modifiers[2].Modifier.Should().BeOfType<InvertModifier>();
    }

    [Fact]
    public void Disabling_a_modifier_keeps_chain_valid()
    {
        var vm = new BindingEditorViewModel();
        vm.Modifiers[0].Enabled = false;
        // Disabled converter still type-checks → chain remains valid.
        vm.IsValid.Should().BeTrue();
        vm.Modifiers[0].Enabled.Should().BeFalse();
    }

    [Fact]
    public void Build_result_preserves_modifier_order_and_id()
    {
        var id = Guid.NewGuid();
        var vm = new BindingEditorViewModel(new Binding
        {
            Id = id,
            Source = new MouseAxisSource(MouseAxis.X),
            Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
            Modifiers = new Modifier[]
            {
                StickDynamicsModifier.DefaultVelocity,
                new OutputScaleModifier(1.5),
            },
            Label = "Steering"
        });
        var result = vm.BuildResult();
        result.Id.Should().Be(id);
        result.Label.Should().Be("Steering");
        result.Modifiers.Should().HaveCount(2);
        result.Modifiers[1].Should().BeOfType<OutputScaleModifier>().Which.Factor.Should().Be(1.5);
    }

    [Fact]
    public void Editing_modifier_param_via_proxy_updates_the_card()
    {
        var vm = new BindingEditorViewModel(new Binding
        {
            Source = new MouseAxisSource(MouseAxis.X),
            Target = new StickAxisTarget(Stick.Left, AxisComponent.X),
            Modifiers = new Modifier[]
            {
                StickDynamicsModifier.DefaultVelocity,
                new OutputScaleModifier(1.0)
            }
        });
        var sensCard = vm.Modifiers.First(c => c.Modifier is OutputScaleModifier);
        vm.SelectedCard = sensCard;
        var proxy = vm.SelectedProxy.Should().BeOfType<OutputScaleProxy>().Subject;
        proxy.Factor = 2.5;
        ((OutputScaleModifier)sensCard.Modifier).Factor.Should().Be(2.5);
    }
}
