# Azure Pipelines + Power Platform Build Tools: deploy gate

The XrmBedrock-style stack: Azure DevOps pipelines, Power Platform Build Tools
service connections (workload identity federation), environment approvals.

```yaml
trigger: none

pool:
  vmImage: windows-latest   # PowerPlatform tasks require Windows; dvdrift itself runs anywhere

resources:
  repositories:
    - repository: dvdrift
      type: github               # or 'git' for an Azure Repos mirror
      name: jakobhoeg/dvdrift
      endpoint: github-connection
      ref: refs/tags/v1

stages:
  - stage: ExportAndDiff
    jobs:
      - job: diff
        steps:
          - task: PowerPlatformToolInstaller@2
            displayName: Install Power Platform tools

          - task: PowerPlatformExportSolution@2
            displayName: Export current solution
            inputs:
              authenticationType: PowerPlatformSPN
              PowerPlatformSPN: 'Dataverse Dev'      # workload-identity service connection
              SolutionName: 'MySolution'
              SolutionOutputFile: '$(Build.ArtifactStagingDirectory)/current.zip'

          - task: PowerShell@2
            displayName: Fetch last-deployed baseline
            inputs:
              targetType: inline
              script: |
                # Baseline pattern of your choice: snapshot branch, tag, or artifact.
                git fetch --depth 1 origin UnpackedExports
                git checkout origin/UnpackedExports -- baseline/

          - template: pipelines/templates/dvdrift-steps.yml@dvdrift
            parameters:
              baseline: baseline
              current: $(Build.ArtifactStagingDirectory)/current.zip
              out: $(Build.ArtifactStagingDirectory)/report.md
              url: $(DataverseUrl)                 # environment variable group
              clientId: $(DataverseAppId)
              clientSecret: $(DataverseSecret)
              tenantId: $(TenantId)

  - stage: Deploy
    dependsOn: ExportAndDiff
    jobs:
      - deployment: import
        environment: Test          # wire approvals in ADO Environments — this is the gate
        strategy:
          runOnce:
            deploy:
              steps:
                - download: none
                - script: echo "PowerPlatformImportSolution + persist new baseline"
```

## The template

`pipelines/templates/dvdrift-steps.yml` installs the .NET 8 SDK and the global
tool, runs the diff, uploads the Markdown report to the build summary, publishes
it as a pipeline artifact, and sets two output variables (`changed`,
`reportPath`) that a later job can read:

```yaml
- job: gate
  dependsOn: diff
  variables:
    changed: $[ dependencies.diff.outputs['dvdrift.changed'] ]
```

Set `failOnChange: true` to fail the build on any change instead of only
reporting it; `offline: true` skips the attribution join. See the
[template reference](../azure-pipelines-template.md) for every parameter.

## Token note

Build Tools authenticate via the service connection, but `dvdrift` needs its own
token for the attribution join. Options:

1. **Client credentials** (shown above): store `DataverseAppId`/`DataverseSecret`/
   `TenantId` in the environment variable group (XrmBedrock already assumes these
   exist) and pass them as the `clientId`/`clientSecret`/`tenantId` parameters.
2. **Workload identity token:** use an `AzureCLI@2` task with the ARM service
   connection to run `az account get-access-token --resource $(DataverseUrl)`,
   store it in a variable, and pass it as the `accessToken` parameter instead. No
   secrets anywhere.

Either way the template hands the values to the tool as `DATAVERSE_*` environment
variables, so secrets never appear in a command line or the build log.

## Environment approvals

The diff itself doesn't block anything — the ADO **Environment** approval on the
deploy stage is the gate. The report artifact is what the reviewer reads before
clicking Approve. See the [template reference](../azure-pipelines-template.md) for
gating parameters, and the [generic recipe](generic-bring-your-own-export.md) for baseline patterns.
