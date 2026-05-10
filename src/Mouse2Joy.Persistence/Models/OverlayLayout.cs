using System.Text.Json;

namespace Mouse2Joy.Persistence.Models;

public sealed record OverlayLayout
{
    public bool Enabled { get; init; }
    public List<WidgetConfig> Widgets { get; init; } = new();
}

/// <summary>
/// One of nine reference points on a rectangle. Used both for "where on the
/// reference frame (parent/monitor) does the widget attach" and "which point
/// on the widget itself lands at that attach point".
/// </summary>
public enum Anchor
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Center,
    Right,
    BottomLeft,
    Bottom,
    BottomRight
}

public sealed record WidgetConfig
{
    /// <summary>Stable per-instance id. Generated on creation; survives type changes.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Stable identifier of the widget type, e.g. "LeftStick", "Buttons", "ProfileStatus".</summary>
    public string Type { get; init; } = "";

    /// <summary>
    /// Optional user-defined label. Shown in the Overlay tab's widget table; the
    /// auto-numbered "#N" hint is used as a fallback when this is empty.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Optional parent widget id. When set, this widget's <see cref="X"/>/<see cref="Y"/>
    /// are interpreted as offsets from the parent's resolved position. The widget also
    /// inherits its parent's <see cref="MonitorIndex"/> (its own value is ignored).
    /// </summary>
    public string? ParentId { get; init; }

    /// <summary>
    /// Index into the monitor enumeration (0 = primary). Ignored when
    /// <see cref="ParentId"/> is non-null. If the persisted index doesn't exist
    /// (monitor unplugged), the renderer falls back to monitor 0 without rewriting settings.
    /// </summary>
    public int MonitorIndex { get; init; } = 0;

    public bool Visible { get; init; } = true;

    /// <summary>
    /// Which point on the reference frame (parent's rendered rect when <see cref="ParentId"/>
    /// is set, otherwise the widget's monitor bounds) the widget anchors to. The
    /// <see cref="X"/>/<see cref="Y"/> offsets are added to this point.
    /// </summary>
    public Anchor AnchorPoint { get; init; } = Anchor.TopLeft;

    /// <summary>
    /// Which point on the widget itself lands at the anchor. Lets the user say
    /// "place my widget's bottom-right at the parent's top-left, with no offset".
    /// </summary>
    public Anchor SelfAnchor { get; init; } = Anchor.TopLeft;

    /// <summary>
    /// X offset (DIPs) from the resolved anchor point on the reference frame.
    /// Sign convention is standard screen space: positive = right.
    /// </summary>
    public double X { get; init; }

    /// <summary>Y offset; positive = down.</summary>
    public double Y { get; init; }

    /// <summary>Rendered width in DIPs. Replaces the old Scale multiplier.</summary>
    public double Width { get; init; } = 80;

    /// <summary>Rendered height in DIPs.</summary>
    public double Height { get; init; } = 80;

    /// <summary>
    /// When true, the editor keeps <see cref="Width"/> and <see cref="Height"/>
    /// in lockstep (editing one updates the other to preserve the current ratio).
    /// Square-only widget types force this on regardless of the persisted value.
    /// </summary>
    public bool LockAspect { get; init; } = true;

    /// <summary>Per-widget styling. Schema is widget-defined; stored as raw JSON elements.</summary>
    public Dictionary<string, JsonElement> Options { get; init; } = new();
}
