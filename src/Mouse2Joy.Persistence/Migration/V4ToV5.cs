using System.Text.Json.Nodes;

namespace Mouse2Joy.Persistence.Migration;

/// <summary>
/// JSON-node migration from schemaVersion 4 to 5.
///
/// v5 adds an optional <c>shape</c> field to <c>segmentedResponseCurve</c>
/// modifiers (selecting Convex / Concave) and two new
/// <c>transitionStyle</c> enum values (<c>QuinticSmooth</c>,
/// <c>PowerCurve</c>). The C# constructor default for <c>Shape</c> is
/// <c>Convex</c>, so v4 documents without the field deserialize correctly
/// without any content rewriting — this migration only bumps the version
/// stamp so the document is canonical v5 on save.
///
/// <para>Per the project convention (any wire-format change bumps + every
/// bump gets a migration), this exists even though it's effectively a
/// version-stamp bump. See <c>ai-docs/MIGRATION_CONVENTIONS.md</c>.</para>
/// </summary>
internal static class V4ToV5
{
    public static void Apply(JsonNode root)
    {
        if (root is JsonObject ro)
        {
            ro["schemaVersion"] = 5;
        }
    }
}
