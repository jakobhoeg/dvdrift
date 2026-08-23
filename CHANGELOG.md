# Changelog

All notable changes to this project are documented here. The section for each
version becomes that version's GitHub release notes, so write it for users.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
