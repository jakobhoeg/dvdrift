# DataverseDrift

**Several people are editing the same Dataverse environment, and nobody can say
what moved.** Forms, views, entities and flows all change in place, in a shared
DEV or TEST box, with no commit, no author, and no record. A week later someone
asks who touched the Account form, and the honest answer is that nobody knows.

`dvdrift` answers it. Point it at two snapshots of an environment and it reports
**what changed, who changed it, and what got left running** - like the cloud flow
somebody activated to try something and never switched off. No AI: identical
inputs always produce byte-identical output.

## Why this is hard today

The information you want already exists. Dataverse records `modifiedby` and
`modifiedon` on metadata, and `statecode` on every flow. Nothing joins it to a
report a human would read. So teams fall back on diffing the exported solution
XML, which does not work either:

1. **Raw XML diffs are unusable** - Dataverse re-serializes solution XML on every
   export (version stamps, `IntroducedVersion`, regenerated GUIDs, reordered
   nodes), drowning the handful of real changes in thousands of lines of noise.
2. **A diff can only see what changed** - the flow that has been quietly running
   in TEST since March did not change this week, so it appears in no diff, ever.
   It still needs to show up in the report.
3. **The existing tools stop short** - solution comparers are interactive GUIs,
   `pac` has no diff verb at all, and native auditing covers data records rather
   than customizations.

`dvdrift` canonicalizes away the serialization noise, classifies changes into
friendly component types, joins attribution + flow state from the Dataverse Web API,
and emits Markdown (default) or JSON.

## Install

```sh
dotnet tool install --global DataverseDrift
```

(.NET 8 SDK required. The core diff works fully offline; only the attribution join
needs API access.)

## Quick start

```sh
# Two snapshots in, one report out
dvdrift baseline.zip current.zip

# JSON to a file, gate the pipeline on any change
dvdrift snapshots/2026-08-18 snapshots/2026-08-19 --format json --out report.json --fail-on-change

# Join attribution and flow state from Dataverse
dvdrift baseline current --url https://org-test.crm4.dynamics.com \
  --tenant-id $TENANT --client-id $APP --client-secret $SECRET
```

## Usage

```sh
dvdrift <baseline> <current> [options]
dvdrift --help
```

