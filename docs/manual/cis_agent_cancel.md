---
title: "cis agent cancel"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-28"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-cancel
---

# `cis agent cancel`

Stop retained owned execution from another controller, including an outstanding accounted execution whose latest recorded process has already exited.

```text
cis agent cancel <run-id> --actor <identity> --reason <rationale> [--repo <path>] [--format <human|json|agent>]
```

CIS first validates the bounded retained owned-process set from the run manifest, event journal, and accounting record. It kills a process tree only when its process ID, start timestamp and ownership can be verified through the existing cancellation rules. It stops every verified still-live owned process, including an earlier one when the latest manifest process has exited. A missing, incomplete, changed or unverifiable identity retains the appropriate hold; stopping only the latest process cannot prove the whole execution stopped.

For an accounted direct task, cancellation preserves spent work and settles its reservation only after all relevant recorded owned execution is verified stopped. Any unknown or surviving process retains recovery uncertainty and reserved capacity, so conflicting work stays held. Cancellation appends actor, rationale and lifecycle evidence. A previously recorded terminal execution outcome remains distinct from accounting cleanup; repeated settlement does not charge or release capacity twice. Cancellation does not replenish the containing change or task allowance, clear an engineering hold, or grant another implementation route. Inspect [`cis agent work status`](cis_agent_work.md) and the retained run before continuing.
