using System.Text.Json.Serialization;

namespace Mouse2Joy.Persistence.Models;

/// <summary>
/// One step in a binding's chain. Modifiers are immutable records with a
/// configurable Enabled flag (disabled = passthrough but still type-checked).
///
/// Polymorphism uses the same "$kind" discriminator as InputSource and
/// OutputTarget so callers can treat modifiers uniformly with other binding
/// shapes.
///
/// All modifiers are sealed records to make value-equality reliable for the
/// engine's "preserve state when chain unchanged" cache eviction.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(DeltaScaleModifier), "deltaScale")]
[JsonDerivedType(typeof(StickDynamicsModifier), "stickDynamics")]
[JsonDerivedType(typeof(DigitalToScalarModifier), "digitalToScalar")]
[JsonDerivedType(typeof(ScalarToDigitalThresholdModifier), "scalarToDigitalThreshold")]
[JsonDerivedType(typeof(OutputScaleModifier), "outputScale")]
[JsonDerivedType(typeof(InnerDeadzoneModifier), "innerDeadzone")]
[JsonDerivedType(typeof(OuterSaturationModifier), "outerSaturation")]
[JsonDerivedType(typeof(ResponseCurveModifier), "responseCurve")]
[JsonDerivedType(typeof(SegmentedResponseCurveModifier), "segmentedResponseCurve")]
[JsonDerivedType(typeof(ParametricCurveModifier), "parametricCurve")]
[JsonDerivedType(typeof(CurveEditorModifier), "curveEditor")]
[JsonDerivedType(typeof(InvertModifier), "invert")]
[JsonDerivedType(typeof(RampUpModifier), "rampUp")]
[JsonDerivedType(typeof(RampDownModifier), "rampDown")]
[JsonDerivedType(typeof(LimiterModifier), "limiter")]
[JsonDerivedType(typeof(ToggleModifier), "toggle")]
[JsonDerivedType(typeof(SmoothingModifier), "smoothing")]
[JsonDerivedType(typeof(AutoFireModifier), "autoFire")]
[JsonDerivedType(typeof(HoldToActivateModifier), "holdToActivate")]
[JsonDerivedType(typeof(TapModifier), "tap")]
[JsonDerivedType(typeof(MultiTapModifier), "multiTap")]
[JsonDerivedType(typeof(WaitForTapResolutionModifier), "waitForTapResolution")]
public abstract record Modifier
{
    /// <summary>
    /// When false, this modifier acts as a typed passthrough — input is
    /// returned verbatim. Type validation still applies, so disabling cannot
    /// turn a previously valid chain invalid.
    /// </summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// Multiplies the Delta signal by Factor before integration. Use this to
/// control "how much mouse motion is needed to fill the stick" — Factor &lt; 1
/// requires more motion (less sensitive), Factor &gt; 1 requires less motion
/// (more sensitive). Full deflection is still reachable in either case
/// because <see cref="StickDynamicsModifier"/>'s internal clamp at ±1 takes
/// over once enough motion accumulates.
///
/// <para>Contrast with <see cref="OutputScaleModifier"/> which acts after the
/// integrator and caps maximum output: those are different operations and
/// live at different chain positions (Delta-side vs Scalar-side).</para>
///
/// <para>Factor is clamped to ≥ 0 in the evaluator (the stored value round-
/// trips unchanged). Negative factors (input inversion) are out of scope —
/// use a dedicated invert modifier if added later.</para>
/// </summary>
/// <param name="Factor">Multiplier for the Delta signal; ≥ 0. Default 1.0 (passthrough).</param>
public sealed record DeltaScaleModifier(double Factor) : Modifier
{
    public static DeltaScaleModifier Default => new(1.0);
}

/// <summary>The Velocity / Accumulator / Persistent modes for the StickDynamics modifier.</summary>
public enum StickDynamicsMode
{
    /// <summary>Exponential decay of velocity toward 0. Mouse-velocity-driven; best for "joystick from mouse motion" feel.</summary>
    Velocity,

