# Dataverse Solution Governance

This context defines the exported solution state compared by the tool and the identities that must remain stable across packaging formats.

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