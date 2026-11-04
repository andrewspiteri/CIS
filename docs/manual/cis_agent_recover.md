---
title: "cis agent recover"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-recover
---

# `cis agent recover`

Recover a non-terminal run whose owned provider process is proven absent, or reconcile a terminal run with unfinalized accounting under the same owned-process checks.

```text
cis agent recover <run-id> --actor <identity> --reason <rationale> [--repo <path>] [--format <human|json|agent>]
```

CIS refuses recovery while the recorded process identity is active, during the startup grace period, or when the task lock belongs to another run. Valid non-terminal recovery marks the run `Interrupted`, appends the actor and reason, and removes only its matching stale lock. A terminal manifest is eligible only to reconcile its unfinalized accounting; terminal appearance is not proof that capacity can be released. Use `cis agent resume` afterwards when the provider supports continuation and the retained worktree, envelope and authority context pass its checks. A reusable native session is not always required: Claude reviews restart with full context because review sessions are nonpersistent. Recovery itself does not approve commands or resume execution.

Accounted direct work retains its launch intent, reservation, measured cost, and process uncertainty across interruption. A terminal-looking manifest or absent process checkpoint does not prove no work ran. With verified owned-process absence, conservative accounting recovery charges at least the full reserved bound; repeated reconciliation cannot spend an unlimited fresh allowance. A never-launched reservation without a run manifest uses the exact-execution [`work disposition reconcile`](cis_agent_work.md#reconcile-interrupted-preparation) path after its 30-second grace period. A launch intent without verified process identity remains uncertain. Such holds prevent conflicting work for the affected owner while their reservations continue to constrain the containing change; independently eligible owners may still fit within the remaining allowance. Diagnostic cleanup never erases accounting or allocation decisions.
