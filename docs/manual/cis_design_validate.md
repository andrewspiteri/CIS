---
title: "cis design validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-23"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-design-validate
---

# `cis design validate`

Validates the wireframe, renderer, guideline provenance, shared shell/table,
offline asset boundary, and review artifacts.

```text
cis design validate <change-id> [--repo <path>] [--format <human|json|agent>]
```

Exit `0` means the current lifecycle state is internally consistent. Exit `2` reports
missing provenance, unsafe renderer dependencies, invalid approvals, or missing PNGs
for a review/approved design.

Before visual approval, validation binds the pack to the current valid wireframe digest.
The digest is approved atomically with the rendered pack unless it already has a
standalone wireframe approval.