    /// <summary>Spring-recenter of an integrated position. Mouse-delta-driven; best for "displace and return" feel.</summary>
    Accumulator,

    /// <summary>No auto-recenter. Stick stays at the integrated position; user moves mouse the same distance back to recenter.</summary>
    Persistent
}

/// <summary>
/// Converts a stream of signed mouse-count deltas into a Scalar deflection.
/// Required for any chain that wires a mouse-axis source to a Scalar target.
/// Stateful: holds velocity / position state across ticks.
/// </summary>
/// <param name="Mode">Which integration model to apply.</param>
/// <param name="Param1">
/// Mode-dependent first parameter:
///   Velocity     → DecayPerSecond     (exponential decay rate)
///   Accumulator  → SpringPerSecond    (spring-back rate)
///   Persistent   → CountsPerFullDeflection
/// </param>
/// <param name="Param2">
/// Mode-dependent second parameter (ignored for Persistent):
///   Velocity     → MaxVelocityCounts        (mouse counts/sec mapping to full deflection)
///   Accumulator  → CountsPerFullDeflection  (mouse counts integrated to full deflection)
/// </param>
public sealed record StickDynamicsModifier(StickDynamicsMode Mode, double Param1, double Param2) : Modifier
{
    public static StickDynamicsModifier DefaultVelocity => new(StickDynamicsMode.Velocity, 8.0, 800.0);
    public static StickDynamicsModifier DefaultAccumulator => new(StickDynamicsMode.Accumulator, 5.0, 400.0);
    public static StickDynamicsModifier DefaultPersistent => new(StickDynamicsMode.Persistent, 400.0, 0.0);
}

/// <summary>
/// Converts a Digital signal to a Scalar. Default 0.0 when off, 1.0 when on;
/// either can be customized (e.g. -1.0 / +1.0 for a centered axis driven by
/// a single button).
/// </summary>
public sealed record DigitalToScalarModifier(double OnValue, double OffValue) : Modifier
{
    public static DigitalToScalarModifier Default => new(1.0, 0.0);
}

/// <summary>
/// Converts a Scalar to a Digital. Output is true when |input| exceeds
/// Threshold, otherwise false. No hysteresis in v1.
/// </summary>
public sealed record ScalarToDigitalThresholdModifier(double Threshold) : Modifier
{
    public static ScalarToDigitalThresholdModifier Default => new(0.5);
}

/// <summary>
/// Multiplies the Scalar signal by Factor, then clamps to [-1, 1]. Acts on
/// the post-integrator signal — useful as an output cap / governor (e.g.
/// "walking speed never exceeds 60% stick deflection"). For "make the stick
/// require more mouse motion but still reach full deflection," use
/// <see cref="DeltaScaleModifier"/> instead, which acts on the Delta signal
/// before integration.
/// </summary>
public sealed record OutputScaleModifier(double Factor) : Modifier
{
    public static OutputScaleModifier Default => new(1.0);
}

/// <summary>
/// Forces |x| below Threshold to 0 and renormalizes the surviving range:
/// output = sign(x) * (|x| - d) / (1 - d) for |x| > d, else 0.
/// </summary>
public sealed record InnerDeadzoneModifier(double Threshold) : Modifier
{
    public static InnerDeadzoneModifier Default => new(0.1);
}

/// <summary>
/// Clamps |x| to (1 - Threshold) and renormalizes:
/// output = sign(x) * min(|x|, 1 - o) / (1 - o).
/// </summary>
public sealed record OuterSaturationModifier(double Threshold) : Modifier
{
    public static OuterSaturationModifier Default => new(0.1);
}

/// <summary>
/// Applies a power curve to |x|, sign-preserving:
/// output = sign(x) * |x|^Exponent. Exponent &lt; 1 boosts small inputs (concave);
/// &gt; 1 attenuates them (convex). Guard: Exponent &lt;= 0 collapses to 1.0 (identity).
/// </summary>
public sealed record ResponseCurveModifier(double Exponent) : Modifier
{
    public static ResponseCurveModifier Default => new(1.0);
}

/// <summary>Which side of the threshold gets the curve applied.</summary>
public enum SegmentedCurveRegion
{
    /// <summary>Below the threshold is linear passthrough; above the threshold is curved.</summary>
    AboveThreshold,

