using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.UI.ViewModels.Editor;

/// <summary>
/// Source of "what modifiers can the user add?" for the +Add Modifier
/// dropdown. Returns one entry per kind in v1, each with a preconfigured
/// default instance the editor can clone. The catalog is intentionally
/// type-agnostic — the editor's filter logic decides which entries are
/// applicable to the current chain tail; this just enumerates them.
/// </summary>
public static class ModifierCatalog
{
    public sealed record Entry(string Name, Func<Modifier> Create);

    public static IReadOnlyList<Entry> AllEntries { get; } = new Entry[]
    {
        new("Stick Dynamics — Velocity",     () => StickDynamicsModifier.DefaultVelocity),
        new("Stick Dynamics — Accumulator",  () => StickDynamicsModifier.DefaultAccumulator),
        new("Stick Dynamics — Persistent",   () => StickDynamicsModifier.DefaultPersistent),
        new("Digital → Scalar",              () => DigitalToScalarModifier.Default),
        new("Threshold (Scalar → Digital)",  () => ScalarToDigitalThresholdModifier.Default),
        new("Sensitivity",                   () => SensitivityModifier.Default),
        new("Inner Deadzone",                () => InnerDeadzoneModifier.Default),
        new("Outer Saturation",              () => OuterSaturationModifier.Default),
        new("Response Curve",                () => ResponseCurveModifier.Default),
        new("Invert",                        () => new InvertModifier()),
        new("Ramp Up",                       () => RampUpModifier.Default),
        new("Ramp Down",                     () => RampDownModifier.Default),
        new("Limiter",                       () => LimiterModifier.Default),
        new("Smoothing",                     () => SmoothingModifier.Default),
        new("Toggle",                        () => new ToggleModifier()),
        new("Auto-fire",                     () => AutoFireModifier.Default),
        new("Hold to Activate",              () => HoldToActivateModifier.Default),
        new("Tap",                           () => TapModifier.Default),
        new("Multi-tap",                     () => MultiTapModifier.Default),
        new("Wait for Tap Resolution",       () => WaitForTapResolutionModifier.Default),
    };
}
