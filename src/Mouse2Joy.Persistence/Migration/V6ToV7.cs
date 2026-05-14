using System.Text.Json.Nodes;

namespace Mouse2Joy.Persistence.Migration;

/// <summary>
/// JSON-node migration from schemaVersion 6 to 7.
///
/// v7 adds a new modifier kind: <c>curveEditor</c> (interactive
/// drag-the-points canvas editor for response curves). No existing profiles
/// can contain this modifier, so the migration is purely a version-stamp
/// bump. Per the project convention (any wire-format change bumps + every
/// bump gets a migration), this exists even though there's nothing to
/// rewrite.
/// </summary>
internal static class V6ToV7
{
    public static void Apply(JsonNode root)
    {
        if (root is JsonObject ro)
        {
            ro["schemaVersion"] = 7;
        }
    }
}
