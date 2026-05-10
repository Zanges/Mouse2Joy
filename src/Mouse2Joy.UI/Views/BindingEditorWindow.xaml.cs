using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Mouse2Joy.Persistence.Models;
using Mouse2Joy.UI.Controls;
using Mouse2Joy.UI.Tooltips;
using Mouse2Joy.UI.ViewModels;

namespace Mouse2Joy.UI.Views;

public partial class BindingEditorWindow : Window
{
    public Binding? Result { get; private set; }

    public BindingEditorWindow(Binding? initial = null)
    {
        InitializeComponent();
        // Populate ButtonCombo with the GamepadButton enum values.
        foreach (var name in Enum.GetNames(typeof(GamepadButton)))
            ButtonCombo.Items.Add(new ComboBoxItem { Content = name });

        SourceKindCombo.SelectionChanged += (_, _) => UpdateSourceVisibility();
        TargetKindCombo.SelectionChanged += (_, _) => { UpdateTargetVisibility(); UpdateStickModelVisibility(); };
        StickModelCombo.SelectionChanged += (_, _) => OnStickModelChanged();
        SensSlider.ValueChanged += (_, _) => UpdateCurve();
        DzSlider.ValueChanged += (_, _) => UpdateCurve();
        SatSlider.ValueChanged += (_, _) => UpdateCurve();
        ExpSlider.ValueChanged += (_, _) => UpdateCurve();

        // The Label field's placeholder previews what the row will read as if the
        // user leaves Label blank. Recompute it from the current Source/Target
        // selections every time either changes — and also from the secondary
        // pickers (axis, button, stick, etc.) since those determine the formatted
        // text in BindingDisplay.FormatAuto.
        SourceKindCombo.SelectionChanged   += (_, _) => UpdateAutoLabelPlaceholder();
        MouseAxisCombo.SelectionChanged    += (_, _) => UpdateAutoLabelPlaceholder();
        MouseButtonCombo.SelectionChanged  += (_, _) => UpdateAutoLabelPlaceholder();
        MouseScrollCombo.SelectionChanged  += (_, _) => UpdateAutoLabelPlaceholder();
        KeyBox.LostFocus                   += (_, _) => UpdateAutoLabelPlaceholder();
        TargetKindCombo.SelectionChanged   += (_, _) => UpdateAutoLabelPlaceholder();
        StickCombo.SelectionChanged        += (_, _) => UpdateAutoLabelPlaceholder();
        StickAxisCombo.SelectionChanged    += (_, _) => UpdateAutoLabelPlaceholder();
        TriggerCombo.SelectionChanged      += (_, _) => UpdateAutoLabelPlaceholder();
        ButtonCombo.SelectionChanged       += (_, _) => UpdateAutoLabelPlaceholder();
        DPadCombo.SelectionChanged         += (_, _) => UpdateAutoLabelPlaceholder();

        if (initial is not null) LoadFrom(initial);
        else
        {
            SourceKindCombo.SelectedIndex = 0;
            TargetKindCombo.SelectedIndex = 0;
            StickCombo.SelectedIndex = 0;
            StickAxisCombo.SelectedIndex = 0;
            MouseAxisCombo.SelectedIndex = 0;
            StickModelCombo.SelectedIndex = 0;
            // New binding: source defaults to mouse axis (idx 0) which conventionally suppresses.
            SuppressCb.IsChecked = true;
            UpdateCurve();
        }
        // When the user changes source kind, update the suppress default.
        SourceKindCombo.SelectionChanged += (_, _) => UpdateSuppressDefault();
        // Initial placeholder seed (after LoadFrom or default selections are in place).
        UpdateAutoLabelPlaceholder();
    }

