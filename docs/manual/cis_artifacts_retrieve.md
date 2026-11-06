---
title: "cis artifacts retrieve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-artifacts-retrieve
---

# `cis artifacts retrieve`

Verify and non-destructively extract an archive or one retained entry for inspection.

```text
cis artifacts retrieve --archive <id> [--entry <original-path>] [--repo <path>] [--format <human|json|agent>]
```

Output remains below `.cis/local/artifacts/retrieved/`. Digest mismatch, missing entries, and traversal fail with exit code `4`.

Retrieval creates a physical copy and therefore needs remaining owner capacity in governed work. Use the inherited association or `--change`, `--task`, `--authority`, `--parent-execution` and `--actor`. CIS reserves bounded expanded bytes before extraction; a storage hold creates no retrieval output. The existing archive and the new copy both count. Inspection and listing remain available without extraction, and retrieval never grants completion or cleanup authority.
