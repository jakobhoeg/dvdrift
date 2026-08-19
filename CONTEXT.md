# DataverseDrift

This context defines the language of the tool: the exported solution state it compares, the identities that must remain stable across packaging formats, and the governance concepts layered on top of the diff.

## Naming

The NuGet package is **DataverseDrift**; it installs the **`dvdrift`** command; per-repo configuration lives in **`dvdrift.json`**. Project, assembly, and namespace names remain `Dataverse.SolutionDiff.*` - an internal detail that is deliberately not user-facing. Use the package name for the product, the command name for invocations.
_Avoid_: dvdiff, Dataverse.SolutionDiff (as the product name), solution differ

## Language

**Solution snapshot**:
A point-in-time folder or archive containing one or more exported or unpacked Dataverse solutions.
_Avoid_: Export bundle, input package

**Snapshot scope**:
The files belonging to exactly one Dataverse solution within a solution snapshot. Its stable identity is the solution manifest `UniqueName` when available; archive and folder names are packaging hints, not identity.
_Avoid_: Path prefix, solution folder

**Solution layout**:
The structural representation of a snapshot scope, such as a raw or extended export or a PAC/SolutionPackager unpack. Layout affects extraction but not the identity of equivalent solution components.
_Avoid_: Folder format, export shape

**Component**:
One addressable customization within a snapshot scope - an entity, attribute, form, view, flow, web resource, and so on - carrying a component type and a name. Components are matched across snapshots by logical name first, GUID second, so a deleted-and-rebuilt component reports as modified rather than as a delete plus an add.
_Avoid_: Item, artifact, object

**Serialization noise**:
Differences produced by Dataverse re-serializing a solution on export - version stamps, `IntroducedVersion`, regenerated GUIDs, reordered nodes - that carry no customization meaning. Canonicalization removes it before comparison; the strip list is configurable per repo.
_Avoid_: Churn, false positive, junk diff

**Baseline** / **current**:
The two snapshots being compared. Baseline is the approved or previously observed state; current is the state under review. The same mechanism serves both a deploy gate (baseline = last deployed) and a scheduled drift check (baseline = previous export), so the terms describe roles, not cadence.
_Avoid_: Source/target, before/after, old/new

**Drift**:
Any way the current snapshot no longer matches the baseline. Covers both changed components and automations left in an unintended state; a component may drift without any file changing, which is why drift is not a synonym for the diff.
_Avoid_: Delta, divergence, difference (as a synonym for drift)

**Attribution**:
The `createdby`/`createdon`/`modifiedby`/`modifiedon` join from the Dataverse Web API, answering who changed a component and when. It is a point-in-time read of live data, is architecturally separate from the diff engine, and degrades to "unavailable" - never to a failure - when a component is deleted or the run is offline.
_Avoid_: Ownership, blame, audit trail

**Automation inventory**:
The always-reported list of cloud flows, classic workflows, and business process flows with their state, status, owner, and last modification. Independent of change detection by design: an unchanged-but-active automation must still appear in every report.
_Avoid_: Flow list, state flag

**Gating**:
Failing the run when a chosen condition is met, via `--fail-on-change` and the exit code. Gating is opt-in; reporting is the default, and the pipeline assets surface whether anything changed without failing unless asked.
_Avoid_: Blocking, enforcement, policy

**Determinism**:
Identical snapshot inputs produce byte-identical report bytes - stable ordering, normalized line endings, no generation timestamps, no AI. The attribution join reads live data and is marked in the report as point-in-time, which is the one part of the output that is not reproducible from the inputs alone.
_Avoid_: Idempotent, stable output