    /// <summary>Below the threshold is curved; above the threshold is linear passthrough.</summary>
    BelowThreshold
}

/// <summary>How the linear and curved segments connect at the threshold.</summary>
public enum SegmentedCurveTransitionStyle
{
    /// <summary>
    /// Output is continuous at the threshold but slope is discontinuous —
    /// produces a visible "kink." The original Segmented Response Curve
    /// behavior (preserved as an option). Use when the sharp inflection point
    /// is desired or for backward compatibility with profiles authored
    /// before smooth styles existed.
    /// </summary>
    Hard,

    /// <summary>
    /// Smoothstep blend (3u² − 2u³) between the linear formula and the
    /// power-curve formula. Smooth on both ends of the curved segment
    /// (matched derivatives at both join points). Preserves the existing
    /// meaning of Exponent as a power exponent of the underlying curve.
    /// </summary>
    SmoothStep,

    /// <summary>
    /// Cubic Hermite spline tangent to the linear segment at the threshold
    /// and reaching ±1 at full deflection with the requested terminal slope.
    /// C¹ smooth at the threshold by construction, but cubic constraints
    /// force the curve to *dip below* (convex) or *bulge above* (concave)
    /// the linear chord when terminal slope differs from chord slope. See
    /// <see cref="QuinticSmooth"/> for the no-dip/no-bulge alternative.
    /// </summary>
    HermiteSpline,

    /// <summary>
    /// Quintic Hermite spline with curvature matched to zero at both ends
    /// (C² smooth at the threshold and at full deflection). The curvature
    /// constraint eliminates the dip/bulge present in
    /// <see cref="HermiteSpline"/> — the curve is locally tangent AND
    /// flat-curvature with the linear segment at the join, so it cannot
    /// curl below/above the chord near the threshold. Exponent maps to
    /// terminal slope at full deflection (same as HermiteSpline). This is
    /// the recommended smooth style.
    /// </summary>
    QuinticSmooth,

    /// <summary>
    /// Additive power-curve form: <c>out = t + (u + (n−1)·u²) · L / n</c>
    /// for above-threshold convex. Simpler formula, no dip/bulge by
    /// construction (the added quadratic term is monotonically positive
    /// for convex). Has a small documented slope mismatch at the threshold
    /// from renormalization: linear-side slope is 1, curved-side slope is
    /// 1/Exponent. Acceptable trade-off for users who want a simpler
    /// mental model than the Hermite splines.
    /// </summary>
    PowerCurve
}

/// <summary>
/// Whether the curved segment accelerates away from the linear segment
/// (convex) or decelerates approaching the extreme (concave).
/// </summary>
public enum SegmentedCurveShape
{
    /// <summary>
    /// Curve accelerates away from the linear segment — gentle near the
    /// threshold, steep at the extreme. The "exponential" feel: tiny
    /// inputs near the threshold barely move; large inputs ramp up fast.
    /// </summary>
    Convex,

