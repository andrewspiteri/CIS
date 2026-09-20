---
title: "cis graph status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-graph-status
---

# `cis graph status`

Reads the SQLite graph header, manifest, stored diagnostics, and current input hashes
without loading or validating every node and edge. Use it for UI status, routing readiness,
and Repository Doctor. Use `cis graph validate` when deep structural assurance is required.

## Synopsis

```text
cis graph status [--repo <path> | --workspace <path>]
  [--format <human|json|agent>]
```

The command reports graph availability, build identity, node/edge counts, stored build
diagnostics, and `fresh` or `stale` input state. It never rebuilds the graph or changes
canonical Markdown.

Within one read-only calculation, graph validation and metadata readers share input
existence, content hashes and read failures. The next calculation rechecks the inputs,
including edits that preserve size and last-write time. Directory scans reuse the filesystem's
enumeration metadata and skip reparse points; no persistent timestamp-only source cache is used.

Exit `0` means status was read, including a stale result; exit `2` means repository or
workspace configuration is invalid; exit `4` means no compatible graph exists; and exit
`5` means cached graph metadata is invalid.

## Related commands

- [`cis graph build`](cis_graph_build.md)
- [`cis graph validate`](cis_graph_validate.md)
- [`cis repo doctor`](cis_repo_doctor.md)
