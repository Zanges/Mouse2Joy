using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Persistence;

/// <summary>
/// Static metadata about modifier and source/target signal types. Lives in
/// Persistence so both the engine (chain construction, validation) and the UI
/// (editor validation, catalog filtering) can consume it without cross-deps.
/// </summary>
public static class ModifierTypes
{
    /// <summary>The signal type a source emits when its raw events are accumulated within a tick.</summary>
    public static SignalType GetSourceOutputType(InputSource source) => source switch
    {
        MouseAxisSource => SignalType.Delta,
        MouseButtonSource => SignalType.Digital,
        KeySource => SignalType.Digital,
        MouseScrollSource => SignalType.Digital,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown source kind.")
    };

    /// <summary>The signal type a target consumes.</summary>
    public static SignalType GetTargetInputType(OutputTarget target) => target switch
    {
        StickAxisTarget => SignalType.Scalar,
        TriggerTarget => SignalType.Scalar,
        ButtonTarget => SignalType.Digital,
        DPadTarget => SignalType.Digital,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown target kind.")
    };

    /// <summary>
    /// The accepted-input and produced-output signal types of a modifier.
    /// Pure metadata — independent of the modifier's parameter values or
    /// Enabled flag (a disabled modifier is still typed, so it cannot turn a
    /// previously valid chain invalid).
    /// </summary>
    public static (SignalType In, SignalType Out) GetIO(Modifier modifier) => modifier switch
    {
        StickDynamicsModifier => (SignalType.Delta, SignalType.Scalar),
        DigitalToScalarModifier => (SignalType.Digital, SignalType.Scalar),
        ScalarToDigitalThresholdModifier => (SignalType.Scalar, SignalType.Digital),
        SensitivityModifier => (SignalType.Scalar, SignalType.Scalar),
        InnerDeadzoneModifier => (SignalType.Scalar, SignalType.Scalar),
        OuterSaturationModifier => (SignalType.Scalar, SignalType.Scalar),
        ResponseCurveModifier => (SignalType.Scalar, SignalType.Scalar),
        InvertModifier => (SignalType.Scalar, SignalType.Scalar),
        RampUpModifier => (SignalType.Scalar, SignalType.Scalar),
        RampDownModifier => (SignalType.Scalar, SignalType.Scalar),
        LimiterModifier => (SignalType.Scalar, SignalType.Scalar),
        SmoothingModifier => (SignalType.Scalar, SignalType.Scalar),
        ToggleModifier => (SignalType.Digital, SignalType.Digital),
        AutoFireModifier => (SignalType.Digital, SignalType.Digital),
        HoldToActivateModifier => (SignalType.Digital, SignalType.Digital),
        TapModifier => (SignalType.Digital, SignalType.Digital),
        MultiTapModifier => (SignalType.Digital, SignalType.Digital),
        WaitForTapResolutionModifier => (SignalType.Digital, SignalType.Digital),
        _ => throw new ArgumentOutOfRangeException(nameof(modifier), modifier, "Unknown modifier kind.")
    };

    /// <summary>Human-friendly display name for a modifier kind. Used by the editor catalog and chain list.</summary>
    public static string GetDisplayName(Modifier modifier) => modifier switch
    {
        StickDynamicsModifier sd => $"Stick Dynamics ({sd.Mode})",
        DigitalToScalarModifier => "Digital → Scalar",
        ScalarToDigitalThresholdModifier => "Threshold (Scalar → Digital)",
        SensitivityModifier => "Sensitivity",
        InnerDeadzoneModifier => "Inner Deadzone",
        OuterSaturationModifier => "Outer Saturation",
        ResponseCurveModifier => "Response Curve",
        InvertModifier => "Invert",
        RampUpModifier => "Ramp Up",
        RampDownModifier => "Ramp Down",
        LimiterModifier => "Limiter",
        ToggleModifier => "Toggle",
        SmoothingModifier => "Smoothing",
        AutoFireModifier => "Auto-fire",
        HoldToActivateModifier => "Hold to Activate",
        TapModifier => "Tap",
        MultiTapModifier mt => $"Multi-tap ({mt.TapCount}×)",
        WaitForTapResolutionModifier => "Wait for Tap Resolution",
        _ => modifier.GetType().Name
    };
}