    /// <summary>
    /// Curve decelerates approaching the extreme — steep near the
    /// threshold, gentle at the extreme. The "logarithmic" feel: tiny
    /// inputs immediately produce noticeable output; large inputs
    /// flatten out approaching full deflection.
    /// </summary>
    Concave
}

/// <summary>
/// Applies a power curve to only one segment of |x|, with the other segment
/// passing through linearly. Sign-preserving. The curved segment is remapped
/// to its own [0, 1] sub-range so the two segments meet continuously at the
/// threshold (no jump in output).
///
/// <para>Region = AboveThreshold (default): |x| in [0, t] is linear; |x| in
/// (t, 1] is curved. Useful for keeping fine, low-deflection input precise
/// while making the upper range more aggressive.</para>
///
/// <para>Region = BelowThreshold: |x| in [0, t) is curved; |x| in [t, 1] is
/// linear. Useful for a soft start before a fully linear upper range.</para>
///
/// <para>Math (AboveThreshold): for a = |x|, t = Threshold, n = Exponent:
/// out = a when a ≤ t; out = t + ((a - t) / (1 - t))^n * (1 - t) when a &gt; t.</para>
///
/// <para>Guards: Exponent &lt;= 0 collapses to 1.0 (identity, matching
/// ResponseCurveModifier); the evaluator additionally clamps Threshold
/// strictly inside (0, 1) to avoid division-by-zero at the segment boundary.
/// The stored value round-trips unchanged.</para>
/// </summary>
/// <param name="Threshold">Fraction of |input| where the linear and curved segments meet, in [0, 1].</param>
/// <param name="Exponent">
/// Curve aggressiveness inside the curved segment. Meaning depends on
/// <paramref name="TransitionStyle"/>:
/// <list type="bullet">
///   <item><see cref="SegmentedCurveTransitionStyle.Hard"/> and
///   <see cref="SegmentedCurveTransitionStyle.SmoothStep"/>: power exponent
///   of the underlying curve (<c>u^Exponent</c>). &lt; 1 boosts; &gt; 1
///   attenuates the curved segment.</item>
///   <item><see cref="SegmentedCurveTransitionStyle.HermiteSpline"/>:
///   terminal slope at full deflection. = 1 gives a straight line; &gt; 1
///   gives progressively steeper acceleration toward the extremes.</item>
/// </list>
/// The numeric scale ends up feeling similar across styles so users don't
/// need to recalibrate when switching.
/// </param>
/// <param name="Region">Which segment is curved.</param>
/// <param name="TransitionStyle">
/// How the linear and curved segments connect at the threshold. Defaults to
/// <see cref="SegmentedCurveTransitionStyle.Hard"/> for backward
/// compatibility — old JSON without this field deserializes with the
/// original behavior. The catalog default (see <see cref="Default"/>) is
/// <see cref="SegmentedCurveTransitionStyle.QuinticSmooth"/> so newly-added
/// instances are smooth-and-dip-free out of the box.
/// </param>
/// <param name="Shape">
/// Whether the curved segment is <see cref="SegmentedCurveShape.Convex"/>
/// (accelerates away from the linear segment) or
/// <see cref="SegmentedCurveShape.Concave"/> (decelerates approaching the
/// extreme). Defaults to <c>Convex</c> for backward compatibility — old
/// JSON without this field deserializes with the original
/// accelerating-curve behavior across all styles.
/// </param>
public sealed record SegmentedResponseCurveModifier(
    double Threshold,
    double Exponent,
    SegmentedCurveRegion Region,
    SegmentedCurveTransitionStyle TransitionStyle = SegmentedCurveTransitionStyle.Hard,
    SegmentedCurveShape Shape = SegmentedCurveShape.Convex) : Modifier
{
    /// <summary>
    /// Catalog default: <see cref="SegmentedCurveTransitionStyle.QuinticSmooth"/>
    /// (C² smooth at the threshold — no dip, no bulge by construction) plus
    /// <see cref="SegmentedCurveShape.Convex"/>. Newly-added modifiers feel
    /// right without the user discovering the options.
    ///
    /// <para>The constructor defaults for <c>TransitionStyle</c> and
    /// <c>Shape</c> are intentionally <c>Hard</c> and <c>Convex</c> so old
    /// JSON without those fields loads with the original behavior. This
    /// factory only affects newly-added instances.</para>
    /// </summary>
    public static SegmentedResponseCurveModifier Default
        => new(0.3, 2.0, SegmentedCurveRegion.AboveThreshold,
               SegmentedCurveTransitionStyle.QuinticSmooth,
               SegmentedCurveShape.Convex);
}

/// <summary>
/// One control point of a <see cref="ParametricCurveModifier"/>'s response
/// curve. <c>Y</c> is the response output for input magnitude <c>X</c> in
/// symmetric mode, or signed input <c>X</c> in full-range mode.
/// </summary>
public sealed record CurvePoint(double X, double Y);

/// <summary>
/// User-defined response curve via control points. The evaluator interpolates
/// between points with monotone cubic Hermite (Fritsch-Carlson), which
/// guarantees C¹ smoothness AND that output is monotonic when the input
/// data is monotonic (no "more input → less output" artifacts).
///
/// <para>Symmetric mode (default): the points define behavior in X ∈ [0, 1]
/// and the negative side is computed via odd reflection — output(-x) =
/// -output(x). Full-range mode: points span X ∈ [-1, 1] and the user can
/// shape positive and negative input asymmetrically.</para>
///
/// <para>Constraint: at least 2 points required (otherwise the evaluator
/// returns input unchanged as a defensive fallback). The UI starts with 3
/// total points (2 endpoints + 1 interior) and lets the user add/remove
/// interior points up to a total of 7.</para>
/// </summary>
public sealed record ParametricCurveModifier : Modifier, ICurveData
{
    public IReadOnlyList<CurvePoint> Points { get; init; } = Array.Empty<CurvePoint>();
    public bool Symmetric { get; init; } = true;

