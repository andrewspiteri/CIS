---
title: "cis solution-design approve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-solution-design-approve
---

# `cis solution-design approve`

```text
cis solution-design approve --reviewer <human> --reason <rationale>
  [--workspace <path>] [--format <human|json|agent>]
```

Records one explicit human approval over the exact overall solution design and component
sheet. Both files receive matching authority, timestamp, rationale, lifecycle, and bundle
hash; their catalogue entries become Active together. Approval fails unless the complete
bundle is valid and current.
