using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.UI.ViewModels;

/// <summary>
/// Pure display formatters for <see cref="WidgetConfig"/>. Mirrors
/// <see cref="BindingDisplay"/> for the bindings tab. The single public entry point
/// <see cref="ResolveDisplayName"/> is shared by the Overlay tab's widget table and
/// the widget editor's anchor-parent dropdown so the two never drift.
/// </summary>
public static class WidgetDisplay
{
    /// <summary>
    /// User-defined Name when set; otherwise an auto-generated "Type #N" disambiguator
    /// when there are multiple widgets of the same type; otherwise the bare type name.
    /// </summary>
    public static string ResolveDisplayName(WidgetConfig w, IReadOnlyList<WidgetConfig> all)
    {
        if (!string.IsNullOrWhiteSpace(w.Name))
        {
            return w.Name;
        }

        var sameType = all.Where(p => p.Type == w.Type).ToList();
        if (sameType.Count <= 1)
        {
            return w.Type;
        }

        var idx = sameType.FindIndex(p => p.Id == w.Id) + 1;
        return $"{w.Type} #{idx}";
    }
}