    /// <summary>
    /// Catalog default: 3 points tracing the identity line in symmetric
    /// mode. New instances are passthrough; user reshapes from there.
    /// </summary>
    public static ParametricCurveModifier Default => new()
    {
        Points = new[]
        {
            new CurvePoint(0.0, 0.0),
            new CurvePoint(0.5, 0.5),
            new CurvePoint(1.0, 1.0),
        },
        Symmetric = true,
    };

    // Records with IReadOnlyList<T> fields don't get value-equality for the
    // list contents by default — the auto-generated Equals compares lists by
    // reference. Override to compare by sequence so the engine's
    // "preserve state when chain unchanged" cache eviction works correctly
    // for this modifier just like for the others.
    public bool Equals(ParametricCurveModifier? other)
    {
        if (other is null) return false;
        if (Enabled != other.Enabled) return false;
        if (Symmetric != other.Symmetric) return false;
        if (Points.Count != other.Points.Count) return false;
        for (int i = 0; i < Points.Count; i++)
            if (Points[i] != other.Points[i]) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Enabled);
        hash.Add(Symmetric);
        foreach (var p in Points) hash.Add(p);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Sibling to <see cref="ParametricCurveModifier"/> with the same data shape
/// (control points + symmetric flag) and identical math. The difference is
/// purely the UI: this kind is edited via an interactive drag-the-points
/// canvas in a popout window, while ParametricCurveModifier is edited via
/// per-point numeric sliders.
///
/// <para>Both modifiers implement <see cref="ICurveData"/>; the engine's
/// <c>ParametricCurveEvaluator</c> takes <see cref="ICurveData"/> so the
/// math is shared with zero duplication.</para>
///
/// <para>The two are not auto-convertible (different JSON discriminators) —
/// a profile saved with a canvas-edited curve stays that way on reload,
/// preserving the user's authoring intent.</para>
/// </summary>
public sealed record CurveEditorModifier : Modifier, ICurveData
{
    public IReadOnlyList<CurvePoint> Points { get; init; } = Array.Empty<CurvePoint>();
    public bool Symmetric { get; init; } = true;

    /// <summary>
    /// Catalog default: 3 points tracing the identity line in symmetric
    /// mode. Same default as <see cref="ParametricCurveModifier"/> so new
    /// instances from either catalog entry feel equivalent.
    /// </summary>
    public static CurveEditorModifier Default => new()
    {
        Points = new[]
        {
            new CurvePoint(0.0, 0.0),
            new CurvePoint(0.5, 0.5),
            new CurvePoint(1.0, 1.0),
        },
        Symmetric = true,
    };

