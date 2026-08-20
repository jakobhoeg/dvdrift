# Azure Pipelines template reference

`pipelines/templates/dvdrift-steps.yml` is a steps template. Consume it from a
repository resource:

```yaml
resources:
  repositories:
    - repository: dvdrift
      type: github               # or 'git' for an Azure Repos mirror
      name: jakobhoeg/dvdrift
      endpoint: github-connection
      ref: refs/tags/v1

steps:
  - template: pipelines/templates/dvdrift-steps.yml@dvdrift
    parameters:
      baseline: $(Pipeline.Workspace)/baseline
      current: $(Build.ArtifactStagingDirectory)/current.zip
      url: $(DataverseUrl)
      clientId: $(DataverseAppId)
      clientSecret: $(DataverseSecret)
      tenantId: $(TenantId)
```

The template installs the .NET 8 SDK (`UseDotNet@2`) and the global tool, runs
the diff, uploads the Markdown report to the build summary, and publishes it as a
pipeline artifact. It works on Windows and Linux agents.

## Parameters

| Parameter | Default | Behavior |
|---|---|---|
| `baseline` | *(required)* | Baseline snapshot: solution `.zip`, container zip/folder, raw extract, or pac-unpacked folder |
| `current` | *(required)* | Current snapshot; the two formats need not match |
| `format` | `md` | `md` or `json` |
| `out` | `$(Build.ArtifactStagingDirectory)/dvdrift-report.md` | Report file path |
| `config` | *(none)* | Path to a `dvdrift.json` |
| `workingDirectory` | `$(Build.SourcesDirectory)` | Directory snapshot paths resolve from |
| `offline` | `false` | Skip the Dataverse attribution/state join |
| `failOnChange` | `false` | Fail the build when any change is detected |
| `publishArtifact` | `true` | Publish the report as a pipeline artifact |
| `artifactName` | `solution-diff` | Artifact name |
| `buildSummary` | `true` | Attach the report to the build summary (Markdown format only) |
| `toolVersion` | *(latest)* | Pin the global tool version |
| `name` | `dvdrift` | Step name the output variables are scoped to |
| `url` | *(none)* | Dataverse environment URL for the attribution join |
| `tenantId` / `clientId` / `clientSecret` | *(none)* | Client-credentials auth |
| `accessToken` | *(none)* | Pre-acquired token instead of client credentials |

Auth parameters reach the tool as `DATAVERSE_*` environment variables, never as
command-line arguments.

## Output variables

| Variable | Value |
|---|---|
| `<name>.changed` | `true` when the diff found at least one change, else `false` |
| `<name>.reportPath` | Path of the generated report |

Within the same job:

```yaml
- script: echo "Drift detected"
  condition: eq(variables['dvdrift.changed'], 'true')
```

Across jobs:

```yaml
- job: gate
  dependsOn: diff
  variables:
    changed: $[ dependencies.diff.outputs['dvdrift.changed'] ]
```

Pass a different `name` when the template is used more than once in a job, so the
output variables do not collide.

## Failure behavior

| Situation | Task result |
|---|---|
| No changes | success, `changed=false` |
| Changes, `failOnChange: false` | success, `changed=true` |
| Changes, `failOnChange: true` | failure with a logged error |
| Usage / auth / runtime error (tool exit `3`) | failure, no output variables |

The artifact is published with `condition: succeededOrFailed()`, so the report is
available even when the gate fails.

See the [CLI reference](cli.md) and the
[Azure Pipelines recipe](recipes/azure-pipelines-build-tools.md).
