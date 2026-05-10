namespace Mouse2Joy.UI.Tooltips;

/// <summary>
/// Structured tooltip content with optional sectioning. Bound by an
/// implicit DataTemplate in App.xaml that renders Typical first (italic,
/// dim) so the most-needed-on-the-spot answer is closest to the input,
/// then Description, then Advice. Null/empty fields are skipped.
///
/// Both XAML and C# can construct this. In XAML use the property-setter
/// form: <c>&lt;tt:TooltipContent Description="..." Typical="..."/&gt;</c>.
/// </summary>
public sealed class TooltipContent
{
    /// <summary>Main prose: what the parameter does.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Optional guidance: when to change it, when not to.</summary>
    public string? Advice { get; set; }

    /// <summary>Optional metadata: typical value range. Rendered first, italic + dim, prefixed by the template with "Typical ".</summary>
    public string? Typical { get; set; }

    public TooltipContent() { }

    public TooltipContent(string description, string? advice = null, string? typical = null)
    {
        Description = description;
        Advice = advice;
        Typical = typical;
    }
}
