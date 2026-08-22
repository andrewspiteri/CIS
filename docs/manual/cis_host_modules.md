---
title: "cis host modules"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-08"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-host-modules
---

# `cis host modules`

Lists the CIS modules explicitly loaded by the host.

## Synopsis

```text
cis host modules [--format <human|json|agent>]
```

## Options

| Option | Required | Default | Effect |
| --- | --- | --- | --- |
| `--format <format>` | No | `human` | Selects `human`, `json`, or `agent` output. Values are case-insensitive. |
| `-?`, `-h`, `--help` | No | — | Shows command help and exits without listing modules. |

## Effects

The command reads the in-memory module catalog assembled by the CIS composition root. It does not scan the target repository, discover arbitrary assemblies, or modify files.

Each item contains the module name, description, assembly name, and assembly version. Human output emphasizes the name, version, and description; JSON returns a `modules` array; agent output emits one record per module:

```text
module=<name>;assembly=<assembly>;version=<version>
```

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | The module catalog was rendered successfully. |
| `2` | The requested output format is unsupported. |

## Examples

```powershell
cis host modules
cis host modules --format json
cis host modules --format agent
```
