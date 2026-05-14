using System.Windows;
using System.Windows.Media;

namespace Mouse2Joy.UI.Overlay.Widgets;

/// <summary>
/// TwoAxis widget — circle-with-dot visual for a full 2D stick. Source picks
/// the left or right stick; W/H are forced equal by the editor (square-only),
/// so internal geometry uses min(W, H) as the working size.
/// </summary>
public sealed class TwoAxisWidget : OverlayWidget
{
    public override string TypeId => "TwoAxis";

    public static readonly string[] Sources = { "LeftStick", "RightStick" };

    public static IReadOnlyList<OptionDescriptor> OptionSchema { get; } = new OptionDescriptor[]
    {
        new("source", "Source", OptionKind.Enum, "LeftStick", EnumValues: Sources),
        new("accentColor", "Accent color", OptionKind.Color, "#FF00C878"),
        new("showBackground", "Show background", OptionKind.Bool, false)
    };

    protected override Size MeasureOverride(Size availableSize)
        => new(Config.Width, Config.Height);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var w = Math.Max(0, Config.Width);
        var h = Math.Max(0, Config.Height);
        var accent = ReadColorBrush("accentColor", AccentBrush);
        var source = ReadString("source", "LeftStick");

        // The widget is square-only via editor enforcement; use min(W,H) defensively
        // so a non-square value (e.g. from a hand-edited file) still renders sanely.
        var working = Math.Max(0, Math.Min(w, h));
        var center = new Point(w / 2.0, working / 2.0);
        var radius = working * 0.45;

        if (ReadBool("showBackground", false))
        {
            drawingContext.DrawRoundedRectangle(BgBrush, Outline, new Rect(0, 0, w, h), 6, 6);
        }

        drawingContext.DrawEllipse(null, Outline, center, radius, radius);
        drawingContext.DrawLine(Outline, new Point(center.X - radius, center.Y), new Point(center.X + radius, center.Y));
        drawingContext.DrawLine(Outline, new Point(center.X, center.Y - radius), new Point(center.X, center.Y + radius));

        var (dx, dy) = source switch
        {
            "RightStick" => (Snapshot.RightStickX, Snapshot.RightStickY),
            _ => (Snapshot.LeftStickX, Snapshot.LeftStickY)
        };

        var px = center.X + dx * radius;
        // WPF Y is downward; gamepad Y conventionally up = positive. Invert for natural display.
        var py = center.Y - dy * radius;
        var dotR = Math.Max(2, working * 0.06);
        drawingContext.DrawEllipse(accent, null, new Point(px, py), dotR, dotR);
    }
}
