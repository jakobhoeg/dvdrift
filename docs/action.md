# GitHub Action reference

The repository root ships a composite action. Reference it by tag:

```yaml
- uses: YOUR-ORG/dataverse-solution-diff@v1
  id: diff
  with:
    baseline: baseline/
    current: current.zip
```

It installs the `DataverseDrift` global tool (skipped if `dvdrift` is
already on `PATH`), runs the diff, and appends the Markdown report to the job
summary. The runner needs a .NET 8 SDK; GitHub-hosted runners have one, so add
`actions/setup-dotnet@v4` only on self-hosted runners.

## Inputs

| Input | Default | Behavior |
|---|---|---|
| `baseline` | *(required)* | Baseline snapshot: solution `.zip`, container zip/folder, raw extract, or pac-unpacked folder |
| `current` | *(required)* | Current snapshot; the two formats need not match |
| `format` | `md` | `md` or `json` |
| `out` | `dvdrift-report.md` | Report file path |
| `config` | *(none)* | Path to a `dvdrift.json`; defaults to one in the working directory if present |
| `offline` | `false` | Skip the Dataverse attribution/state join |
| `fail-on-change` | `false` | Fail the step when any change is detected |
| `job-summary` | `true` | Append the report to the job summary (Markdown format only) |
| `working-directory` | `.` | Directory snapshot paths resolve from |
| `tool-version` | *(latest)* | Pin the global tool version |
| `url` | *(none)* | Dataverse environment URL for the attribution join |
| `tenant-id` / `client-id` / `client-secret` | *(none)* | Client-credentials auth |
| `access-token` | *(none)* | Pre-acquired token instead of client credentials |

Auth inputs are passed to the tool as `DATAVERSE_*` environment variables, never
as command-line arguments.

## Outputs

| Output | Value |
|---|---|
| `changed` | `true` when the diff found at least one change, else `false` |
| `report-path` | Path of the generated report |

`changed` is set whether or not `fail-on-change` is enabled, so a later step can
branch on it:

```yaml
- if: steps.diff.outputs.changed == 'true'
  run: echo "Drift detected - review ${{ steps.diff.outputs.report-path }}"
```

## Failure behavior

| Situation | Step result |
|---|---|
| No changes | success, `changed=false` |
| Changes, `fail-on-change: false` | success, `changed=true` |
| Changes, `fail-on-change: true` | failure with an error annotation |
| Usage / auth / runtime error (tool exit `3`) | failure, no outputs |

See the [CLI reference](cli.md) for the underlying options and the
[GitHub Actions recipe](recipes/github-actions-pac.md) for a full deploy gate.
