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

CIS checks the bounded union of retained owned identities from the manifest, event journal, and accounting record. It refuses recovery while any relevant identity remains active or unverifiable, during the startup grace period, or when the task lock belongs to another run. Use `cis agent cancel` to stop independently verified live owned processes, including an earlier one when the latest has exited or another identity remains unresolved. That partial cleanup leaves unverifiable processes untargeted and cannot authorize whole-execution settlement; recovery remains non-success until every relevant identity is resolved and stopped. Valid non-terminal recovery marks the run `Interrupted`, appends the actor and reason, and removes only its matching stale lock. A terminal manifest is eligible only to reconcile its unfinalized accounting; terminal appearance is not proof that capacity can be released. Use `cis agent resume` afterwards when the provider supports continuation and the retained worktree, envelope and authority context pass its checks. A reusable native session is not always required: Claude reviews restart with full context because review sessions are nonpersistent. Recovery itself does not approve commands or resume execution.

Accounted direct work retains its launch intent, reservation, measured cost, and process uncertainty across interruption. A terminal-looking manifest or absent process checkpoint does not prove no work ran. With every relevant owned process verified absent, conservative accounting recovery charges at least the full reserved bound and settles once. A never-launched reservation without a run manifest uses the exact-execution [`work disposition reconcile`](cis_agent_work.md#reconcile-interrupted-preparation) path only after its 30-second grace period and proof that the recorded preparing controller stopped. A live preparer, another-host owner, unreadable identity or missing historical ownership metadata remains held; grace and no provider PID alone do not prove abandonment. A launch intent without verified process identity also remains uncertain. Such holds prevent conflicting work for the affected owner while their reservations continue to constrain the containing change; independently eligible owners may still fit within the remaining allowance. Diagnostic cleanup never erases accounting or allocation decisions.

Governed story stages use the same process and accounting checks. After recovery, continue through the approved story task's `execution-plan` and `execute` commands. Generic `agent resume` redirects story stages to that route so candidate review, correction history and application checks remain governed. Settling a stage does not release an inclusive root reservation while another owned stage remains uncertain.
