using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.UI.ViewModels.Editor;

/// <summary>
/// Minimal ICommand implementation used by proxy view-models that need to
/// bind a button click to an action (e.g. opening a popout window).
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Per-modifier-kind adapter that surfaces mutable params for two-way XAML
/// bindings. Wraps a <see cref="ModifierCardViewModel"/> and writes a new
/// immutable record back to it on every change.
///
/// One proxy class per modifier kind that has tunable params. They're built
/// on-demand by the param templates' DataTemplate bindings.
/// </summary>
public abstract class ModifierParamProxy : INotifyPropertyChanged
{
    protected readonly ModifierCardViewModel Card;

    protected ModifierParamProxy(ModifierCardViewModel card)
    {
        Card = card;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnChanged([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

public sealed class StickDynamicsProxy : ModifierParamProxy
{
    public StickDynamicsProxy(ModifierCardViewModel card) : base(card) { }

    private StickDynamicsModifier Mod => (StickDynamicsModifier)Card.Modifier;

    public StickDynamicsMode Mode
    {
        get => Mod.Mode;
        set
        {
            if (Mod.Mode != value)
            {
                Card.Update(Mod with { Mode = value });
                OnChanged();
                OnChanged(nameof(Param1Label));
                OnChanged(nameof(Param2Label));
                OnChanged(nameof(Param2Visible));
            }
        }
    }

    public double Param1
    {
        get => Mod.Param1;
        set { if (Mod.Param1 != value) { Card.Update(Mod with { Param1 = value }); OnChanged(); } }
    }

    public double Param2
    {
        get => Mod.Param2;
        set { if (Mod.Param2 != value) { Card.Update(Mod with { Param2 = value }); OnChanged(); } }
    }

    public string Param1Label => Mode switch
    {
        StickDynamicsMode.Velocity => "Decay /sec",
        StickDynamicsMode.Accumulator => "Spring /sec",
        StickDynamicsMode.Persistent => "Counts → full",
        _ => "Param 1"
    };

    public string Param2Label => Mode switch
    {
        StickDynamicsMode.Velocity => "Counts/sec → full",
        StickDynamicsMode.Accumulator => "Counts → full",
        _ => string.Empty
    };

    public bool Param2Visible => Mode != StickDynamicsMode.Persistent;
}

public sealed class DigitalToScalarProxy : ModifierParamProxy
{
    public DigitalToScalarProxy(ModifierCardViewModel card) : base(card) { }
    private DigitalToScalarModifier Mod => (DigitalToScalarModifier)Card.Modifier;

    public double OnValue
    {
        get => Mod.OnValue;
        set { if (Mod.OnValue != value) { Card.Update(Mod with { OnValue = value }); OnChanged(); } }
    }
    public double OffValue
    {
        get => Mod.OffValue;
        set { if (Mod.OffValue != value) { Card.Update(Mod with { OffValue = value }); OnChanged(); } }
    }
}

public sealed class ScalarToDigitalThresholdProxy : ModifierParamProxy
{
    public ScalarToDigitalThresholdProxy(ModifierCardViewModel card) : base(card) { }
    private ScalarToDigitalThresholdModifier Mod => (ScalarToDigitalThresholdModifier)Card.Modifier;

    public double Threshold
    {
        get => Mod.Threshold;
        set { if (Mod.Threshold != value) { Card.Update(Mod with { Threshold = value }); OnChanged(); } }
    }
}

public sealed class DeltaScaleProxy : ModifierParamProxy
{
    public DeltaScaleProxy(ModifierCardViewModel card) : base(card) { }
    private DeltaScaleModifier Mod => (DeltaScaleModifier)Card.Modifier;

    public double Factor
    {
        get => Mod.Factor;
        set { if (Mod.Factor != value) { Card.Update(Mod with { Factor = value }); OnChanged(); } }
    }
}

public sealed class OutputScaleProxy : ModifierParamProxy
{
    public OutputScaleProxy(ModifierCardViewModel card) : base(card) { }
    private OutputScaleModifier Mod => (OutputScaleModifier)Card.Modifier;

    public double Factor
    {
        get => Mod.Factor;
        set { if (Mod.Factor != value) { Card.Update(Mod with { Factor = value }); OnChanged(); } }
    }
}

public sealed class InnerDeadzoneProxy : ModifierParamProxy
{
    public InnerDeadzoneProxy(ModifierCardViewModel card) : base(card) { }
    private InnerDeadzoneModifier Mod => (InnerDeadzoneModifier)Card.Modifier;

    public double Threshold
    {
        get => Mod.Threshold;
        set { if (Mod.Threshold != value) { Card.Update(Mod with { Threshold = value }); OnChanged(); } }
    }
}

public sealed class OuterSaturationProxy : ModifierParamProxy
{
    public OuterSaturationProxy(ModifierCardViewModel card) : base(card) { }
    private OuterSaturationModifier Mod => (OuterSaturationModifier)Card.Modifier;

    public double Threshold
    {
        get => Mod.Threshold;
        set { if (Mod.Threshold != value) { Card.Update(Mod with { Threshold = value }); OnChanged(); } }
    }
}

public sealed class ResponseCurveProxy : ModifierParamProxy
{
    public ResponseCurveProxy(ModifierCardViewModel card) : base(card) { }
    private ResponseCurveModifier Mod => (ResponseCurveModifier)Card.Modifier;

    public double Exponent
    {
        get => Mod.Exponent;
        set { if (Mod.Exponent != value) { Card.Update(Mod with { Exponent = value }); OnChanged(); } }
    }
}

public sealed class SegmentedResponseCurveProxy : ModifierParamProxy
{
    public SegmentedResponseCurveProxy(ModifierCardViewModel card) : base(card) { }
    private SegmentedResponseCurveModifier Mod => (SegmentedResponseCurveModifier)Card.Modifier;

    public double Threshold
    {
        get => Mod.Threshold;
        set { if (Mod.Threshold != value) { Card.Update(Mod with { Threshold = value }); OnChanged(); } }
    }

    public double Exponent
    {
        get => Mod.Exponent;
        set { if (Mod.Exponent != value) { Card.Update(Mod with { Exponent = value }); OnChanged(); } }
    }

    public SegmentedCurveRegion Region
    {
        get => Mod.Region;
        set { if (Mod.Region != value) { Card.Update(Mod with { Region = value }); OnChanged(); } }
    }

    public SegmentedCurveTransitionStyle TransitionStyle
    {
        get => Mod.TransitionStyle;
        set { if (Mod.TransitionStyle != value) { Card.Update(Mod with { TransitionStyle = value }); OnChanged(); } }
    }

    public SegmentedCurveShape Shape
    {
        get => Mod.Shape;
        set { if (Mod.Shape != value) { Card.Update(Mod with { Shape = value }); OnChanged(); } }
    }
}

public sealed class RampUpProxy : ModifierParamProxy
{
    public RampUpProxy(ModifierCardViewModel card) : base(card) { }
    private RampUpModifier Mod => (RampUpModifier)Card.Modifier;

    public double SecondsToFull
    {
        get => Mod.SecondsToFull;
        set { if (Mod.SecondsToFull != value) { Card.Update(Mod with { SecondsToFull = value }); OnChanged(); } }
    }
}

public sealed class RampDownProxy : ModifierParamProxy
{
    public RampDownProxy(ModifierCardViewModel card) : base(card) { }
    private RampDownModifier Mod => (RampDownModifier)Card.Modifier;

    public double SecondsFromFull
    {
        get => Mod.SecondsFromFull;
        set { if (Mod.SecondsFromFull != value) { Card.Update(Mod with { SecondsFromFull = value }); OnChanged(); } }
    }
}

public sealed class LimiterProxy : ModifierParamProxy
{
    public LimiterProxy(ModifierCardViewModel card) : base(card) { }
    private LimiterModifier Mod => (LimiterModifier)Card.Modifier;

    public double MaxPositive
    {
        get => Mod.MaxPositive;
        set { if (Mod.MaxPositive != value) { Card.Update(Mod with { MaxPositive = value }); OnChanged(); } }
    }
    public double MaxNegative
    {
        get => Mod.MaxNegative;
        set { if (Mod.MaxNegative != value) { Card.Update(Mod with { MaxNegative = value }); OnChanged(); } }
    }
}

public sealed class SmoothingProxy : ModifierParamProxy
{
    public SmoothingProxy(ModifierCardViewModel card) : base(card) { }
    private SmoothingModifier Mod => (SmoothingModifier)Card.Modifier;

    public double TimeConstantSeconds
    {
        get => Mod.TimeConstantSeconds;
        set { if (Mod.TimeConstantSeconds != value) { Card.Update(Mod with { TimeConstantSeconds = value }); OnChanged(); } }
    }
}

public sealed class AutoFireProxy : ModifierParamProxy
{
    public AutoFireProxy(ModifierCardViewModel card) : base(card) { }
    private AutoFireModifier Mod => (AutoFireModifier)Card.Modifier;

    public double Hz
    {
        get => Mod.Hz;
        set { if (Mod.Hz != value) { Card.Update(Mod with { Hz = value }); OnChanged(); } }
    }
}

public sealed class HoldToActivateProxy : ModifierParamProxy
{
    public HoldToActivateProxy(ModifierCardViewModel card) : base(card) { }
    private HoldToActivateModifier Mod => (HoldToActivateModifier)Card.Modifier;

    public double HoldSeconds
    {
        get => Mod.HoldSeconds;
        set { if (Mod.HoldSeconds != value) { Card.Update(Mod with { HoldSeconds = value }); OnChanged(); } }
    }
}

public sealed class TapProxy : ModifierParamProxy
{
    public TapProxy(ModifierCardViewModel card) : base(card) { }
    private TapModifier Mod => (TapModifier)Card.Modifier;

    public double MaxHoldSeconds
    {
        get => Mod.MaxHoldSeconds;
        set { if (Mod.MaxHoldSeconds != value) { Card.Update(Mod with { MaxHoldSeconds = value }); OnChanged(); } }
    }
    public double PulseSeconds
    {
        get => Mod.PulseSeconds;
        set { if (Mod.PulseSeconds != value) { Card.Update(Mod with { PulseSeconds = value }); OnChanged(); } }
    }
    public bool WaitForHigherTaps
    {
        get => Mod.WaitForHigherTaps;
        set
        {
            if (Mod.WaitForHigherTaps != value)
            {
                Card.Update(Mod with { WaitForHigherTaps = value });
                OnChanged();
                OnChanged(nameof(ConfirmWaitVisible));
            }
        }
    }
    public double ConfirmWaitSeconds
    {
        get => Mod.ConfirmWaitSeconds;
        set { if (Mod.ConfirmWaitSeconds != value) { Card.Update(Mod with { ConfirmWaitSeconds = value }); OnChanged(); } }
    }
    public bool ConfirmWaitVisible => Mod.WaitForHigherTaps;
}

public sealed class MultiTapProxy : ModifierParamProxy
{
    public MultiTapProxy(ModifierCardViewModel card) : base(card) { }
    private MultiTapModifier Mod => (MultiTapModifier)Card.Modifier;

    public int TapCount
    {
        get => Mod.TapCount;
        set
        {
            var clamped = value < 1 ? 1 : value;
            if (Mod.TapCount != clamped)
            {
                Card.Update(Mod with { TapCount = clamped });
                OnChanged();
            }
        }
    }
    public double WindowSeconds
    {
        get => Mod.WindowSeconds;
        set { if (Mod.WindowSeconds != value) { Card.Update(Mod with { WindowSeconds = value }); OnChanged(); } }
    }
    public double MaxHoldSeconds
    {
        get => Mod.MaxHoldSeconds;
        set { if (Mod.MaxHoldSeconds != value) { Card.Update(Mod with { MaxHoldSeconds = value }); OnChanged(); } }
    }
    public double PulseSeconds
    {
        get => Mod.PulseSeconds;
        set { if (Mod.PulseSeconds != value) { Card.Update(Mod with { PulseSeconds = value }); OnChanged(); } }
    }
    public bool WaitForHigherTaps
    {
        get => Mod.WaitForHigherTaps;
        set { if (Mod.WaitForHigherTaps != value) { Card.Update(Mod with { WaitForHigherTaps = value }); OnChanged(); } }
    }
}

public sealed class WaitForTapResolutionProxy : ModifierParamProxy
{
    public WaitForTapResolutionProxy(ModifierCardViewModel card) : base(card) { }
    private WaitForTapResolutionModifier Mod => (WaitForTapResolutionModifier)Card.Modifier;

    public double MaxHoldSeconds
    {
        get => Mod.MaxHoldSeconds;
        set { if (Mod.MaxHoldSeconds != value) { Card.Update(Mod with { MaxHoldSeconds = value }); OnChanged(); } }
    }
    public double WaitSeconds
    {
        get => Mod.WaitSeconds;
        set { if (Mod.WaitSeconds != value) { Card.Update(Mod with { WaitSeconds = value }); OnChanged(); } }
    }
    public double PulseSeconds
    {
        get => Mod.PulseSeconds;
        set { if (Mod.PulseSeconds != value) { Card.Update(Mod with { PulseSeconds = value }); OnChanged(); } }
    }
}

/// <summary>
/// One row in the <see cref="ParametricCurveProxy"/>'s editable point list.
/// Holds X and Y as INotifyPropertyChanged so individual textbox/slider
/// edits trigger updates. The proxy subscribes to PropertyChanged on each
/// row and writes a new <see cref="ParametricCurveModifier"/> on every edit.
/// </summary>
public sealed class CurvePointRow : INotifyPropertyChanged
{
    private double _x;
    private double _y;

    public int Index { get; init; }

    public double X
    {
        get => _x;
        set { if (_x != value) { _x = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(X))); } }
    }

    public double Y
    {
        get => _y;
        set { if (_y != value) { _y = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Y))); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Setter that bypasses change notification — used by the
    /// proxy during sync-from-mod so we don't fire feedback loops.</summary>
    internal void SetSilent(double x, double y) { _x = x; _y = y; }
}

public sealed class ParametricCurveProxy : ModifierParamProxy
{
    private const int MinPointCount = 2;
    private const int MaxPointCount = 7;

    private bool _suppressRowEvents;

    public ParametricCurveProxy(ModifierCardViewModel card) : base(card)
    {
        PointRows = new ObservableCollection<CurvePointRow>();
        SyncRowsFromMod();
    }

    private ParametricCurveModifier Mod => (ParametricCurveModifier)Card.Modifier;

    public ObservableCollection<CurvePointRow> PointRows { get; }

    public bool Symmetric
    {
        get => Mod.Symmetric;
        set
        {
            if (Mod.Symmetric != value)
            {
                Card.Update(Mod with { Symmetric = value });
                OnChanged();
            }
        }
    }

    public int PointCount
    {
        get => Mod.Points.Count;
        set
        {
            var clamped = value;
            if (clamped < MinPointCount)
            {
                clamped = MinPointCount;
            }

            if (clamped > MaxPointCount)
            {
                clamped = MaxPointCount;
            }

            if (clamped == Mod.Points.Count)
            {
                return;
            }

            var newPoints = ResamplePointsTo(Mod.Points, clamped);
            Card.Update(Mod with { Points = newPoints });
            SyncRowsFromMod();
            OnChanged();
        }
    }

    private void SyncRowsFromMod()
    {
        _suppressRowEvents = true;
        try
        {
            // Detach handlers from existing rows.
            foreach (var oldRow in PointRows)
            {
                oldRow.PropertyChanged -= OnRowChanged;
            }

            PointRows.Clear();

            for (int i = 0; i < Mod.Points.Count; i++)
            {
                var p = Mod.Points[i];
                var row = new CurvePointRow { Index = i };
                row.SetSilent(p.X, p.Y);
                row.PropertyChanged += OnRowChanged;
                PointRows.Add(row);
            }
        }
        finally
        {
            _suppressRowEvents = false;
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressRowEvents)
        {
            return;
        }

        if (sender is not CurvePointRow row)
        {
            return;
        }

        var pts = Mod.Points.ToArray();
        if (row.Index < 0 || row.Index >= pts.Length)
        {
            return;
        }

        pts[row.Index] = new CurvePoint(row.X, row.Y);
        Card.Update(Mod with { Points = pts });
    }

    /// <summary>
    /// Resample to <paramref name="newCount"/> points by evaluating the
    /// current spline at evenly-spaced X positions. Preserves visual shape
    /// when growing or shrinking the point count. X range = [0, 1] in
    /// symmetric mode, [-1, 1] in full-range mode.
    /// </summary>
    private CurvePoint[] ResamplePointsTo(IReadOnlyList<CurvePoint> src, int newCount)
    {
        var xMin = Mod.Symmetric ? 0.0 : -1.0;
        var xMax = 1.0;
        var step = (xMax - xMin) / (newCount - 1);

        // Linear interpolation through the existing points for resampling.
        // We don't have access to the engine's Fritsch-Carlson here (the
        // proxy lives in UI, not Engine), and importing the math would
        // create a layering inversion. Linear is fine for resampling — the
        // user sees the result and can adjust further.
        var srcSorted = src.OrderBy(p => p.X).ToArray();
        var result = new CurvePoint[newCount];
        for (int i = 0; i < newCount; i++)
        {
            var x = xMin + step * i;
            if (i == newCount - 1)
            {
                x = xMax;  // exact endpoint
            }

            result[i] = new CurvePoint(x, EvaluateLinear(srcSorted, x));
        }
        return result;
    }

    private static double EvaluateLinear(CurvePoint[] sorted, double x)
    {
        if (sorted.Length == 0)
        {
            return x;
        }

        if (sorted.Length == 1)
        {
            return sorted[0].Y;
        }

        if (x <= sorted[0].X)
        {
            return sorted[0].Y;
        }

        if (x >= sorted[^1].X)
        {
            return sorted[^1].Y;
        }

        for (int i = 0; i < sorted.Length - 1; i++)
        {
            if (x <= sorted[i + 1].X)
            {
                var t = (x - sorted[i].X) / (sorted[i + 1].X - sorted[i].X);
                return sorted[i].Y + t * (sorted[i + 1].Y - sorted[i].Y);
            }
        }
        return sorted[^1].Y;
    }
}

/// <summary>
/// Proxy for <see cref="CurveEditorModifier"/>. The param panel renders just
/// a hint + an "Edit Curve..." button; all real editing happens in a popout
/// window opened via <see cref="OpenEditorCommand"/>.
/// </summary>
public sealed class CurveEditorProxy : ModifierParamProxy
{
    public CurveEditorProxy(ModifierCardViewModel card) : base(card)
    {
        OpenEditorCommand = new RelayCommand(OpenEditor);
    }

    public RelayCommand OpenEditorCommand { get; }

    private void OpenEditor()
    {
        var window = new Views.Editor.CurveEditorWindow(Card)
        {
            Owner = System.Windows.Application.Current?.Windows.OfType<System.Windows.Window>()
                .FirstOrDefault(w => w.IsActive),
        };
        window.ShowDialog();
    }
}
