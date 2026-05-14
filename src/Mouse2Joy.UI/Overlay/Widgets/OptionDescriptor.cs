namespace Mouse2Joy.UI.Overlay.Widgets;

// Tag enum values intentionally match primitive type names (Bool/String/Int)
// so the editor schema reads naturally; suppress CA1720 here.
#pragma warning disable CA1720
public enum OptionKind
{
    Bool,
    Color,
    String,
    Int,
    Enum
}
#pragma warning restore CA1720

/// <summary>
/// Describes one editable option for a widget. The Widget Editor UI renders
/// fields by walking a widget's <c>OptionSchema</c> list and choosing a control
/// per <see cref="OptionKind"/>. Widgets read live values from
/// <c>Config.Options</c> via the helpers on <see cref="OverlayWidget"/>.
/// </summary>
public sealed record OptionDescriptor(
    string Key,
    string Label,
    OptionKind Kind,
    object Default,
    int? Min = null,
    int? Max = null,
    string[]? EnumValues = null);

public static class WidgetSchemas
{
    /// <summary>
    /// Map from a <c>WidgetConfig.Type</c> string to that widget's
    /// editable option list. Returns an empty list for unknown types so the
    /// editor can render a degraded but functional form.
    /// </summary>
    public static IReadOnlyList<OptionDescriptor> For(string type) => type switch
    {
        "Status" => StatusWidget.OptionSchema,
        "EngineStatusIndicator" => EngineStatusIndicatorWidget.OptionSchema,
        "Axis" => AxisWidget.OptionSchema,
        "TwoAxis" => TwoAxisWidget.OptionSchema,
        "Button" => ButtonWidget.OptionSchema,
        "Background" => BackgroundWidget.OptionSchema,
        "MouseActivity" => MouseActivityWidget.OptionSchema,
        "ButtonGrid" => ButtonGridWidget.OptionSchema,
        _ => Array.Empty<OptionDescriptor>()
    };
}
