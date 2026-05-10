using System.Windows;
using System.Windows.Media;

namespace Mouse2Joy.UI.Overlay.Widgets;

/// <summary>
/// Utility widget — renders a filled rounded rectangle filling W/H. Intended
/// as a parent for grouping other widgets visually, or as a standalone backdrop.
/// Color includes alpha (#AARRGGBB hex); default matches the previous default
/// per-widget background panel.
/// </summary>
public sealed class BackgroundWidget : OverlayWidget
{
    public override string TypeId => "Background";

    public static IReadOnlyList<OptionDescriptor> OptionSchema { get; } = new OptionDescriptor[]
    {
        new("color", "Color (#AARRGGBB)", OptionKind.Color, "#90000000"),
        new("cornerRadius", "Corner radius", OptionKind.Int, 6, Min: 0, Max: 24)
    };

    protected override Size MeasureOverride(Size availableSize)
        => new(Config.Width, Config.Height);

    protected override void OnRender(DrawingContext dc)
    {
        var w = Math.Max(0, Config.Width);
        var h = Math.Max(0, Config.Height);
        if (w <= 0 || h <= 0) return;

        var fill = ReadColorBrush("color", BgBrush);
        var radius = Math.Clamp(ReadInt("cornerRadius", 6), 0, 24);

        dc.DrawRoundedRectangle(fill, null, new Rect(0, 0, w, h), radius, radius);
    }
}
