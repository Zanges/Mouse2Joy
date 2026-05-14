using System.Windows;
using System.Windows.Media;

namespace Mouse2Joy.UI.Overlay.Widgets;

/// <summary>
/// Mouse activity dot — shows the recent mouse delta as an arrow inside a circle.
/// Square-only (editor enforces). Sized via W/H; uses min(W,H) defensively.
/// </summary>
public sealed class MouseActivityWidget : OverlayWidget
{
    public override string TypeId => "MouseActivity";

    public static IReadOnlyList<OptionDescriptor> OptionSchema { get; } = new OptionDescriptor[]
    {
        new("accentColor", "Arrow color", OptionKind.Color, "#FF00C878"),
        // Trail rendering is a future enhancement; the value persists today and feeds it once added.
        new("trailLength", "Trail length", OptionKind.Int, 0, Min: 0, Max: 10),
        new("showBackground", "Show background", OptionKind.Bool, false)
    };

    protected override Size MeasureOverride(Size availableSize)
        => new(Config.Width, Config.Height);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var w = Math.Max(0, Config.Width);
        var h = Math.Max(0, Config.Height);
        var accent = ReadColorBrush("accentColor", AccentBrush);

        // Reference scale derived from the smaller axis so non-square inputs (from
        // a hand-edited file) still render symmetrically. Editor enforces square.
        var size = Math.Min(w, h);
        var refScale = size / 80.0;
        var radius = size * 0.4;
        var center = new Point(w / 2.0, h / 2.0);

        if (ReadBool("showBackground", false))
        {
            drawingContext.DrawRoundedRectangle(BgBrush, Outline, new Rect(0, 0, w, h), 6, 6);
        }

        drawingContext.DrawEllipse(null, Outline, center, radius, radius);

        // Display recent mouse delta as an arrow vector.
        var dx = Snapshot.RawMouseDeltaX;
        var dy = Snapshot.RawMouseDeltaY;
        if (dx == 0 && dy == 0)
        {
            return;
        }

        var mag = Math.Sqrt(dx * dx + dy * dy);
        var ux = dx / mag;
        var uy = dy / mag;
        var len = Math.Min(mag * 0.6, radius);
        var end = new Point(center.X + ux * len, center.Y + uy * len);
        drawingContext.DrawLine(new Pen(accent, 2 * refScale), center, end);
        drawingContext.DrawEllipse(accent, null, end, 3 * refScale, 3 * refScale);
    }
}
