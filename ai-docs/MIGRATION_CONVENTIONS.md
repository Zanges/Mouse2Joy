# Migration conventions

Schema migrations live in [src/Mouse2Joy.Persistence/Migration/](../src/Mouse2Joy.Persistence/Migration/). Two patterns, choose by the size of the change — this is a hybrid convention, deliberately *not* "always one or the other":

- **JSON-node rewrite (default for small changes).** Operate on `JsonNode` / `JsonObject` in place: rename discriminators, rename fields, add a defaulted field, drop a field. The migration takes the root `JsonNode` and mutates it. Lightweight, no model duplication. Example: [V2ToV3.cs](../src/Mouse2Joy.Persistence/Migration/V2ToV3.cs) (`"sensitivity"` → `"outputScale"` rename).

- **Typed-record rebuild (for structural changes).** Mirror the old version's model graph into `Legacy/V<N>/`, write a pure `LegacyVN → Profile` transformation. Use when fields are added or removed, types are restructured, or business logic from the old shape needs to be re-expressed in the new shape. Example: [V1ToV2.cs](../src/Mouse2Joy.Persistence/Migration/V1ToV2.cs) (mapped v1's `Curve` + `StickModel` to v2 modifier chains — genuinely structural).

Migrations chain in `ProfileStore.DeserializeProfile`: peek `schemaVersion`, apply each successive migration up to `Profile.CurrentSchemaVersion`. A v1 doc passes through V1ToV2 then V2ToV3 then deserializes as a v3 `Profile`. The first `Save()` after migration persists the document in the current shape.

## Rules

- **Every bump of `Profile.CurrentSchemaVersion` MUST add a corresponding migration function** registered in the `DeserializeProfile` pipeline. The pipeline has to handle the previous version cleanly.
- **Every migration MUST have a dedicated unit test in `Mouse2Joy.Persistence.Tests`** covering at minimum: a profile in the prior version migrates correctly, a no-op profile (without the changed feature) still loads, the version stamp is updated. Bonus: end-to-end coverage through `ProfileStore.DeserializeProfile`.
- **Bump rule: any wire-format change bumps the version, even cosmetic.** A discriminator rename or field rename counts. Better to over-bump than to break compatibility silently or rely on judgment calls.
- **Don't pin specific version numbers in documentation or assertions.** Reference `Profile.CurrentSchemaVersion` so docs and tests don't go stale on the next bump.

## Why the hybrid pattern

A single uniform pattern was tempting but wrong:

- *Always typed-record rebuild* would require copying the entire current model graph into `Legacy/V<N>/` on every version bump. After a year of work the legacy folder would be dozens of records of churn. The maintenance cost compounds quickly and makes small migrations (like our V2→V3 discriminator rename) feel disproportionate.
- *Always JSON-node rewrite* would force structural migrations (rebuilding business logic from a different shape, like V1's `Curve` → v2's modifier chain) into raw JSON manipulation. That's where typed records earn their keep — strong typing catches mistakes at compile time and the v1 model documents what the old shape *was*.

So: choose by the shape of the change. Field renames and discriminator changes are JSON-node. Structural rebuilds get a typed-legacy mirror. If you're unsure, JSON-node is the default — it's cheaper and works for most cases.

## Implementation notes

- **Property names are camelCase in JSON** because [JsonOptions.cs](../src/Mouse2Joy.Persistence/JsonOptions.cs) sets `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`. A C# property `Multiplier` serializes as `"multiplier"`. JSON-node migrations operate on camelCase keys (`obj["multiplier"]`, not `obj["Multiplier"]`).
- **The `$kind` discriminator is the polymorphism marker** for `Modifier`, `InputSource`, `OutputTarget`. Renaming a class without renaming the discriminator string means existing profiles still load; renaming both requires a migration step.
- **`V1ToV2.Migrate` always stamps `Profile.CurrentSchemaVersion`**, not v2 specifically. So a v1 doc passing through V1ToV2 today produces a v3-shaped Profile directly, and the V2ToV3 step is a no-op for v1-sourced documents. That's intentional — typed migrations only handle structural transforms; the JSON-node pipeline handles the cosmetic cleanups uniformly afterwards. Don't change V1ToV2 to "actually produce v2" — it'd just create a different inconsistency.
- **JSON-node migrations should be idempotent and tolerant of missing fields.** A no-op profile (e.g. v2 doc with no `sensitivity` modifier) must pass through cleanly. The V2ToV3 implementation checks for the discriminator existence before rewriting and silently skips otherwise.

## Future considerations

- If `Profile`'s model gets enough new top-level fields that JSON-node rewrites start touching many places, consider an `INewFieldDefaults` interface or a typed-rebuild step. So far the field additions have been local (inside a modifier or binding) and the JSON-node convention has stayed lightweight.
- If v1 support is ever dropped, [V1ToV2.cs](../src/Mouse2Joy.Persistence/Migration/V1ToV2.cs) and [Legacy/V1/](../src/Mouse2Joy.Persistence/Legacy/V1/) can go entirely. The chain just collapses to start at v2.
- Adding a `MigrationLog` (which migrations ran on this profile, with timestamps) is a possible future feature if profile corruption ever needs to be diagnosed. Not worth doing speculatively.
