---
title: "cis impact accept"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-impact-accept
---

# `cis impact accept`

Records explicit human acceptance of one proposed impact.

```text
cis impact accept <change-id> <finding-id> --reason <text>
  [--repo <path>] [--format <human|json|agent>]
```

The reason is mandatory. Accepted impacts must be covered by plan work items. The
command updates `impact.md` and appends an audit event; analysis alone never accepts.
