# Configuration

`dvdrift` works with zero configuration. When a repo needs to adjust what counts as
re-serialization noise, drop a `dvdrift.json` in the working directory (or pass
`--config <path>`).

## dvdrift.json

```json
{
  "stripElements": {
    "add": ["MyVolatileElement"],
    "remove": ["formid"]
  },
  "stripAttributes": {
    "add": ["generatedBy"],
    "remove": []
  }
}
```

- `add` — extra element/attribute local names appended to the defaults.
- `remove` — entries subtracted from the defaults.

Names are matched case-insensitively by XML local name, so namespace and casing do
not affect a rule. Both lists are optional; omit the file entirely to use defaults.
Without `--config`, `dvdrift.json` is discovered in the current working directory.
An explicit `--config` path that does not exist is a runtime error (exit `3`).

## Default strip list

Validated against real Dataverse exports (DAXIF extended exports and `pac solution export`):

| Kind | Entries |
|---|---|
| Elements | `IntroducedVersion`, `modifiedon`, `createdon`, `overriddencreatedon`, `importsequencenumber`, `versionnumber`, `formid`, `savedqueryid` |
| Attributes | `OrganizationVersion`, `CRMServerServiceabilityVersion`, `modifiedon`, `createdon`, `SdkMessageProcessingStepId`, `SdkMessageProcessingStepImageId`, `SdkMessageProcessingStepSecureConfigId`, `RoleId`, `AppModuleIdUnique` |

Notes:

- `formid`/`savedqueryid` are stripped from *content* because component identity is
  matched by logical name first. The ids are still recorded by the extractor and
  shown as an _(id changed)_ note when a component was deleted and recreated.
- Plugin-step, role, and app-module ids are likewise read before canonicalization,
  then stripped from comparison content because export/sync operations regenerate them.
- If you diff snapshots where GUID stability matters more than delete+recreate
  resilience, remove `formid`/`savedqueryid` from the strip list as shown above.
- Solution `<Version>` is intentionally **not** stripped by default — a version bump
  is a real change. If your pipeline auto-increments on every export, add
  `"Version"` to `stripElements.add`.

## Canonicalization rules (not configurable)

These invariants are always applied, identically on both sides:

- XML: attributes sorted, child element order preserved, insignificant whitespace
  discarded, LF line endings, no XML declaration. Child order is treated as semantic
  unless paired real exports prove a specific collection is unordered.
- JSON (flow definitions): object properties sorted recursively, array order
  preserved, fixed indentation, LF line endings.
- Text (YAML formula definitions, text web resources): LF endings, trailing
  whitespace trimmed.
- Binaries (plugin assemblies, images): SHA-256 hash comparison only.
