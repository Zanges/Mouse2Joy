using System.Text.Json.Serialization;

namespace Mouse2Joy.Persistence.Legacy.V1;

/// <summary>
/// v1-schema stick-model polymorphic hierarchy. Read-only; only used by the
/// migration path. The discriminator names match what was on disk in v1.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(LegacyVelocityStickModel), "velocity")]
[JsonDerivedType(typeof(LegacyAccumulatorStickModel), "accumulator")]
[JsonDerivedType(typeof(LegacyPersistentStickModel), "persistent")]
internal abstract record LegacyStickModel;

internal sealed record LegacyVelocityStickModel(double DecayPerSecond, double MaxVelocityCounts) : LegacyStickModel;
internal sealed record LegacyAccumulatorStickModel(double SpringPerSecond, double CountsPerFullDeflection) : LegacyStickModel;
internal sealed record LegacyPersistentStickModel(double CountsPerFullDeflection) : LegacyStickModel;
