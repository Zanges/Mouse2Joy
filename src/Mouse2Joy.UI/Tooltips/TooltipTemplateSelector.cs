using System.Windows;
using System.Windows.Controls;

namespace Mouse2Joy.UI.Tooltips;

/// <summary>
/// Picks the right ContentTemplate for a tooltip's content. Strings get
/// the wrapping plain-text template; <see cref="TooltipContent"/> records
/// fall through to the typed DataTemplate that ships in App.xaml. Wired
/// up by the implicit ToolTip style in App.xaml.
/// </summary>
public sealed class TooltipTemplateSelector : DataTemplateSelector
{
    public DataTemplate? StringTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is string)
            return StringTemplate;
        // For TooltipContent (or any other typed item), return null so WPF
        // falls back to its normal DataType-keyed template lookup, which
        // resolves to the <DataTemplate DataType="tt:TooltipContent"/> in
        // App.xaml.
        return null;
    }
}
