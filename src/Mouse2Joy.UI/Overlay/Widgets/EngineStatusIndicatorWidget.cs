using System.Windows;
using System.Windows.Media;
using Mouse2Joy.Engine.State;

namespace Mouse2Joy.UI.Overlay.Widgets;

/// <summary>
/// Engine status indicator — a colored dot that reflects the current
/// <see cref="EngineMode"/> (Active=green, SoftMuted=yellow, Off=grey). This is
/// the indicator half of the old bundled <c>Status</c> widget; the textual
/// half (mode name, profile name, button/axis status) now lives in the new
/// text-only <see cref="StatusWidget"/>. Square widget; the dot fills the
/// configured box with a small inset.
/// </summary>
public sealed class EngineStatusIndicatorWidget : OverlayWidget
{
    public override string TypeId => "EngineStatusIndicator";

    public static IReadOnlyList<OptionDescriptor> OptionSchema { get; } = new OptionDescriptor[]
    {
        new("activeColor", "Active color", OptionKind.Color, "#FF00C878"),
        new("softMutedColor", "Soft-muted color", OptionKind.Color, "#FFDCB400"),
        new("offColor", "Off color", OptionKind.Color, "#FF787878"),
        new("showBackground", "Show background", OptionKind.Bool, false),
        new("backgroundColor", "Background color", OptionKind.Color, "#8C000000")
    };

    protected override Size MeasureOverride(Size availableSize)
        => new(Config.Width, Config.Height);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var w = Math.Max(0, Config.Width);
        var h = Math.Max(0, Config.Height);
        if (w <= 0 || h <= 0)
        {
            return;
        }

        if (ReadBool("showBackground", false))
        {
            var bg = ReadColorBrush("backgroundColor", BgBrush);
            drawingContext.DrawRoundedRectangle(bg, Outline, new Rect(0, 0, w, h), 6, 6);
        }

        var fallback = Color.FromRgb(120, 120, 120);
        var activeBrush = ReadColorBrush("activeColor", new SolidColorBrush(Color.FromRgb(0, 200, 120)));
        var softBrush = ReadColorBrush("softMutedColor", new SolidColorBrush(Color.FromRgb(220, 180, 0)));
        var offBrush = ReadColorBrush("offColor", new SolidColorBrush(fallback));

        var brush = Snapshot.Mode switch
        {
            EngineMode.Active => activeBrush,
            EngineMode.SoftMuted => softBrush,
            EngineMode.Off => offBrush,
            _ => offBrush
        };

        // Inset so the dot doesn't touch the box edge. Radius is the smaller of
        // the two halves (so non-square boxes still render a proper circle).
        var cx = w / 2.0;
        var cy = h / 2.0;
        var r = Math.Max(0, Math.Min(w, h) / 2.0 - 2);
        drawingContext.DrawEllipse(brush, null, new Point(cx, cy), r, r);
    }
}
