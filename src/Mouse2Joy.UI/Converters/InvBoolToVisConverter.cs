using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Mouse2Joy.UI.Converters;

/// <summary>
/// Inverse Boolean-to-Visibility: true → Collapsed, false → Visible. Used
/// to show validation banners when IsValid is false without inverting the
/// boolean in the view model.
/// </summary>
public sealed class InvBoolToVisConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
