---
title: "Stale-Safe Task Envelopes"
type: article
status: Active
series: "Human and Agent Execution"
series_order: 4
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on agent-envelope change
summary: "How path and digest binding prevents old prepared work from being accepted against a changed task."
cis:
  stable_id: change-impact-studio:article:stale-safe-task-envelopes
---

# Stale-safe task envelopes

Prepared agent work can outlive the task that created it. A decision changes, a
specification is revised, or targets are narrowed while an executor continues from an
older package.

Without stale detection, both versions look legitimate.

## Bind the envelope to canonical identity

CIS task envelopes record the change ID, work ID, canonical task path, task digest,
accepted scope digests, context manifest, repository identity and revision or working-tree
digest, target repository, run mode, provider, permission ceiling, expiry, and preparation
metadata. The digests represent the exact authority supplied to the executor.

Direct execution adds a run and attempt history without replacing the envelope. Resume
resends the complete current digest-bound contract and uses provider continuation only when
the recorded provider capability supports it.

## Validate again on result ingestion

When a result returns, CIS checks envelope identity and compares the recorded digest
with the current task. It validates structured fields and ensures changed paths are
repository relative.

A mismatch is evidence that the work targeted different authority. The system stops
instead of merging the result optimistically.

Unreadable, partial, out-of-target, stale, or malformed expected results become invalid
evidence rather than a successful run. Process success and evidential validity are separate
states.

## Stale does not mean useless

The implementation may contain valuable work. A human can inspect it, revise the plan,
or prepare a new task. The result cannot be accepted automatically as fulfillment of
the current task.

## Import is not completion

Valid ingestion appends evidence. It does not transition the task, approve scope, or
accept verification. Those gates use current canonical state and independent evidence.

## Staleness can enter through several paths

The task text is only one input. An envelope can become stale when:

- accepted impact or a resolved decision changes;
- a canonical specification receives a new digest;
- repository targets or allowed paths are narrowed;
- the product-definition activation moves;
- the target working tree no longer matches its captured baseline;
- a required provider capability or permission policy changes; or
- the envelope passes its expiry.

Each case means the executor received a different authority or environment from the one
now under review. A timestamp alone cannot establish freshness; identity and digest
comparison can.

## Working-tree identity matters

Not every approved task starts from a clean commit. CIS can seed an isolated worktree from
the exact tracked and non-ignored untracked baseline. The envelope records that identity
so pre-existing files are not mistaken for provider output and later local edits are not
silently included.

This is particularly important when comparing providers. Two attempts are comparable
only when their starting state and task digest agree. “Both ran on Tuesday” is not a
controlled baseline.

## Resume creates another attempt, not another task

A recoverable interruption may justify resuming the same run. The new attempt preserves
the original task and run identity, records its own events and timing, and receives the
complete current contract again. Provider continuation is used only when the adapter
supports it and the authority remains current.

If the task changed, resume is the wrong operation. Prepare a new envelope or revise the
governed work so history shows that execution targeted a new contract.

## Handle stale output without discarding evidence

Stale work may still demonstrate a useful implementation technique or reveal a missing
impact. Keep it isolated, inspect the diff, and decide whether to revise scope, create a
new task, or reproduce the work against the current baseline. Do not copy it into the
governed checkout and then describe it as current completion evidence.

The distinction protects both safety and learning. Rejecting automatic import does not
require pretending the attempt never happened.

## Evidence validity is its own axis

A current envelope can return invalid evidence: malformed structured output, an
out-of-target path, an unreadable artifact, or a missing expected result. A stale envelope
can return technically excellent code. Freshness and quality are separate gates, and
neither grants acceptance by itself.

## Make failure actionable

Stale validation should identify the mismatched identity, expected and observed digests,
affected source, and safe next actions. A generic “invalid result” encourages users to
bypass the guard. Precise diagnostics let them decide whether to revise, rerun, or retain
the attempt only as supporting evidence.

## Takeaway

Portable execution needs freshness as well as identity. Bind every envelope to the
exact task digest, revalidate it when results return, and treat stale work as a review
case rather than current completion evidence.

## Canonical CIS sources

- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [`cis agent prepare`](../manual/cis_agent_prepare.md)
- [`cis agent import-result`](../manual/cis_agent_import_result.md)
- [`cis agent resume`](../manual/cis_agent_resume.md)
- [Provider-neutral agent execution](../specs/features/agent-execution-coordination-feature.md)
