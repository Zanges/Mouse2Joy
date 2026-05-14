using System.ComponentModel;
using System.Windows;
using Mouse2Joy.Persistence.Models;
using Mouse2Joy.UI.ViewModels.Editor;

namespace Mouse2Joy.UI.Views.Editor;

public partial class CurveEditorWindow : Window
{
    public CurveEditorWindow(ModifierCardViewModel card)
    {
        InitializeComponent();
        DataContext = new CurveEditorWindowViewModel(card);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

/// <summary>
/// Lightweight view-model for the curve editor popout. Wraps a
/// <see cref="ModifierCardViewModel"/> backing a <see cref="CurveEditorModifier"/>
/// and exposes its fields as two-way-bindable properties. Every set writes
/// back to the card via <c>Card.Update(Mod with ...)</c>, just like other
/// modifier proxies do.
/// </summary>
internal sealed class CurveEditorWindowViewModel : INotifyPropertyChanged
{
    private const int MinPointCount = 2;
    private const int MaxPointCount = 7;

    private readonly ModifierCardViewModel _card;

    public CurveEditorWindowViewModel(ModifierCardViewModel card)
    {
        _card = card;
    }

    private CurveEditorModifier Mod => (CurveEditorModifier)_card.Modifier;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public IReadOnlyList<CurvePoint> Points
    {
        get => Mod.Points;
        set
        {
            if (!ReferenceEquals(value, Mod.Points))
            {
                _card.Update(Mod with { Points = value });
                OnChanged(nameof(Points));
                OnChanged(nameof(PointCount));
            }
        }
    }

    public bool Symmetric
    {
        get => Mod.Symmetric;
        set
        {
            if (Mod.Symmetric != value)
            {
                _card.Update(Mod with { Symmetric = value });
                OnChanged(nameof(Symmetric));
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

            var newPoints = ResamplePointsTo(Mod.Points, clamped, Mod.Symmetric);
            _card.Update(Mod with { Points = newPoints });
            OnChanged(nameof(PointCount));
            OnChanged(nameof(Points));
        }
    }

    /// <summary>
    /// Linear resampling along the existing curve to a new point count.
    /// Same approach as <see cref="ParametricCurveProxy"/>'s resampler:
    /// preserves visual shape while changing the number of control points.
    /// </summary>
    private static CurvePoint[] ResamplePointsTo(IReadOnlyList<CurvePoint> src, int newCount, bool symmetric)
    {
        var xMin = symmetric ? 0.0 : -1.0;
        var xMax = 1.0;
        var step = (xMax - xMin) / (newCount - 1);
        var sorted = src.OrderBy(p => p.X).ToArray();
        var result = new CurvePoint[newCount];
        for (int i = 0; i < newCount; i++)
        {
            var x = xMin + step * i;
            if (i == newCount - 1)
            {
                x = xMax;
            }

            result[i] = new CurvePoint(x, EvaluateLinear(sorted, x));
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
