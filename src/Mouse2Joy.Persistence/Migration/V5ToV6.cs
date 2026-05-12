using System.Text.Json.Nodes;

namespace Mouse2Joy.Persistence.Migration;

/// <summary>
/// JSON-node migration from schemaVersion 5 to 6.
///
/// v6 adds a new modifier kind: <c>parametricCurve</c> (user-defined response
/// curve via control points, monotone cubic Hermite interpolated). No
/// existing profiles can contain this modifier, so the migration is purely
/// a version-stamp bump.
///
/// <para>Per the project convention (any wire-format change bumps + every
/// bump gets a migration), this exists even though there's nothing to
/// rewrite. Adding a new modifier kind to the polymorphic discriminator set
/// is a wire-format change.</para>
/// </summary>
internal static class V5ToV6
{
    public static void Apply(JsonNode root)
    {
        if (root is JsonObject ro) ro["schemaVersion"] = 6;
    }
}
