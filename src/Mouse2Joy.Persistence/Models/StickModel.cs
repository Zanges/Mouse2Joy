using System.Text.Json.Serialization;

namespace Mouse2Joy.Persistence.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(VelocityStickModel), "velocity")]
[JsonDerivedType(typeof(AccumulatorStickModel), "accumulator")]
[JsonDerivedType(typeof(PersistentStickModel), "persistent")]
public abstract record StickModel;

/// <param name="DecayPerSecond">Exponential decay rate. deflection *= exp(-DecayPerSecond * dt). Higher = snappier return-to-center.</param>
/// <param name="MaxVelocityCounts">Mouse counts/sec that map to full deflection (1.0).</param>
public sealed record VelocityStickModel(double DecayPerSecond, double MaxVelocityCounts) : StickModel;

/// <param name="SpringPerSecond">Exponential spring rate. deflection *= exp(-SpringPerSecond * dt).</param>
/// <param name="CountsPerFullDeflection">Mouse counts integrated to reach full deflection from center.</param>
public sealed record AccumulatorStickModel(double SpringPerSecond, double CountsPerFullDeflection) : StickModel;

/// <param name="CountsPerFullDeflection">Mouse counts integrated to reach full deflection from center. The stick stays at the integrated position; there is no auto-recenter. Move the mouse the same distance in the opposite direction to recenter. Overshoot past full deflection is clamped and discarded.</param>
public sealed record PersistentStickModel(double CountsPerFullDeflection) : StickModel;
