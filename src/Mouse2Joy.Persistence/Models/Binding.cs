namespace Mouse2Joy.Persistence.Models;

public sealed record Binding
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required InputSource Source { get; init; }
    public required OutputTarget Target { get; init; }
    public Curve Curve { get; init; } = Curve.Default;

    /// <summary>Set only when Target is a <see cref="StickAxisTarget"/>; otherwise null.</summary>
    public StickModel? StickModel { get; init; }

    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Optional user-supplied label for the row. Null/empty means "no custom
    /// name" — the UI falls back to an auto-generated "Source → Target" string.
    /// Stored verbatim; trimming is the caller's responsibility.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>
    /// When true, the matching real input is swallowed before reaching the
    /// focused application (mouse cursor stops moving, key never produces text).
    /// When false, the input passes through and the gamepad output is generated
    /// in addition. Default per source kind: mouse-axis = true (cursor would
    /// otherwise fight the stick), everything else = false.
    /// </summary>
    public bool SuppressInput { get; init; } = true;
}
