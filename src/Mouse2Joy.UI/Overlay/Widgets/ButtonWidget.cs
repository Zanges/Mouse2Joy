using System.Windows;
using System.Windows.Media;
using Mouse2Joy.Engine;

namespace Mouse2Joy.UI.Overlay.Widgets;

/// <summary>
/// Single-button indicator. Rounded rect filling W/H; lights up with the
/// accent colour while the source button is pressed, dim outline otherwise.
/// Source picks one of the 15 XInput buttons.
/// </summary>
public sealed class ButtonWidget : OverlayWidget
{
    /// <summary>Source enum order; matches the editor's combobox order.</summary>
    public static readonly string[] Sources =
    {
        "A", "B", "X", "Y",
        "LeftShoulder", "RightShoulder",
        "LeftThumb", "RightThumb",
        "Back", "Start", "Guide",
        "DPadUp", "DPadDown", "DPadLeft", "DPadRight"
    };

    public override string TypeId => "Button";

    public static IReadOnlyList<OptionDescriptor> OptionSchema { get; } = new OptionDescriptor[]
    {
        new("source", "Source", OptionKind.Enum, "A", EnumValues: Sources),
        new("accentColor", "Pressed color", OptionKind.Color, "#FF00C878"),
        new("showBackground", "Show background", OptionKind.Bool, false)
    };

    protected override Size MeasureOverride(Size availableSize)
        => new(Config.Width, Config.Height);

    protected override void OnRender(DrawingContext dc)
    {
        var w = Math.Max(0, Config.Width);
        var h = Math.Max(0, Config.Height);
        var accent = ReadColorBrush("accentColor", AccentBrush);
        var source = ReadString("source", "A");
        var mask = ParseMask(source);
        var pressed = (Snapshot.Buttons & mask) != 0;

        if (ReadBool("showBackground", false))
            dc.DrawRoundedRectangle(BgBrush, Outline, new Rect(0, 0, w, h), 6, 6);

        // Pressed = filled with accent, otherwise just an outlined rounded rect.
        var fill = pressed ? accent : null;
        var rect = new Rect(1, 1, Math.Max(0, w - 2), Math.Max(0, h - 2));
        dc.DrawRoundedRectangle(fill, Outline, rect, 4, 4);
    }

    private static XInputButtons ParseMask(string source) => source switch
    {
        "A" => XInputButtons.A,
        "B" => XInputButtons.B,
        "X" => XInputButtons.X,
        "Y" => XInputButtons.Y,
        "LeftShoulder" => XInputButtons.LeftShoulder,
        "RightShoulder" => XInputButtons.RightShoulder,
        "LeftThumb" => XInputButtons.LeftThumb,
        "RightThumb" => XInputButtons.RightThumb,
        "Back" => XInputButtons.Back,
        "Start" => XInputButtons.Start,
        "Guide" => XInputButtons.Guide,
        "DPadUp" => XInputButtons.DPadUp,
        "DPadDown" => XInputButtons.DPadDown,
        "DPadLeft" => XInputButtons.DPadLeft,
        "DPadRight" => XInputButtons.DPadRight,
        _ => XInputButtons.None
    };
}
