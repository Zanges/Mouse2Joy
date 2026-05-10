using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Persistence.Legacy.V1;

/// <summary>
/// v1-shaped Profile / Binding records used only for deserializing v1 JSON
/// before migration. After migration the v2 <see cref="Profile"/> /
/// <see cref="Binding"/> types take over.
///
/// Source / Target / enums are stable across v1 and v2, so we re-use those
/// types directly. Only the shaping/integration fields differ.
/// </summary>
internal sealed record LegacyProfile
{
    public int SchemaVersion { get; init; } = 1;
    public string Name { get; init; } = "";
    public int TickRateHz { get; init; } = 250;
    public List<LegacyBinding> Bindings { get; init; } = new();
}

internal sealed record LegacyBinding
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required InputSource Source { get; init; }
    public required OutputTarget Target { get; init; }
    public LegacyCurve Curve { get; init; } = LegacyCurve.Default;
    public LegacyStickModel? StickModel { get; init; }
    public bool Enabled { get; init; } = true;
    public string? Label { get; init; }
    public bool SuppressInput { get; init; } = true;
}
