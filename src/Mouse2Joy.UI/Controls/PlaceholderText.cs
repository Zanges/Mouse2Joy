using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Mouse2Joy.UI.Controls;

/// <summary>
/// Attached behaviour that renders italic, dim placeholder text inside a
/// <see cref="TextBox"/> when its <see cref="TextBox.Text"/> is empty. Set via
/// <c>controls:PlaceholderText.Text="…"</c> in XAML, or in code via
/// <see cref="SetText"/>. The placeholder hides the moment any character is typed
/// and reappears when the box is cleared. Focus does not toggle visibility — the
/// hint stays visible while the field is empty so the user can read the auto-label
/// they would otherwise inherit.
/// </summary>
/// <remarks>
/// Implementation: a single <see cref="TextBlock"/> hosted inside a one-shot
/// <see cref="Adorner"/> attached to the textbox. The adorner sits above the
/// textbox content and below the caret — typing draws on top of it. The adorner
/// is created lazily on the first non-empty assignment of the attached property
/// and toggles its visibility from a <see cref="TextBox.TextChanged"/> handler.
/// </remarks>
public static class PlaceholderText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text",
        typeof(string),
        typeof(PlaceholderText),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;

        // Wire one-time hooks per textbox. The Loaded event ensures the adorner
        // layer is available; TextChanged updates visibility as the user types.
        if (tb.GetValue(IsHookedProperty) is not true)
        {
            tb.SetValue(IsHookedProperty, true);
            tb.Loaded += OnTextBoxLoaded;
            tb.TextChanged += OnTextBoxTextChanged;
            // If the textbox is already loaded (e.g. attached prop set after init),
            // attach the adorner now.
            if (tb.IsLoaded) AttachAdorner(tb);
        }
        else
        {
            UpdateAdornerText(tb, (string)e.NewValue);
            UpdatePlaceholderVisibility(tb);
        }
    }

    private static readonly DependencyProperty IsHookedProperty = DependencyProperty.RegisterAttached(
        "IsHooked", typeof(bool), typeof(PlaceholderText), new PropertyMetadata(false));

    private static readonly DependencyProperty AdornerProperty = DependencyProperty.RegisterAttached(
        "Adorner", typeof(PlaceholderAdorner), typeof(PlaceholderText), new PropertyMetadata(null));

    private static void OnTextBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) AttachAdorner(tb);
    }

    private static void OnTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb) UpdatePlaceholderVisibility(tb);
    }

    private static void AttachAdorner(TextBox tb)
    {
        var layer = AdornerLayer.GetAdornerLayer(tb);
        if (layer is null) return;
        if (tb.GetValue(AdornerProperty) is PlaceholderAdorner) return;

        var adorner = new PlaceholderAdorner(tb, GetText(tb));
        layer.Add(adorner);
        tb.SetValue(AdornerProperty, adorner);
        UpdatePlaceholderVisibility(tb);
    }

    private static void UpdateAdornerText(TextBox tb, string newPlaceholder)
    {
        if (tb.GetValue(AdornerProperty) is PlaceholderAdorner adorner)
            adorner.SetPlaceholder(newPlaceholder);
    }

    private static void UpdatePlaceholderVisibility(TextBox tb)
    {
        if (tb.GetValue(AdornerProperty) is not PlaceholderAdorner adorner) return;
        adorner.Visibility = string.IsNullOrEmpty(tb.Text) && !string.IsNullOrEmpty(GetText(tb))
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// One-line italic dim text rendered in the textbox's adorner layer. Padded
    /// to align with the textbox's own text content (left padding accounts for
    /// the default 2px textbox border + 2px content padding).
    /// </summary>
    private sealed class PlaceholderAdorner : Adorner
    {
        private static readonly Brush DimBrush;
        private readonly TextBlock _text;

        static PlaceholderAdorner()
        {
            DimBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
            DimBrush.Freeze();
        }

        public PlaceholderAdorner(TextBox owner, string placeholder) : base(owner)
        {
            IsHitTestVisible = false;
            _text = new TextBlock
            {
                Text = placeholder,
                Foreground = DimBrush,
                FontStyle = FontStyles.Italic,
                VerticalAlignment = VerticalAlignment.Center,
                // Match the inner padding a default WPF TextBox renders text at.
                Margin = new Thickness(4, 0, 4, 0),
                IsHitTestVisible = false,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            AddVisualChild(_text);
        }

        public void SetPlaceholder(string placeholder) => _text.Text = placeholder;

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _text;

        protected override Size MeasureOverride(Size constraint)
        {
            _text.Measure(constraint);
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _text.Arrange(new Rect(new Point(0, 0), finalSize));
            return finalSize;
        }
    }
}
