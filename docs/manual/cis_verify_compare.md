---
title: "cis verify compare"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-verify-compare
---

# `cis verify compare`

Compare planned repository and explicit path targets with observed workspace changes.

```text
cis verify compare <change-id> [--repo <path>] [--format <human|json|agent>]
```

Reports unknown repositories as errors and planned-but-missing or unplanned repositories and paths as warnings without rewriting the plan. Authority-repository dossier changes are implicit governance evidence and are not treated as an unplanned participant target.
