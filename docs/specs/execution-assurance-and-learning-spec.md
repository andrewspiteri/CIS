---
title: "Execution, Assurance, Diagnostics, and Learning"
type: specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-09"
review_cadence: "on execution or authority change"
cis:
  stable_id: change-impact-studio:spec:execution-assurance-learning
---

# Execution, assurance, diagnostics, and learning

## Authority model

Canonical Markdown owns routes, workflow definitions, task requirements, evidence,
human reviews, acceptance, and applied-learning history. Derived envelopes, run state,
model cache, sanitized usage, verification snapshots, diagnostic analysis, and learning
proposals live under `.cis/local/`. Graphs and projections can be rebuilt. Preserve native
results, transcripts, snapshots and receipts while they support verification or review;
rebuilding context does not recreate the evidence of a past execution.

No model, workflow exit code, agent result, tracker status, diagnostic finding, or
learning proposal can grant task completion, design or plan approval, risk acceptance,
verification acceptance, or change closure.

## AI and deterministic generation

The AI profile maps named capabilities to provider, model, remote permission, and cache
policy. CIS selects an available local provider when the route requests the repository's
smallest local model. Remote use requires the canonical route and an explicit
`--allow-remote` authorization for the exact prompt. Usage records omit prompts and
responses. Template rendering requires every value, rejects repository escapes, writes
atomically, and records content hashes.

## Workflows and agents

Workflow Markdown contains ordered step IDs, executable plus argument list, dependencies,
continue-on-failure choice, and timeout. CIS never evaluates a shell command. It checkpoints
after every step, resumes succeeded steps only against the same definition digest, and
streams redacted, timestamped, channel-labelled output into bounded attempt-specific
logs. Timeout and diagnostic rerun paths preserve partial and earlier output rather than
overwriting it.

Agent preparation binds one task's canonical path and digest into a portable envelope.
Result ingestion validates envelope identity, current task digest, structured fields, and
repository-relative changed paths. It appends an evidence row but leaves lifecycle
transition and completion to the governed plan command and human authority.

## Provider permissions and continuation

`agent run` requires explicit provider, mode, permission and actor; target and transport are optional selections.
`agent prepare` does not accept mode or permission flags. Codex `--approve-requests` applies only to supported
requests within the declared ceiling. Claude print mode has no interactive approval relay: `acceptEdits` does
not by itself approve shell commands, and reported permission denials fail the attempt.

For supported implementation tasks with workspace-write, optional repeated `--allow-command` entries add literal
native rules for the current attempt. Validation rejects wildcards and shell-rule injection syntax. Rules are
recorded with actor and attempt, and are not silently reused on resume. CIS restarts native session context when
either attempt has a command list. Native normalization, compound commands, directory changes and subprocess
behavior remain relevant; these rules do not implement OS-level confinement. Claude review sessions also restart
because they are nonpersistent. The complete bound task context and normal freshness checks still apply.

See the [run](../manual/cis_agent_run.md) and [resume](../manual/cis_agent_resume.md) contracts. Provider completion
is distinct from successful execution of required checks; inspect retained tool outputs and native results.

## Verification

Verification captures an exact Git-baseline file snapshot, compares observed files with
task targets, and validates plan, design, task, and evidence gates. Evidence rows name the
exact task, check, command or artifact, result, and notes. Final verification acceptance
requires a clean validation plus a human reviewer and rationale.

Repositories that adopt engineering defaults derive required gates independently of configured checks. Native
completion checks exact task/source/policy/graph identity, traceability, artifact integrity and distinct review
runs. Passed, failed, missing, stale, skipped and justified inapplicable remain separate states. The closing
iteration rebuilds context, reconciles CIS guidance/dependencies, checks all-standard alignment, reruns affected
verification after edits, and receives the assigned reviewer's final-state check.

Use [completion context](../manual/cis_plan_task_completion_context.md) for the evidence contract and
[finalize](../manual/cis_verify_finalize.md) for standard whole-change closure. Separate acceptance/closure writes
can make a prior snapshot stale; recapture and validate the resulting bookkeeping without inventing new approval.

## Diagnostics and learning

The diagnostics profile lists repository-relative sources and explicit enabled/sensitive
flags. CIS refuses sensitive sources, bounds every read, performs defense-in-depth
redaction, and persists only normalized derived analysis.

Test runners place suite-specific runtime evidence beneath
`.cis/local/testing/diagnostics/<suite-id>/<run-id>/attempt-<number>/`. Reconciliation hashes workflow logs and
suite diagnostics and correlates them to the run, attempt, suite, component, repository
revision, and exact executed test identities. Browser, Compose, and container harnesses
capture bounded failure evidence before cleanup; collection failure cannot be reported as
a passing test artifact.

Learning collection aggregates sanitized feedback and diagnostic counts. Proposals remain
derived until human approval. Application promotes only the reviewed recommendation,
reviewer, rationale, timestamp, and source digest into canonical learning history; it does
not self-modify source code, instructions, policies, approvals, or acceptance.
