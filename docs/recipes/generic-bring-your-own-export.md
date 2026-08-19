# Generic: bring your own export

`dvdrift` never exports, unpacks, or imports solutions itself. Every integration is
the same three steps:

```
produce snapshot A ──╮
                      ├─► dvdrift A B ─► report (+ exit code)
produce snapshot B ──╯
```

## Accepted snapshot formats

| Format | How you get it | Notes |
|---|---|---|
| Solution `.zip` | `pac solution export`, Power Platform Build Tools export task, DAXIF `Solution.Export` | Read directly, no unpack step needed |
| Container `.zip` / folder of solution zips | Multi-solution export pipelines that produce one zip per solution | Expanded recursively; components are prefixed with their solution name |
| Raw zip-extract folder | `unzip solution.zip` | What "save the artifact" pipelines naturally produce |
| `pac solution unpack` folder | `pac solution unpack` / SolutionPackager | Per-component folder layout |

Mixed inputs are fine (zip vs folder, either layout).

Note: unmanaged, non-extended exports do not contain flow definitions. If flow
diffing matters, export managed/extended (DAXIF `extended = true`) or use
`pac solution unpack`.

## Producing snapshots

**pac CLI:**

```sh
pac auth create --environment $ENV_URL --tenant $TENANT --applicationId $APP_ID --clientSecret $SECRET
pac solution export --name MySolution --path current.zip
# or, if you prefer the unpacked layout in source control:
pac solution unpack --zipfile current.zip --folder current/ --allowWrite
```

**Azure DevOps Power Platform Build Tools:**

```yaml
- task: PowerPlatformExportSolution@2
  inputs:
    authenticationType: PowerPlatformSPN
    PowerPlatformSPN: 'Dataverse Dev'
    SolutionName: MySolution
    SolutionOutputFile: $(Build.ArtifactStagingDirectory)/current.zip
```

**DAXIF:** `SolutionExportDev.fsx` (or `Solution.Export` with `extended = true`)
produces a zip whose extracted layout `dvdrift` reads natively.

## Running the diff

```sh
# Offline (no attribution/state — pure diff):
dvdrift baseline.zip current.zip --offline --out report.md

# With attribution + flow state:
dvdrift baseline.zip current.zip \
  --url $ENV_URL --tenant-id $TENANT --client-id $APP_ID --client-secret $SECRET \
  --out report.md --fail-on-change
```

See the [CLI reference](../cli.md) for authentication environment variables,
gating flags, and exit codes.

## Baseline intent

- **Deploy gate:** baseline = snapshot persisted after last successful deploy,
  current = freshly exported solution. Gate with `--fail-on-change`.
- **Scheduled drift check:** baseline = yesterday's scheduled export of the same
  environment, current = today's. Report changed components with live attribution
  and flow state.

## Baseline persistence patterns

The tool doesn't care where snapshots live. Common patterns:

1. **Orphan git branch** with timestamped folders (`2026-08-19_08-00/managed/...`) —
   history for free, works for both modes.
2. **Pipeline artifacts** with retention — simplest, but retention expires.
3. **Git tags** (`deployed/test`, moved after each successful deploy) pointing at a
   commit containing the unpacked snapshot.
4. **Rolling directory** on a self-hosted runner for drift checks (yesterday/today).
