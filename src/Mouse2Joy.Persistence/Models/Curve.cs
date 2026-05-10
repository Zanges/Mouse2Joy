namespace Mouse2Joy.Persistence.Models;

/// <summary>
/// Per-axis shaping parameters. Same record applies to stick axes (signed)
/// and triggers (folded to positive); ignored for digital outputs.
/// </summary>
/// <param name="Sensitivity">Scalar applied before deadzone/saturation. 1.0 = identity.</param>
/// <param name="InnerDeadzone">Magnitude below which output is forced to 0. Range [0, 0.95).</param>
/// <param name="OuterSaturation">Distance from |x|=1 above which output is clamped to 1. Range [0, 0.95). Must satisfy InnerDeadzone + OuterSaturation &lt; 1.</param>
/// <param name="Exponent">Power applied to the post-deadzone signal. n &lt; 1 boosts small inputs (concave), n &gt; 1 attenuates them (convex). Range (0, 4].</param>
public sealed record Curve(
    double Sensitivity,
    double InnerDeadzone,
    double OuterSaturation,
    double Exponent)
{
    public static Curve Default { get; } = new(1.0, 0.0, 0.0, 1.0);
}
