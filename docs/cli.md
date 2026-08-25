# CLI reference

```text
dvdrift <baseline> <current> [options]
```

`baseline` and `current` may each be a solution zip, a container zip or folder of
solution zips, a raw extracted solution folder, or a `pac solution unpack` folder.
The two inputs do not need to use the same format.

## Options

| Option | Behavior |
|---|---|
| `--format <md|json>` | Output format; defaults to Markdown |
| `--summary-only` | Emit only the counts line, without the per-component tables |
| `--out <file>` | Write the report to a file instead of standard output |
| `--config <file>` | Load a specific config file; see [configuration.md](configuration.md) |
| `--offline` | Skip Dataverse attribution and automation-state queries |
| `--fail-on-change` | Exit `1` when any change is detected |
| `--help`, `-h` | Show command help |
| `--version` | Show the installed version |

## Compact output

`--summary-only` drops the per-component tables and emits just the counts line,
for places where the full report is too verbose to embed - release notes, chat
notifications, PR comments. It composes with both formats and with
`--fail-on-change`, so a gating run can still produce a one-line report.

```bash
dvdrift baseline/ current/ --summary-only
```

```text
**190 added · 111 modified · 6 deleted**
```

Pair it with `--format json` when a script needs the numbers. The counts sit at
the root, so no Markdown parsing is involved:

```bash
ADDED=$(dvdrift baseline/ current/ --format json --summary-only | jq .added)
```

```json
{
  "added": 190,
  "modified": 111,
  "deleted": 6,
  "attributionIncluded": false
}
```

## Attribution

Attribution requires `--url` (or `DATAVERSE_URL`) and either an access token or
client credentials. Without `--offline` or a URL, the diff still succeeds without
attribution/state and writes a warning to standard error.

| Option | Environment fallback |
|---|---|
| `--url <url>` | `DATAVERSE_URL` |
| `--tenant-id <guid>` | `DATAVERSE_TENANT_ID` |
| `--client-id <guid>` | `DATAVERSE_CLIENT_ID` |
| `--client-secret <secret>` | `DATAVERSE_CLIENT_SECRET` |
| `--access-token <token>` | `DATAVERSE_ACCESS_TOKEN` |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | No enabled gating condition was hit |
| `1` | Changes were detected and `--fail-on-change` was set |
| `3` | Usage, configuration, authentication, or runtime error |

