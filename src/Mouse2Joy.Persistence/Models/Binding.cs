namespace Mouse2Joy.Persistence.Models;

public sealed record Binding
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required InputSource Source { get; init; }
    public required OutputTarget Target { get; init; }

    /// <summary>
    /// Ordered list of transforms applied between Source and Target. The
    /// engine evaluates these in order, threading a typed signal through
    /// each. An empty list is valid only when Source's output signal type
    /// already matches Target's input type (e.g. button → button).
    /// </summary>
    public IReadOnlyList<Modifier> Modifiers { get; init; } = Array.Empty<Modifier>();

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
