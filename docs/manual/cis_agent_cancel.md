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

CIS preserves repository/run validation, required actor and reason, and evidence-read checks. It identifies cancellation candidates from the bounded retained manifest, event journal, and accounting evidence. Each candidate needs reliable provenance for its complete process ID/start-time pair and current ownership verification. An incomplete identity cannot borrow another process's timestamp, even if the resulting pair happens to match the operating system's timing tolerance. CIS rechecks identity before stopping each verified still-live owned process, including an earlier one when the latest manifest process has exited.

Uncertainty about one retained identity does not prevent stopping another independently verified owned process. Unverifiable processes remain untargeted. If any relevant process remains live or uncertain after this partial cleanup, the result is non-success, the reservation remains unfinalized, and conflicting work stays held. Stopping a verified process is not proof of whole-execution settlement.

For an accounted direct task, cancellation preserves spent work and settles its reservation only after every relevant owned process is verified stopped and identity uncertainty is resolved. Cancellation appends actor, rationale and lifecycle evidence. A previously recorded terminal status, result, failure kind and completion time remain distinct from accounting cleanup; repeated settlement does not charge or release capacity twice. Ordinary nonterminal cancellation remains available. Cancellation does not replenish the containing change or task allowance, clear an engineering hold, or grant another implementation route. Inspect [`cis agent work status`](cis_agent_work.md) and the retained run before continuing.
