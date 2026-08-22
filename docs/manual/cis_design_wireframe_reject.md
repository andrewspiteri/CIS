---
title: "cis design wireframe-reject"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-13"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-design-wireframe-reject
---

# `cis design wireframe-reject`

Rejects the current textual wireframes with explicit reviewer identity and rationale.

```text
cis design wireframe-reject <change-id> --reviewer <identity> --reason <rationale> [--repo <path>] [--format <human|json|agent>]
```

The rejected digest remains in `wireframes.md`; status returns to in-progress so the
behavioral contract can be revised before renderer scaffolding.

