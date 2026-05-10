using System.ComponentModel;
using System.Runtime.CompilerServices;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.UI.ViewModels.Editor;

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

public sealed class SensitivityProxy : ModifierParamProxy
{
    public SensitivityProxy(ModifierCardViewModel card) : base(card) { }
    private SensitivityModifier Mod => (SensitivityModifier)Card.Modifier;

    public double Multiplier
    {
        get => Mod.Multiplier;
        set { if (Mod.Multiplier != value) { Card.Update(Mod with { Multiplier = value }); OnChanged(); } }
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
