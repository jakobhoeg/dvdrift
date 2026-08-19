# Scheduled drift check

The use case that catches what deploy gates can't: **changes made directly in a
shared environment outside the deploy path** — a flow adjusted in TEST, or a form
tweaked during investigation.

Nightly: export the environment, diff against yesterday's export, attach live
attribution/state to changed components, and post the report.

## GitHub Actions (cron)

```yaml
name: Nightly drift check

on:
  schedule:
    - cron: "0 1 * * *"   # 01:00 UTC
  workflow_dispatch:

permissions:
  contents: write   # push the rolling snapshot back to the snapshot branch

jobs:
  drift:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Install tools
        run: |
          dotnet tool install --global DataverseDrift
          dotnet tool install --global Microsoft.PowerApps.CLI.Tool

      - name: Export current environment state
        run: |
          pac auth create --environment "${{ vars.TEST_URL }}" \
            --tenant "${{ secrets.TENANT_ID }}" \
            --applicationId "${{ secrets.DATAVERSE_APP_ID }}" \
            --clientSecret "${{ secrets.DATAVERSE_CLIENT_SECRET }}"
          pac solution export --name MySolution --path today.zip

      - name: Fetch previous snapshot
        run: |
          git fetch --depth 1 origin DriftSnapshots || true
          git checkout origin/DriftSnapshots -- yesterday/ 2>/dev/null || mkdir -p yesterday

      - name: Diff and capture report
        run: |
          dvdrift yesterday today.zip \
            --url "${{ vars.TEST_URL }}" \
            --tenant-id "${{ secrets.TENANT_ID }}" \
            --client-id "${{ secrets.DATAVERSE_APP_ID }}" \
            --client-secret "${{ secrets.DATAVERSE_CLIENT_SECRET }}" \
            --out drift-report.md
          cat drift-report.md >> "$GITHUB_STEP_SUMMARY"

      - name: Roll the snapshot forward
        run: |
          # push today's export (unzipped) as the new 'yesterday' on DriftSnapshots
          echo "snapshot roll-forward goes here"

      - name: Notify on drift
        if: failure()
        run: echo "The drift report failed or a notification rule matched — post to Teams/Slack"
```

## Azure Pipelines (schedules)

Same flow with a `schedules:` cron trigger, `PowerPlatformExportSolution@2` for the
export, and the report published as a build artifact. See
[azure-pipelines-build-tools.md](azure-pipelines-build-tools.md) for the token options.

## Reading the report in drift mode

The **Changes** section answers "what did people do in this environment yesterday".
Changed flows and workflows carry their live attribution and state when a Dataverse
URL and credentials are supplied. Deleted components show attribution as unavailable
— a deleted flow leaves nothing to query. The row still tells you *what* disappeared.

See the [CLI reference](../cli.md) for option and exit-code semantics.

## Choosing the snapshot cadence

One export per night per environment is the sweet spot: exports are idempotent and
read-only, and a 24h window keeps reports short enough to actually read. If your
environment is quiet, weekly works too — the mechanism is identical.
