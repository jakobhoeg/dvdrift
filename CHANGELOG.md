# Changelog

All notable changes to this project are documented here. The section for each
version becomes that version's GitHub release notes, so write it for users.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.1] - 2026-08-29

### Fixed

- Solution manifests are no longer reported as modified when the only difference
  is the solution version. Dataverse and export pipelines auto-increment
  `SolutionManifest/Version` between exports, which made every comparison list
  every `Solution manifest` component as modified. Other manifest changes (and
  `Version` attributes elsewhere, such as dependency requirements) still diff as
  before.

## [1.2.0] - 2026-08-25

### Added

- `--summary-only` emits just the counts line, without the per-component tables,
  for places where the full report is too verbose to embed - release notes, chat
  notifications, PR comments. It composes with both formats: with `--format json`
  the counts sit at the root of the document (`.added`, `.modified`, `.deleted`)
  so notification and gating scripts need no Markdown parsing. Exposed as
  `summary-only` on the GitHub Action and `summaryOnly` on the Azure Pipelines
  template.

## [1.1.1] - 2026-08-25

### Fixed

- The GitHub Action no longer fails the step when changes are detected and
  `fail-on-change` is `false`. GitHub runs composite steps with `bash -e`, which
  aborted the script the moment `dvdrift` exited non-zero, so the `changed`
  output and job summary were never produced.

## [1.1.0] - 2026-08-23

### Added

- .NET 10 support. The tool now ships both `net8.0` and `net10.0` builds and
  installs against whichever runtime you have; .NET 8 users are unaffected.

## [1.0.0] - 2026-08-23

First public release.

### Added

- Deterministic diff of two Dataverse solution snapshots: solution `.zip`,
  container zip/folder, raw extract, or `pac`-unpacked folder.
- Canonicalizer that strips Dataverse re-serialization noise (version stamps,
  `IntroducedVersion`, regenerated GUIDs, node ordering) so only real changes
  surface.
- Component classification across entities, attributes, forms, views, flows,
  workflows, web resources, canvas apps, PCF controls, custom APIs, security
  roles, plugin assemblies and more, with a fallback bucket so nothing is
  silently dropped.
- Attribution and flow-state join against the Dataverse Web API: `modifiedby`,
  `modifiedon`, and `Draft` / `Activated` / `Suspended` state - so flows left
  running show up even though they did not change.
- Markdown and JSON reports, `--fail-on-change` gating, and `dvdrift.json`
  configuration.
- GitHub Actions composite action and Azure Pipelines steps template.
