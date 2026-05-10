namespace Mouse2Joy.Persistence.Legacy.V1;

/// <summary>
/// v1-schema per-axis shaping parameters. Read-only; only used by the
/// migration path. New code should never reference this type — use the
/// modifier chain instead.
/// </summary>
internal sealed record LegacyCurve(
    double Sensitivity,
    double InnerDeadzone,
    double OuterSaturation,
    double Exponent)
{
    public static LegacyCurve Default { get; } = new(1.0, 0.0, 0.0, 1.0);
}
