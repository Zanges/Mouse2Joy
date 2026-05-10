using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Persistence;

/// <summary>
/// Walks a Source → [Modifiers] → Target chain and verifies the signal types
/// match end-to-end. Disabled modifiers still type-check so toggling a
/// modifier off cannot turn a previously valid chain invalid.
/// </summary>
public static class ChainValidator
{
    public static ValidationResult Validate(InputSource source, IReadOnlyList<Modifier> modifiers, OutputTarget target)
    {
        var current = ModifierTypes.GetSourceOutputType(source);
        for (int i = 0; i < modifiers.Count; i++)
        {
            var (inType, outType) = ModifierTypes.GetIO(modifiers[i]);
            if (inType != current)
            {
                return ValidationResult.Invalid(
                    $"Modifier {i + 1} ({ModifierTypes.GetDisplayName(modifiers[i])}) expects {inType}, but the chain produces {current} at this point.",
                    i);
            }
            current = outType;
        }

        var targetIn = ModifierTypes.GetTargetInputType(target);
        if (current != targetIn)
        {
            return ValidationResult.Invalid(
                modifiers.Count == 0
                    ? $"Source produces {current} but target expects {targetIn}. Add a converter modifier."
                    : $"Final modifier output is {current} but target expects {targetIn}.",
                modifiers.Count);
        }

        return ValidationResult.Ok;
    }
}

/// <summary>
/// Result of validating a chain. <see cref="ErrorIndex"/> points to the first
/// problematic modifier (or modifiers.Count if the tail-to-target edge is the
/// problem).
/// </summary>
public sealed record ValidationResult(bool IsValid, string? ErrorMessage, int ErrorIndex)
{
    public static ValidationResult Ok { get; } = new(true, null, -1);
    public static ValidationResult Invalid(string message, int index) => new(false, message, index);
}
