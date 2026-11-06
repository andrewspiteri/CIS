---
title: "cis artifacts compact"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-artifacts-compact
---

# `cis artifacts compact`

Create and verify a reversible archive before removing eligible original entries.

```text
cis artifacts compact --family <archive-family> --yes [--repo <path>] [--format <human|json|agent>]
```

The command rejects non-archive families and missing confirmation. It records a content-hashed ZIP and manifest beneath `.cis/local/artifacts/archive/`.

Governed compaction inherits the existing work owner or accepts `--change`, `--task`, `--authority`, `--parent-execution` and `--actor`. The actor needs the original allocation authority or its explicit cleanup delegation. CIS reserves temporary ZIP and metadata growth before writing and counts the surviving ZIP, manifest and any retrieval copies afterward. Compaction can reduce live bytes while increasing total owned storage. A capacity or evidence-protection hold does not authorize deleting unknown material. Time and logical attempts remain charged at the same owner.
