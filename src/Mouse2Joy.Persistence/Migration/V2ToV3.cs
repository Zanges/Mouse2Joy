using System.Text.Json.Nodes;

namespace Mouse2Joy.Persistence.Migration;

/// <summary>
/// JSON-node migration from schemaVersion 2 to 3.
///
/// v2 had a "sensitivity" modifier kind with a "multiplier" field. v3 renames
/// it to "outputScale" with a "factor" field (display name "Output Scale").
/// Runtime semantics are unchanged — pure rename driven by adding the new
/// "Delta Scale" modifier and wanting an unambiguous distinction between
/// "input-side scaler" and "output-side scaler."
///
/// <para>This is a small, single-field migration, so it follows the JSON-node
/// rewrite convention rather than the typed-record-rebuild pattern used for
/// V1→V2 (which had structural shape changes).</para>
/// </summary>
internal static class V2ToV3
{
    public static void Apply(JsonNode root)
    {
        // Property names use camelCase per JsonOptions.Default's naming policy.
        if (root["bindings"] is JsonArray bindings)
        {
            foreach (var binding in bindings)
            {
                if (binding?["modifiers"] is not JsonArray mods)
                {
                    continue;
                }

                foreach (var mod in mods)
                {
                    if (mod is not JsonObject obj)
                    {
                        continue;
                    }

                    if (obj["$kind"]?.GetValue<string>() != "sensitivity")
                    {
                        continue;
                    }

                    obj["$kind"] = "outputScale";
                    if (obj["multiplier"] is { } m)
                    {
                        // DeepClone() detaches the value so it can be re-parented;
                        // a direct assignment would throw because m still has obj as parent.
                        obj["factor"] = m.DeepClone();
                        obj.Remove("multiplier");
                    }
                }
            }
        }

        // Stamp the new version so Save() persists in the new shape.
        if (root is JsonObject ro)
        {
            ro["schemaVersion"] = 3;
        }
    }
}
