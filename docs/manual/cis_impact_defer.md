---
title: "cis impact defer"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-impact-defer
---

# `cis impact defer`

Records that a finding is acknowledged but unresolved outside the current approved
scope.

```text
cis impact defer <change-id> <finding-id> --reason <text>
  [--repo <path>] [--format <human|json|agent>]
```

Deferred findings remain a completeness gap and block plan readiness until resolved.
