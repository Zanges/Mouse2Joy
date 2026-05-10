using Mouse2Joy.Engine.Modifiers.Evaluators;
using Mouse2Joy.Engine.Modifiers.SourceAdapters;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Modifiers;

public static class ChainBuilder
{
    public static ISourceAdapter BuildAdapter(InputSource source) => source switch
    {
        MouseAxisSource ma => new MouseAxisAdapter(ma),
        MouseButtonSource or KeySource => new DigitalLatchAdapter(source),
        MouseScrollSource ms => new DigitalMomentaryAdapter(ms),
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown source kind.")
    };

    public static IModifierEvaluator BuildEvaluator(Modifier modifier) => modifier switch
    {
        StickDynamicsModifier sd => new StickDynamicsEvaluator(sd),
        DigitalToScalarModifier d2s => new DigitalToScalarEvaluator(d2s),
        ScalarToDigitalThresholdModifier s2d => new ScalarToDigitalThresholdEvaluator(s2d),
        SensitivityModifier s => new SensitivityEvaluator(s),
        InnerDeadzoneModifier id => new InnerDeadzoneEvaluator(id),
        OuterSaturationModifier os => new OuterSaturationEvaluator(os),
        ResponseCurveModifier rc => new ResponseCurveEvaluator(rc),
        InvertModifier i => new InvertEvaluator(i),
        RampUpModifier ru => new RampUpEvaluator(ru),
        RampDownModifier rd => new RampDownEvaluator(rd),
        LimiterModifier lim => new LimiterEvaluator(lim),
        ToggleModifier tog => new ToggleEvaluator(tog),
        SmoothingModifier sm => new SmoothingEvaluator(sm),
        AutoFireModifier af => new AutoFireEvaluator(af),
        HoldToActivateModifier hold => new HoldToActivateEvaluator(hold),
        TapModifier tap => new TapEvaluator(tap),
        MultiTapModifier mt => new MultiTapEvaluator(mt),
        WaitForTapResolutionModifier wtr => new WaitForTapResolutionEvaluator(wtr),
        _ => throw new ArgumentOutOfRangeException(nameof(modifier), modifier, "Unknown modifier kind.")
    };
}
