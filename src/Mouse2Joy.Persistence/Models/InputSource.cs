using System.Text.Json.Serialization;

namespace Mouse2Joy.Persistence.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(MouseAxisSource), "mouseAxis")]
[JsonDerivedType(typeof(MouseButtonSource), "mouseButton")]
[JsonDerivedType(typeof(MouseScrollSource), "mouseScroll")]
[JsonDerivedType(typeof(KeySource), "key")]
public abstract record InputSource;

public sealed record MouseAxisSource(MouseAxis Axis) : InputSource;

public sealed record MouseButtonSource(MouseButton Button) : InputSource;

public sealed record MouseScrollSource(ScrollDirection Direction) : InputSource;

public sealed record KeySource(VirtualKey Key) : InputSource;
