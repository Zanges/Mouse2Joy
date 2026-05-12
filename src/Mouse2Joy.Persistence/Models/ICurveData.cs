namespace Mouse2Joy.Persistence.Models;

/// <summary>
/// Shared data interface for response curve modifiers. Implementations
/// hold control points and a symmetric flag; the evaluator interpolates
/// between points with monotone cubic Hermite (Fritsch-Carlson).
///
/// <para>Both <see cref="ParametricCurveModifier"/> (numeric editor) and
/// <see cref="CurveEditorModifier"/> (interactive canvas editor) implement
/// this so they can share the same evaluator without duplicating math.</para>
/// </summary>
public interface ICurveData
{
    IReadOnlyList<CurvePoint> Points { get; }
    bool Symmetric { get; }
}
