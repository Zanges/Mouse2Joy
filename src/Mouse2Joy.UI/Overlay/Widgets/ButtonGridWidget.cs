using System.Windows;
using System.Windows.Media;
using Mouse2Joy.Engine;

namespace Mouse2Joy.UI.Overlay.Widgets;

/// <summary>
/// Utility widget — all 15 XInput buttons in a 2-row grid. Useful for an
/// at-a-glance "is anything held?" view. Sized via W/H; geometry adapts to
/// the configured rect.
/// </summary>
public sealed class ButtonGridWidget : OverlayWidget
{
    public override string TypeId => "ButtonGrid";

    public static IReadOnlyList<OptionDescriptor> OptionSchema { get; } = new OptionDescriptor[]
    {
        new("accentColor", "Pressed color", OptionKind.Color, "#FF00C878"),
        new("compact", "Compact (no labels)", OptionKind.Bool, false),
        new("showBackground", "Show background", OptionKind.Bool, false)
    };

    private static readonly (string Label, XInputButtons Mask)[] Layout =
    {
        ("A", XInputButtons.A), ("B", XInputButtons.B), ("X", XInputButtons.X), ("Y", XInputButtons.Y),
        ("LB", XInputButtons.LeftShoulder), ("RB", XInputButtons.RightShoulder),
        ("LS", XInputButtons.LeftThumb), ("RS", XInputButtons.RightThumb),
        ("Bk", XInputButtons.Back), ("St", XInputButtons.Start), ("Gd", XInputButtons.Guide),
        ("U", XInputButtons.DPadUp), ("D", XInputButtons.DPadDown), ("L", XInputButtons.DPadLeft), ("R", XInputButtons.DPadRight)
    };

    protected override Size MeasureOverride(Size availableSize)
        => new(Config.Width, Config.Height);

    protected override void OnRender(DrawingContext dc)
    {
        var w = Math.Max(0, Config.Width);
        var h = Math.Max(0, Config.Height);
        var accent = ReadColorBrush("accentColor", AccentBrush);
        var compact = ReadBool("compact", false);

        if (ReadBool("showBackground", false))
            dc.DrawRoundedRectangle(BgBrush, Outline, new Rect(0, 0, w, h), 6, 6);

        // 8 columns × 2 rows = 16 cells; only 15 used. Cell size derived from W/H.
        const int cols = 8;
        const double pad = 4;
        var cellW = (w - 2 * pad) / cols;
        var cellH = (h - 2 * pad) / 2;
        // Font size scales off the smaller cell axis so it fits.
        var fontSize = Math.Max(7, Math.Min(cellW, cellH) * 0.55);

        for (int i = 0; i < Layout.Length; i++)
        {
            var col = i % cols;
            var row = i / cols;
            var x = pad + col * cellW;
            var y = pad + row * cellH;
            var pressed = (Snapshot.Buttons & Layout[i].Mask) != 0;
            var rect = new Rect(x + 1, y + 1, Math.Max(0, cellW - 2), Math.Max(0, cellH - 2));
            // Pressed cell: filled with accent. Unpressed: bg-tinted to keep the cell visible
            // even when showBackground is off (the panel-wide bg isn't drawn in that case).
            dc.DrawRoundedRectangle(pressed ? accent : BgBrush, Outline, rect, 3, 3);
            if (!compact && rect.Width > 6 && rect.Height > 6)
            {
                var ft = new FormattedText(Layout[i].Label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Consolas"), fontSize, TextBrush, 1.0);
                dc.DrawText(ft, new Point(rect.X + (rect.Width - ft.Width) / 2, rect.Y + (rect.Height - ft.Height) / 2));
            }
        }
    }
}
