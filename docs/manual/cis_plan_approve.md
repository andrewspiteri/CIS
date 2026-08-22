---
title: "cis plan approve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-plan-approve
---

# `cis plan approve`

Records explicit human approval of a valid bounded plan.

```text
cis plan approve <change-id> --reviewer <identity> --reason <rationale> [--repo <path>] [--format <human|json|agent>]
```

Approval reruns all plan validation and blocks on incomplete impact, open decisions,
missing coverage, invalid dependencies, or absent acceptance and validation. A second
approval is `unchanged`. AI and deterministic analysis cannot invoke approval
implicitly. Approval records the reviewer, rationale, and event evidence.