    /// <summary>
    /// Recompute the Label TextBox's placeholder text from the current Source / Target
    /// selections. Mirrors what <see cref="BindingRowViewModel"/> would render in the
    /// table if the user left Label blank, so the editor and the table agree on the
    /// auto-label text. Tolerant to half-built state (returns the trailing "—" when a
    /// selector hasn't resolved yet) since the placeholder is purely informative.
    /// </summary>
    private void UpdateAutoLabelPlaceholder()
    {
        InputSource? src = SourceKindCombo.SelectedIndex switch
        {
            0 => new MouseAxisSource((MouseAxis)Math.Max(0, MouseAxisCombo.SelectedIndex)),
            1 => new MouseButtonSource((MouseButton)Math.Max(0, MouseButtonCombo.SelectedIndex)),
            2 => new MouseScrollSource((ScrollDirection)Math.Max(0, MouseScrollCombo.SelectedIndex)),
            3 => new KeySource(KeyBox.CapturedKey.IsNone ? new VirtualKey(0, false) : KeyBox.CapturedKey),
            _ => null
        };
        OutputTarget? tgt = TargetKindCombo.SelectedIndex switch
        {
            0 => new StickAxisTarget((Stick)Math.Max(0, StickCombo.SelectedIndex), (AxisComponent)Math.Max(0, StickAxisCombo.SelectedIndex)),
            1 => new TriggerTarget((Mouse2Joy.Persistence.Models.Trigger)Math.Max(0, TriggerCombo.SelectedIndex)),
            2 => new ButtonTarget((GamepadButton)Math.Max(0, ButtonCombo.SelectedIndex)),
            3 => new DPadTarget((DPadDirection)Math.Max(0, DPadCombo.SelectedIndex)),
            _ => null
        };
        if (src is null || tgt is null) { PlaceholderText.SetText(LabelTb, ""); return; }
        PlaceholderText.SetText(LabelTb, BindingDisplay.FormatAuto(src, tgt));
    }

    private void LoadFrom(Binding b)
    {
        LabelTb.Text = b.Label ?? string.Empty;
        switch (b.Source)
        {
            case MouseAxisSource ma:
                SourceKindCombo.SelectedIndex = 0; MouseAxisCombo.SelectedIndex = (int)ma.Axis; break;
            case MouseButtonSource mb:
                SourceKindCombo.SelectedIndex = 1; MouseButtonCombo.SelectedIndex = (int)mb.Button; break;
            case MouseScrollSource ms:
                SourceKindCombo.SelectedIndex = 2; MouseScrollCombo.SelectedIndex = (int)ms.Direction; break;
            case KeySource ks:
                SourceKindCombo.SelectedIndex = 3; KeyBox.CapturedKey = ks.Key; break;
        }
        switch (b.Target)
        {
            case StickAxisTarget sa:
                TargetKindCombo.SelectedIndex = 0;
                StickCombo.SelectedIndex = (int)sa.Stick;
                StickAxisCombo.SelectedIndex = (int)sa.Component;
                break;
            case TriggerTarget tt:
                TargetKindCombo.SelectedIndex = 1; TriggerCombo.SelectedIndex = (int)tt.Trigger; break;
            case ButtonTarget bt:
                TargetKindCombo.SelectedIndex = 2; ButtonCombo.SelectedIndex = (int)bt.Button; break;
            case DPadTarget dp:
                TargetKindCombo.SelectedIndex = 3; DPadCombo.SelectedIndex = (int)dp.Direction; break;
        }
        SensSlider.Value = b.Curve.Sensitivity;
        DzSlider.Value = b.Curve.InnerDeadzone;
        SatSlider.Value = b.Curve.OuterSaturation;
        ExpSlider.Value = b.Curve.Exponent;
        SuppressCb.IsChecked = b.SuppressInput;
        switch (b.StickModel)
        {
            case VelocityStickModel v:
                StickModelCombo.SelectedIndex = 0;
                ApplyStickModelLabels();
                Param1Tb.Text = v.DecayPerSecond.ToString("F2", CultureInfo.InvariantCulture);
                Param2Tb.Text = v.MaxVelocityCounts.ToString("F1", CultureInfo.InvariantCulture);
                break;
            case AccumulatorStickModel a:
                StickModelCombo.SelectedIndex = 1;
                ApplyStickModelLabels();
                Param1Tb.Text = a.SpringPerSecond.ToString("F2", CultureInfo.InvariantCulture);
                Param2Tb.Text = a.CountsPerFullDeflection.ToString("F1", CultureInfo.InvariantCulture);
                break;
            case PersistentStickModel p:
                StickModelCombo.SelectedIndex = 2;
                ApplyStickModelLabels();
                Param1Tb.Text = p.CountsPerFullDeflection.ToString("F1", CultureInfo.InvariantCulture);
                break;
            default:
                StickModelCombo.SelectedIndex = 0;
                ApplyStickModelLabels();
                break;
        }
        UpdateCurve();
    }

