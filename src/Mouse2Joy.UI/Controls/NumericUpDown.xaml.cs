using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Mouse2Joy.UI.Controls;

/// <summary>
/// Numeric input with up/down step buttons. Soft-bounded by default — typing a value
/// outside [<see cref="Min"/>, <see cref="Max"/>] is accepted, the buttons clamp.
/// Wires hold-to-repeat via <see cref="RepeatButton"/>; mouse wheel and arrow keys
/// step too. Reverts to the last good value on parse failure.
/// </summary>
public partial class NumericUpDown : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(NumericUpDown),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.Journal,
                OnValueChanged));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(double), typeof(NumericUpDown),
            new PropertyMetadata(1.0));

    public static readonly DependencyProperty MinProperty =
        DependencyProperty.Register(nameof(Min), typeof(double?), typeof(NumericUpDown),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MaxProperty =
        DependencyProperty.Register(nameof(Max), typeof(double?), typeof(NumericUpDown),
            new PropertyMetadata(null));

    public static readonly DependencyProperty DecimalsProperty =
        DependencyProperty.Register(nameof(Decimals), typeof(int), typeof(NumericUpDown),
            new PropertyMetadata(0, OnDecimalsChanged));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public double? Min
    {
        get => (double?)GetValue(MinProperty);
        set => SetValue(MinProperty, value);
    }

    public double? Max
    {
        get => (double?)GetValue(MaxProperty);
        set => SetValue(MaxProperty, value);
    }

    public int Decimals
    {
        get => (int)GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    /// <summary>Fires after <see cref="Value"/> changes, regardless of source (button, wheel, arrow, manual entry).</summary>
    public event Action<NumericUpDown, double, double>? ValueChanged;

    public NumericUpDown()
    {
        InitializeComponent();
        SyncTextFromValue();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not NumericUpDown self) return;
        self.SyncTextFromValue();
        self.ValueChanged?.Invoke(self, (double)e.OldValue, (double)e.NewValue);
    }

    private static void OnDecimalsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NumericUpDown self) self.SyncTextFromValue();
    }

    private void SyncTextFromValue()
    {
        var format = "F" + Math.Max(0, Decimals);
        Tb.Text = Value.ToString(format, CultureInfo.InvariantCulture);
    }

    private void Bump(double direction)
    {
        // First, commit any pending text edit so a held button doesn't fight it.
        TryCommitText();
        var next = Value + direction * Step;
        if (Min.HasValue) next = Math.Max(Min.Value, next);
        if (Max.HasValue) next = Math.Min(Max.Value, next);
        Value = next;
    }

    private void OnUpClick(object sender, RoutedEventArgs e) => Bump(+1);
    private void OnDownClick(object sender, RoutedEventArgs e) => Bump(-1);

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Bump(e.Delta > 0 ? +1 : -1);
        e.Handled = true;
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                Bump(+1);
                e.Handled = true;
                break;
            case Key.Down:
                Bump(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                TryCommitText();
                e.Handled = true;
                break;
        }
    }

    private void OnTextBoxLostFocus(object sender, RoutedEventArgs e) => TryCommitText();

    private void TryCommitText()
    {
        if (double.TryParse(Tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            Value = v;
            // Re-sync so the displayed text matches the stored precision.
            SyncTextFromValue();
        }
        else
        {
            // Bad input — revert to the last good value.
            SyncTextFromValue();
        }
    }
}