    // List-field equality override — same reasoning as ParametricCurveModifier.
    public bool Equals(CurveEditorModifier? other)
    {
        if (other is null) return false;
        if (Enabled != other.Enabled) return false;
        if (Symmetric != other.Symmetric) return false;
        if (Points.Count != other.Points.Count) return false;
        for (int i = 0; i < Points.Count; i++)
            if (Points[i] != other.Points[i]) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Enabled);
        hash.Add(Symmetric);
        foreach (var p in Points) hash.Add(p);
        return hash.ToHashCode();
    }
}

/// <summary>Negates the Scalar signal: x → -x.</summary>
public sealed record InvertModifier() : Modifier;

/// <summary>
/// Rate-limits increases in |x|. Decreases pass through unchanged.
/// SecondsToFull is how long it takes to ramp from 0 to ±1 if the upstream
/// signal jumps to its limit.
/// </summary>
public sealed record RampUpModifier(double SecondsToFull) : Modifier
{
    public static RampUpModifier Default => new(0.5);
}

/// <summary>
/// Rate-limits decreases in |x|. Increases pass through unchanged.
/// SecondsFromFull is how long it takes to ramp from ±1 back to 0 if the
/// upstream signal jumps to 0.
/// </summary>
public sealed record RampDownModifier(double SecondsFromFull) : Modifier
{
    public static RampDownModifier Default => new(0.5);
}

/// <summary>
/// Hard-clamps the Scalar signal to per-side maxima:
///   output = clamp(x, -MaxNegative, +MaxPositive)
/// where both maxima are non-negative magnitudes. Asymmetric so the user
/// can cap one direction independently (e.g. a key that should only deflect
/// right up to 0.33 with MaxPositive=0.33, MaxNegative=1.0).
/// </summary>
public sealed record LimiterModifier(double MaxPositive, double MaxNegative) : Modifier
{
    public static LimiterModifier Default => new(1.0, 1.0);
}

/// <summary>
/// Caps-lock-style toggle. On a rising edge of the input, flips an internal
/// boolean; output is that internal state. Falling edges do nothing.
/// State is reset to false on profile change and on soft-mute resume.
/// </summary>
public sealed record ToggleModifier() : Modifier;

/// <summary>
/// Exponential moving average of the Scalar signal. Smaller TimeConstantSeconds
/// = snappier (less smoothing); larger = smoother but laggier. Set to 0 for
/// passthrough (no smoothing). Stateful: tracks the smoothed value across ticks.
/// </summary>
public sealed record SmoothingModifier(double TimeConstantSeconds) : Modifier
{
    public static SmoothingModifier Default => new(0.05);
}

/// <summary>
/// While the Digital input is true, output pulses true at <paramref name="Hz"/>
/// pulses per second (one tick true, one tick false, paced by Hz). When the
/// input is false the output is false. Hz &lt;= 0 means passthrough.
/// </summary>
public sealed record AutoFireModifier(double Hz) : Modifier
{
    public static AutoFireModifier Default => new(10.0);
}

/// <summary>
/// Output is true only after the Digital input has been held continuously for
/// <paramref name="HoldSeconds"/> seconds. As soon as the input goes false the
/// output goes false and the timer resets. HoldSeconds &lt;= 0 means passthrough.
/// </summary>
public sealed record HoldToActivateModifier(double HoldSeconds) : Modifier
{
    public static HoldToActivateModifier Default => new(0.5);
}

/// <summary>
/// Output pulses true on the release of a "tap" — input held for less than
/// <paramref name="MaxHoldSeconds"/>. Held longer means the release is treated as a
/// hold completion, not a tap, and nothing fires. Pair with HoldToActivate
/// on a second binding to the same source for tap-vs-hold splitting.
///
/// <para>When <paramref name="WaitForHigherTaps"/> is true, the modifier delays
/// firing for <paramref name="ConfirmWaitSeconds"/> after release to confirm no
/// further tap arrives (which would belong to a sibling MultiTap binding on
/// the same source). Confirmation can early-exit if a follow-up press is
/// held past <paramref name="MaxHoldSeconds"/> (clearly not a tap), at which point
/// the pending pulse fires immediately AND the over-held press is mirrored
/// straight through as Digital passthrough until released.</para>
/// </summary>
/// <param name="MaxHoldSeconds">Max input-held duration that still counts as a tap.</param>
/// <param name="PulseSeconds">How long the output stays true after a tap is detected. 0 = single tick.</param>
/// <param name="WaitForHigherTaps">When true, delay firing until ambiguity with sibling multi-tap bindings clears.</param>
/// <param name="ConfirmWaitSeconds">Time to wait after release for a higher-count tap to be ruled out. Only used when WaitForHigherTaps is true.</param>
public sealed record TapModifier(
    double MaxHoldSeconds,
    double PulseSeconds,
    bool WaitForHigherTaps = false,
    double ConfirmWaitSeconds = 0.4) : Modifier
{
    public static TapModifier Default => new(0.3, 0.05);
}

/// <summary>
/// Output pulses true after <paramref name="TapCount"/> taps have landed within
/// <paramref name="WindowSeconds"/> of each other. Each individual tap must be a
/// release within <paramref name="MaxHoldSeconds"/> (same definition as
/// <see cref="TapModifier"/>). The window timer starts from the first tap's
/// release. Subsequent presses within the window count toward the goal; if
/// the window expires before reaching <see cref="TapCount"/>, the counter
/// resets.
///
/// <para>When <paramref name="WaitForHigherTaps"/> is true, the modifier
/// delays firing after the Nth tap until <paramref name="WindowSeconds"/> has
/// elapsed without a further press, OR a follow-up press is held past
/// <paramref name="MaxHoldSeconds"/> (which would not be a tap and so cannot
/// extend the sequence — pending pulse fires immediately). Without this
/// flag, an N-tap fires the moment the Nth tap releases, even if a higher-
/// count sibling binding (e.g. triple-tap on the same key) is about to be
/// satisfied.</para>
/// </summary>
public sealed record MultiTapModifier(
    int TapCount,
    double WindowSeconds,
    double MaxHoldSeconds,
    double PulseSeconds,
    bool WaitForHigherTaps = false) : Modifier
{
    public static MultiTapModifier Default => new(2, 0.4, 0.3, 0.05);
}

/// <summary>
/// Standalone "wait for tap resolution" passthrough for plain Digital→Digital
/// bindings (e.g. Key → Button) on a source that ALSO has Tap or MultiTap
/// bindings. Without this, the plain binding fires immediately on press,
/// always conflicting with double-tap detection. With this, the binding
/// waits to confirm no multi-tap is in progress.
///
/// <para>Behavior:</para>
/// <list type="bullet">
///   <item>Press: suppress output, start hold timer.</item>
///   <item>Held within MaxHoldSeconds: keep suppressing.</item>
///   <item>Held past MaxHoldSeconds: passthrough — output mirrors input from
///         this point until release. The press was clearly not a tap, so
///         tap-counting doesn't apply.</item>
///   <item>Released within MaxHoldSeconds: enter wait state, suppress for
///         WaitSeconds.</item>
///   <item>Wait elapses with no follow-up press: fire a pulse for
///         PulseSeconds.</item>
///   <item>Follow-up press during wait: cancel pending pulse (the user is
///         doing a multi-tap on a sibling binding).</item>
/// </list>
/// </summary>
public sealed record WaitForTapResolutionModifier(
    double MaxHoldSeconds,
    double WaitSeconds,
    double PulseSeconds) : Modifier
{
    public static WaitForTapResolutionModifier Default => new(0.3, 0.4, 0.05);
}
