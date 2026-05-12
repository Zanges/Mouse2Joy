using Mouse2Joy.Persistence.Legacy.V1;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Persistence.Migration;

/// <summary>
/// Pure transformation from a v1-shaped <see cref="LegacyProfile"/> to a v2
/// <see cref="Profile"/>. For each binding, builds the equivalent modifier
/// chain so post-migration runtime behavior matches v1 exactly.
///
/// Migration rules:
/// <list type="bullet">
///   <item>Mouse-axis source + stick-axis target: prepend a StickDynamics
///         modifier carrying the v1 mode and params. If the v1 binding
///         had a null StickModel, emit an explicit Velocity default
///         (matching v1's StickProcessorFactory fallback).</item>
///   <item>Digital source (key / mouse-button / scroll) + Scalar target
///         (stick-axis or trigger): prepend a DigitalToScalar(1.0, 0.0).
///         This matches v1 which treated digital sources as 1.0 on press,
///         0.0 on release for Scalar targets.</item>
///   <item>Digital source + Digital target (button / dpad): no converter.
///         The v1 Curve fields are ignored for these targets so they
///         migrate to nothing.</item>
///   <item>Append shape modifiers (OutputScale, InnerDeadzone,
///         OuterSaturation, ResponseCurve) only when they differ from
///         identity. Order matches the v1 CurveEvaluator so curves stay
///         numerically identical: OutputScale → Inner → Outer → Response.
///         (Originally emitted SensitivityModifier; renamed to OutputScale
///         in v3. V1ToV2 emits the current type directly so the V2ToV3 step
///         is a no-op for v1-sourced profiles.)</item>
/// </list>
/// </summary>
internal static class V1ToV2
{
    public static Profile Migrate(LegacyProfile v1)
    {
        var bindings = new List<Binding>(v1.Bindings.Count);
        foreach (var lb in v1.Bindings)
            bindings.Add(MigrateBinding(lb));

        return new Profile
        {
            SchemaVersion = Profile.CurrentSchemaVersion,
            Name = v1.Name,
            TickRateHz = v1.TickRateHz,
            Bindings = bindings
        };
    }

    private static Binding MigrateBinding(LegacyBinding lb)
    {
        var modifiers = new List<Modifier>(8);

        var sourceType = ModifierTypes.GetSourceOutputType(lb.Source);
        var targetType = ModifierTypes.GetTargetInputType(lb.Target);

        // Step 1: prepend the converter for mouse-axis sources.
        if (sourceType == SignalType.Delta && targetType == SignalType.Scalar)
        {
            modifiers.Add(MigrateStickModel(lb.StickModel));
        }
        // Step 2: prepend a Digital→Scalar converter for digital→Scalar bindings.
        else if (sourceType == SignalType.Digital && targetType == SignalType.Scalar)
        {
            modifiers.Add(DigitalToScalarModifier.Default);
        }
        // (Digital→Digital and Scalar→Scalar after StickDynamics are handled below.)

        // Step 3: append shape modifiers — only meaningful for Scalar targets.
        if (targetType == SignalType.Scalar)
        {
            var c = lb.Curve;
            if (c.Sensitivity != 1.0)
                modifiers.Add(new OutputScaleModifier(c.Sensitivity));
            if (c.InnerDeadzone > 0)
                modifiers.Add(new InnerDeadzoneModifier(c.InnerDeadzone));
            if (c.OuterSaturation > 0)
                modifiers.Add(new OuterSaturationModifier(c.OuterSaturation));
            if (c.Exponent != 1.0)
                modifiers.Add(new ResponseCurveModifier(c.Exponent));
        }

        return new Binding
        {
            Id = lb.Id,
            Source = lb.Source,
            Target = lb.Target,
            Modifiers = modifiers,
            Enabled = lb.Enabled,
            Label = lb.Label,
            SuppressInput = lb.SuppressInput,
        };
    }

    private static StickDynamicsModifier MigrateStickModel(LegacyStickModel? sm) => sm switch
    {
        LegacyVelocityStickModel v => new StickDynamicsModifier(StickDynamicsMode.Velocity, v.DecayPerSecond, v.MaxVelocityCounts),
        LegacyAccumulatorStickModel a => new StickDynamicsModifier(StickDynamicsMode.Accumulator, a.SpringPerSecond, a.CountsPerFullDeflection),
        LegacyPersistentStickModel p => new StickDynamicsModifier(StickDynamicsMode.Persistent, p.CountsPerFullDeflection, 0.0),
        // v1 default fallback (matches StickProcessorFactory.Create(null)).
        null => StickDynamicsModifier.DefaultVelocity,
        _ => throw new ArgumentOutOfRangeException(nameof(sm), sm, "Unknown legacy stick model.")
    };
}
