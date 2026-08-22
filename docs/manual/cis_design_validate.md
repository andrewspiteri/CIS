---
title: "cis design validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-design-validate
---

# `cis design validate`

Validates the approved wireframe, renderer, guideline provenance, shared shell/table,
offline asset boundary, and review artifacts.

```text
cis design validate <change-id> [--repo <path>] [--format <human|json|agent>]
```

Exit `0` means the current lifecycle state is internally consistent. Exit `2` reports
missing provenance, unsafe renderer dependencies, invalid approvals, or missing PNGs
for a review/approved design.