See the [CLI reference](https://github.com/jakobhoeg/dvdrift/blob/master/docs/cli.md) for all options, authentication environment
variables, and exit-code behavior.

## What it detects

- **Added / Modified / Deleted** per component: entities, attributes, forms, views,
  flows, classic workflows, web resources, environment variable definitions, app
  actions, canvas apps, PCF controls, custom APIs, formulas (Power Fx), security
  roles, app modules (model-driven apps), global option sets, entity relationships,
  dashboards, plugin steps, service endpoints, custom connectors, Copilot agents,
  plugin assemblies (hash-compared), plus a fallback bucket so nothing is silently
  dropped - including any customizations.xml section the tool doesn't know yet.
- **Container bundles**: a zip or folder holding one solution zip per solution
  (what multi-solution export pipelines produce) is expanded recursively; component
  names are prefixed with the solution they came from (`ContosoFlows / MySyncFlow`).
- **Identity matching** is logical-name first, GUID fallback - a deleted-and-rebuilt
  component shows as `Modified` (with an "id changed" note), not a Delete+Add pair.
- **Flow/workflow state** (`Draft` / `Activated` / `Suspended`) and **attribution**
  (`modifiedby`/`modifiedon`) come from a live Web API join. Deleted components and
  offline runs show attribution as unavailable - the report never fails over it.

Known content caveats (validated against real exports):

- Unmanaged, non-extended exports contain **no flow definitions** (the `<Workflows>`
  block is empty) - use managed/extended exports or `pac solution unpack` if flow
  diffing matters.
- Re-serialization noise suppressed by the canonicalizer includes DAXIF sync
  timestamps in plugin-step descriptions and auto-incremented assembly versions in
  `PluginTypeName`. Plugin DLLs and canvas-app `.msapp` binaries are hash-compared;
  non-deterministic builds (embedded timestamps) will show as `Modified` on every
  rebuild.
- Relationships between two tables included in the solution are reported per-item.
  Relationships reaching tables outside the solution (first-party tables, other
  vendors' tables) are grouped into one component per in-solution table - e.g.
  `custom_entity (external relationships)` - so a new activity entity doesn't flood
  the report with dozens of auto-generated lookups.

## Example output

```markdown
# Dataverse Solution Diff

**2 added · 2 modified · 1 deleted**

_Attribution: live Dataverse join (point-in-time)_

## Changes

### Added

| Type      | Component         | Modified by | Modified on (UTC) |
| --------- | ----------------- | ----------- | ----------------- |
| Attribute | account.attribute | John Smith  | 2026-08-18 13:02  |

...
```

## How it fits your pipeline

The tool is deliberately storage- and CI-agnostic: you produce the two snapshots,
`dvdrift` compares them. Two ready-made integrations wrap the install-run-report
loop and tell you whether anything changed:

**GitHub Actions** — composite action ([reference](https://github.com/jakobhoeg/dvdrift/blob/master/docs/action.md)):

```yaml
- uses: jakobhoeg/dvdrift@v1
  id: diff
  with:
    baseline: baseline/
    current: current.zip
    url: ${{ vars.DEV_URL }}
    client-id: ${{ secrets.DATAVERSE_APP_ID }}
    client-secret: ${{ secrets.DATAVERSE_CLIENT_SECRET }}
    tenant-id: ${{ secrets.TENANT_ID }}
# report is in the job summary; steps.diff.outputs.changed is 'true' / 'false'
```

**Azure Pipelines** — steps template ([reference](https://github.com/jakobhoeg/dvdrift/blob/master/docs/azure-pipelines-template.md)):

```yaml
- template: pipelines/templates/dvdrift-steps.yml@dvdrift
  parameters:
    baseline: $(Pipeline.Workspace)/baseline
    current: $(Build.ArtifactStagingDirectory)/current.zip
    url: $(DataverseUrl)
    clientId: $(DataverseAppId)
    clientSecret: $(DataverseSecret)
    tenantId: $(TenantId)
# report goes to the build summary and a pipeline artifact; $(dvdrift.changed) is set
```

Both install the global tool from NuGet, keep credentials out of command lines,
and leave gating to you (`fail-on-change` / `failOnChange`, off by default).
End-to-end recipes:

- [Generic "bring your own export" guide](https://github.com/jakobhoeg/dvdrift/blob/master/docs/recipes/generic-bring-your-own-export.md)
- [GitHub Actions + pac (deploy gate)](https://github.com/jakobhoeg/dvdrift/blob/master/docs/recipes/github-actions-pac.md)
- [Azure Pipelines + Power Platform Build Tools](https://github.com/jakobhoeg/dvdrift/blob/master/docs/recipes/azure-pipelines-build-tools.md)
- [Scheduled drift check (nightly export → report)](https://github.com/jakobhoeg/dvdrift/blob/master/docs/recipes/scheduled-drift-check.md)

## Configuration

Per-repo overrides of the volatile-element strip list live in `dvdrift.json`.
See [docs/configuration.md](https://github.com/jakobhoeg/dvdrift/blob/master/docs/configuration.md).

## Determinism guarantee

Identical snapshot inputs produce byte-identical report bytes: stable ordering,
normalized line endings, no generation timestamps. The attribution join is a
point-in-time read of live data and is clearly marked as such in the report.

## Roadmap

- **v1** (this): canonicalizer + classifier + attribution/state join + Markdown/JSON +
  change gating.
- **v1.x**: field-level structural form/entity diffing; broader state flags
  (plugin steps enabled/disabled); entity/attribute metadata attribution.
- **v2**: notification routing (Slack/Teams) for scheduled drift checks.

## License

[MIT](https://github.com/jakobhoeg/dvdrift/blob/master/LICENSE)
