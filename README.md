# DataverseDrift

**Several people are editing the same Dataverse environment, and nobody can say
what moved.** Forms, views, entities and flows all change in place, in a shared
DEV or TEST box, with no commit, no author, and no record.

`dvdrift` answers it. Point it at two snapshots and it reports **what changed, who
changed it, and what got left running** - like the cloud flow somebody activated to
try something and never switched off. No AI: identical inputs always produce
byte-identical output.

Raw XML diffs can't do this. Dataverse re-serializes solution XML on every export,
drowning real changes in thousands of lines of noise - and a diff only sees what
changed, so the flow quietly running in TEST since March never appears in one.
`dvdrift` canonicalizes the noise away, classifies what's left, and joins
attribution and flow state from the Dataverse Web API.

## Install

```sh
dotnet tool install --global DataverseDrift
```

Runs on .NET 8 or .NET 10. The diff works fully offline; only the attribution
join needs API access.

## Usage

```sh
# Two snapshots in, one report out
dvdrift baseline.zip current.zip

# JSON to a file, gate the pipeline on any change
dvdrift snapshots/monday snapshots/tuesday --format json --out report.json --fail-on-change

# Join attribution and flow state from Dataverse
dvdrift baseline current --url https://org-test.crm4.dynamics.com   --tenant-id $TENANT --client-id $APP --client-secret $SECRET
```

Snapshots can be a solution `.zip`, a container zip/folder of several, a raw
extract, or a `pac`-unpacked folder. Output is Markdown (default) or JSON.

- [CLI reference](https://github.com/jakobhoeg/dvdrift/blob/master/docs/cli.md) - all options, auth env vars, exit codes
- [What it detects](https://github.com/jakobhoeg/dvdrift/blob/master/docs/detection.md) - component coverage, example output, caveats
- [Configuration](https://github.com/jakobhoeg/dvdrift/blob/master/docs/configuration.md) - per-repo `dvdrift.json` overrides

## CI/CD

You produce the two snapshots, `dvdrift` compares them - storage- and CI-agnostic.
Both integrations below keep credentials out of command lines and leave gating to
you (off by default).

**GitHub Actions** ([reference](https://github.com/jakobhoeg/dvdrift/blob/master/docs/action.md)):

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
# report lands in the job summary; steps.diff.outputs.changed is 'true' / 'false'
```

**Azure Pipelines** ([reference](https://github.com/jakobhoeg/dvdrift/blob/master/docs/azure-pipelines-template.md)):

```yaml
- template: pipelines/templates/dvdrift-steps.yml@dvdrift
  parameters:
    baseline: $(Pipeline.Workspace)/baseline
    current: $(Build.ArtifactStagingDirectory)/current.zip
    url: $(DataverseUrl)
# report goes to the build summary and an artifact; $(dvdrift.changed) is set
```

End-to-end recipes: [bring your own export](https://github.com/jakobhoeg/dvdrift/blob/master/docs/recipes/generic-bring-your-own-export.md)
· [GitHub Actions + pac](https://github.com/jakobhoeg/dvdrift/blob/master/docs/recipes/github-actions-pac.md)
· [Azure Pipelines + Build Tools](https://github.com/jakobhoeg/dvdrift/blob/master/docs/recipes/azure-pipelines-build-tools.md)
· [scheduled drift check](https://github.com/jakobhoeg/dvdrift/blob/master/docs/recipes/scheduled-drift-check.md)

## Roadmap

- **v1.x** - field-level structural form/entity diffing; plugin step enabled/disabled
  flags; entity/attribute metadata attribution

## License

[MIT](https://github.com/jakobhoeg/dvdrift/blob/master/LICENSE) · [Changelog](https://github.com/jakobhoeg/dvdrift/blob/master/CHANGELOG.md)
