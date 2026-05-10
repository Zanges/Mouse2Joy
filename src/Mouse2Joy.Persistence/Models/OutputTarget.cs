using System.Text.Json.Serialization;

namespace Mouse2Joy.Persistence.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(StickAxisTarget), "stickAxis")]
[JsonDerivedType(typeof(TriggerTarget), "trigger")]
[JsonDerivedType(typeof(ButtonTarget), "button")]
[JsonDerivedType(typeof(DPadTarget), "dpad")]
public abstract record OutputTarget;

public sealed record StickAxisTarget(Stick Stick, AxisComponent Component) : OutputTarget;

public sealed record TriggerTarget(Trigger Trigger) : OutputTarget;

public sealed record ButtonTarget(GamepadButton Button) : OutputTarget;

public sealed record DPadTarget(DPadDirection Direction) : OutputTarget;
