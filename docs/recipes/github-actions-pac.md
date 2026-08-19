# GitHub Actions + pac: approval gate

Diff the freshly exported solution against the last-deployed baseline, surface the
report in the job summary, and require manual approval before import.

This assumes the Microsoft ALM-style flow: export from the source environment,
diff, gate, import into the target.

```yaml
name: Deploy with solution diff gate

on:
  workflow_dispatch:

permissions:
  contents: read
  id-token: write

jobs:
  export-and-diff:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Install pac
        run: dotnet tool install --global Microsoft.PowerApps.CLI.Tool

      - name: Auth to source environment
        run: |
          pac auth create --environment "${{ vars.DEV_URL }}" \
            --tenant "${{ secrets.TENANT_ID }}" \
            --applicationId "${{ secrets.DATAVERSE_APP_ID }}" \
            --clientSecret "${{ secrets.DATAVERSE_CLIENT_SECRET }}"

      - name: Export current solution
        run: pac solution export --name MySolution --path current.zip

      - name: Fetch last-deployed baseline
        run: |
          # Baseline pattern: this repo's UnpackedExports-style branch, a git tag,
          # or a previous workflow artifact. Example: orphan snapshot branch.
          git fetch --depth 1 origin UnpackedExports || true
          git checkout origin/UnpackedExports -- baseline/ 2>/dev/null || mkdir -p baseline

      - name: Diff against baseline
        id: diff
        uses: YOUR-ORG/dataverse-solution-diff@v1
        with:
          baseline: baseline
          current: current.zip
          out: report.md
          url: ${{ vars.DEV_URL }}
          tenant-id: ${{ secrets.TENANT_ID }}
          client-id: ${{ secrets.DATAVERSE_APP_ID }}
          client-secret: ${{ secrets.DATAVERSE_CLIENT_SECRET }}

      - uses: actions/upload-artifact@v4
        with:
          name: solution-diff-report
          path: report.md

  approve:
    needs: export-and-diff
    runs-on: ubuntu-latest
    environment: production-approval   # configure required reviewers in repo settings
    steps:
      - run: echo "Reviewed report, approving import"

  import:
    needs: approve
    runs-on: ubuntu-latest
    steps:
      - run: echo "pac solution import ..."

      - name: Persist new baseline after successful deploy
        run: echo "Commit current.zip (extracted) to the snapshot branch or move the deployed/<env> tag"
```

## Notes

- **The action** installs the global tool, runs the diff, appends the Markdown
  report to the job summary, and exposes the `changed` (`true`/`false`) and
  `report-path` outputs. Full input list: [action reference](../action.md).
- **Gate behavior:** this example is report-only; the protected
  `production-approval` environment is the gate. Set `fail-on-change: true` only
  when changes should stop the workflow instead of being reviewed. `changed` is
  reported either way, so a later step can branch on
  `steps.diff.outputs.changed == 'true'` without failing the job.
- **Runner .NET:** the tool targets .NET 8. GitHub-hosted runners ship it; on a
  self-hosted runner add `actions/setup-dotnet@v4` with `8.0.x` first.
- **Service principal scoping:** the app registration needs read access to the
  environment for the attribution join (`systemforms`, `savedqueries`, `workflows`).
  The same principal used for export usually suffices.
- **Secretless variant:** replace client-secret auth with OIDC federated credentials
  (`azure/login`), fetch a token via
  `az account get-access-token --resource $ENV_URL`, and pass it with
  `--access-token`.
- **First run:** with no baseline yet, everything shows as Added — expected.
  Persist the first export as the baseline and move on.
- See the [CLI reference](../cli.md) for option and exit-code semantics, and the
  [generic recipe](generic-bring-your-own-export.md) for baseline patterns.
