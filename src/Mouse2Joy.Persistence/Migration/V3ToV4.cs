using System.Text.Json.Nodes;

namespace Mouse2Joy.Persistence.Migration;

/// <summary>
/// JSON-node migration from schemaVersion 3 to 4.
///
/// v4 adds an optional <c>transitionStyle</c> field to
/// <c>segmentedResponseCurve</c> modifiers (selecting Hard / SmoothStep /
/// HermiteSpline math). The C# constructor default is <c>Hard</c>, so v3
/// documents without the field deserialize correctly without any content
/// rewriting — this migration only bumps the version stamp so the document
/// is canonical v4 on save.
///
/// <para>The convention is "every CurrentSchemaVersion bump gets a migration
/// function even if it's effectively a no-op." That's deliberate per
/// <c>ai-docs/MIGRATION_CONVENTIONS.md</c>: it documents that the version
/// bump was considered, keeps the pipeline's structure uniform, and the
/// extra cost is negligible.</para>
/// </summary>
internal static class V3ToV4
{
    public static void Apply(JsonNode root)
    {
        if (root is JsonObject ro)
        {
            ro["schemaVersion"] = 4;
        }
    }
}