    private void UpdateSourceVisibility()
    {
        MouseAxisCombo.Visibility = SourceKindCombo.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        MouseButtonCombo.Visibility = SourceKindCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        MouseScrollCombo.Visibility = SourceKindCombo.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        KeyBox.Visibility = SourceKindCombo.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTargetVisibility()
    {
        StickPanel.Visibility = TargetKindCombo.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        TriggerCombo.Visibility = TargetKindCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        ButtonCombo.Visibility = TargetKindCombo.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        DPadCombo.Visibility = TargetKindCombo.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateStickModelVisibility()
    {
        var visible = TargetKindCombo.SelectedIndex == 0;
        StickModelCombo.IsEnabled = visible;
        Param1Tb.IsEnabled = visible;
        Param2Tb.IsEnabled = visible;
    }

    /// <summary>
    /// Fires whenever the stick-model dropdown changes. Updates the
    /// param-row labels, tooltips, row visibility, AND repopulates the
    /// param textboxes with mode-appropriate defaults. LoadFrom calls
    /// this implicitly via SelectedIndex assignment, then overwrites the
    /// textbox values with the persisted ones.
    /// </summary>
    private void OnStickModelChanged()
    {
        UpdateStickModelDefaults();
        ApplyStickModelLabels();
    }

    private void UpdateStickModelDefaults()
    {
        switch (StickModelCombo.SelectedIndex)
        {
            case 0: // Velocity (decay)
                Param1Tb.Text = "8.0";
                Param2Tb.Text = "800.0";
                break;
            case 1: // Accumulator (spring)
                Param1Tb.Text = "5.0";
                Param2Tb.Text = "400.0";
                break;
            case 2: // Persistent (no recenter)
                Param1Tb.Text = "400.0";
                break;
        }
    }

    private void ApplyStickModelLabels()
    {
        if (Param1Label is null || Param2Label is null || Param2Row is null || Param2Tb is null)
            return;

        const string ScaleAdvice =
            "With a default curve this behaves much like the Sensitivity slider above — usually you should leave this at the default and tune Sensitivity instead. Only change it if you know what you're doing (e.g. you want to shift where the inner deadzone or outer saturation falls in mouse-distance/speed terms).";

        switch (StickModelCombo.SelectedIndex)
        {
            case 0: // Velocity (decay)
                Param1Label.Text = "Decay /sec";
                Param1Label.ToolTip = new TooltipContent(
                    description: "How fast the stick returns to center when the mouse stops. Higher = snappier.",
                    typical: "5–15");
                Param2Label.Text = "Counts/sec → full";
                Param2Label.ToolTip = new TooltipContent(
                    description: "Internal scale: mouse counts per second that map to full stick deflection.",
                    advice: ScaleAdvice,
                    typical: "400–1200");
                Param2Row.Height = System.Windows.GridLength.Auto;
                Param2Tb.Visibility = System.Windows.Visibility.Visible;
                Param2Label.Visibility = System.Windows.Visibility.Visible;
                break;
            case 1: // Accumulator (spring)
                Param1Label.Text = "Spring /sec";
                Param1Label.ToolTip = new TooltipContent(
                    description: "How fast the stick springs back toward center each tick. Higher = stronger pull.",
                    typical: "2–10");
                Param2Label.Text = "Counts → full";
                Param2Label.ToolTip = new TooltipContent(
                    description: "Internal scale: how many mouse counts integrate to full stick deflection.",
                    advice: ScaleAdvice,
                    typical: "200–800");
                Param2Row.Height = System.Windows.GridLength.Auto;
                Param2Tb.Visibility = System.Windows.Visibility.Visible;
                Param2Label.Visibility = System.Windows.Visibility.Visible;
                break;
            case 2: // Persistent (no recenter)
                Param1Label.Text = "Counts → full";
                Param1Label.ToolTip = new TooltipContent(
                    description: "Internal scale: how many mouse counts integrate to full stick deflection. The stick stays where you put it; move the mouse back the same distance to recenter.",
                    advice: ScaleAdvice,
                    typical: "200–800");
                Param2Label.Visibility = System.Windows.Visibility.Collapsed;
                Param2Tb.Visibility = System.Windows.Visibility.Collapsed;
                Param2Row.Height = new System.Windows.GridLength(0);
                break;
        }
    }

    private void UpdateCurve()
    {
        if (CurveEditor is null) return;
        CurveEditor.Curve = new Curve(SensSlider.Value, DzSlider.Value, SatSlider.Value, ExpSlider.Value);
    }

    /// <summary>
    /// Set the Suppress checkbox to the sensible default for the current source kind.
    /// Mouse-axis: true (otherwise the cursor would fight the stick).
    /// Mouse-button / scroll / key: false (most users want capture-only by default;
    /// they can opt in if needed).
    /// Only applied when the user changes the source kind interactively after the
    /// dialog opened — pre-existing bindings keep their saved value via LoadFrom.
    /// </summary>
    private void UpdateSuppressDefault()
    {
        SuppressCb.IsChecked = SourceKindCombo.SelectedIndex == 0; // Mouse axis only
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        InputSource src = SourceKindCombo.SelectedIndex switch
        {
            0 => new MouseAxisSource((MouseAxis)Math.Max(0, MouseAxisCombo.SelectedIndex)),
            1 => new MouseButtonSource((MouseButton)Math.Max(0, MouseButtonCombo.SelectedIndex)),
            2 => new MouseScrollSource((ScrollDirection)Math.Max(0, MouseScrollCombo.SelectedIndex)),
            3 => new KeySource(KeyBox.CapturedKey.IsNone ? new VirtualKey(0, false) : KeyBox.CapturedKey),
            _ => throw new InvalidOperationException()
        };
        OutputTarget tgt = TargetKindCombo.SelectedIndex switch
        {
            0 => new StickAxisTarget((Stick)Math.Max(0, StickCombo.SelectedIndex), (AxisComponent)Math.Max(0, StickAxisCombo.SelectedIndex)),
            1 => new TriggerTarget((Mouse2Joy.Persistence.Models.Trigger)Math.Max(0, TriggerCombo.SelectedIndex)),
            2 => new ButtonTarget((GamepadButton)Math.Max(0, ButtonCombo.SelectedIndex)),
            3 => new DPadTarget((DPadDirection)Math.Max(0, DPadCombo.SelectedIndex)),
            _ => throw new InvalidOperationException()
        };
        if (src is KeySource ks && ks.Key.IsNone)
        {
            MessageBox.Show(this, "Please press a key in the source field.", "Mouse2Joy", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var curve = new Curve(SensSlider.Value, DzSlider.Value, SatSlider.Value, ExpSlider.Value);
        StickModel? sm = null;
        if (tgt is StickAxisTarget)
        {
            var p1 = ParseDouble(Param1Tb.Text, 8.0);
            var p2 = ParseDouble(Param2Tb.Text, 800.0);
            sm = StickModelCombo.SelectedIndex switch
            {
                0 => new VelocityStickModel(p1, p2),
                1 => new AccumulatorStickModel(p1, p2),
                2 => new PersistentStickModel(p1),
                _ => new VelocityStickModel(p1, p2),
            };
        }
        var label = string.IsNullOrWhiteSpace(LabelTb.Text) ? null : LabelTb.Text.Trim();
        Result = new Binding { Source = src, Target = tgt, Curve = curve, StickModel = sm, Label = label, SuppressInput = SuppressCb.IsChecked == true };
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static double ParseDouble(string s, double fallback)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
