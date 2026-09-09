---
title: "Recording Completion Evidence That Survives the Conversation"
type: article
status: Draft
series: "Verification and Assurance"
series_order: 6
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on completion-evidence change
summary: "Exact commands, artifacts, outcomes, deferrals, and residual risk belong in durable task and verification records."
cis:
  stable_id: change-impact-studio:article:completion-evidence-survives-conversation
---

# Recording completion evidence that survives the conversation

Chat histories are poor completion records. They are provider-specific, difficult to
review as a diff, and easily separated from the commit they describe.

## Evidence needs exact identity

A completion row should identify the change, task, baseline, check, exact command or
artifact, result, and notes. “Tests passed” is weaker than the project, arguments,
configuration, and observed outcome.

## Record absence honestly

Unavailable browser checks, skipped integrations, environment limitations, and failed
commands belong in the record with owners and follow-up. Silence is not a pass.

## Preserve discrepancies and risk

Unexpected paths, missing planned work, deferrals, and residual risk remain visible at
acceptance. The reviewer rationale explains why the current evidence is sufficient.

## Keep detail proportional

Disposable logs can hold bounded output. The canonical verification record summarizes
the exact evidence and points to artifacts without copying every build line into Git.

Local artifacts may be retained, archived, compacted, restored, or retrieved through an
integrity-checked manifest. Their hashes and locators keep the evidence traceable, but the
canonical record must remain intelligible after local artifacts expire or are removed.

## Separate the claim from its proof

An executor may claim “the API remains compatible.” The durable record should identify
the comparator, current contract, every supported baseline, exact invocation, result,
and retained artifact or digest. Another reviewer can then assess the same claim without
trusting the original conversation.

The principle applies to manual evidence too. “Security reviewed” is incomplete without
the reviewer, scope, findings, exceptions, rationale, and date.

## Record enough context to reproduce meaning

A useful evidence entry normally includes:

- stable change and task IDs;
- approved baseline and candidate identity;
- obligation or acceptance criterion being tested;
- tool, version, arguments, configuration, and environment where material;
- start, finish, exit and evidence-validity state;
- artifact locator and digest;
- truncation, skipped, or unavailable conditions;
- finding or result summary; and
- reviewer disposition and residual risk when applicable.

The objective is not perfect replay of every environment. It is to prevent the evidence
from becoming an unauditable sentence after local state disappears.

## Use durable summaries for disposable detail

A CI log may contain thousands of lines and expire after retention. The canonical record
can preserve the run and job identity, exact check, bounded finding, artifact hash,
retention limitation, and outcome. If the raw log is later unavailable, readers still
know what evidence existed and what it claimed.

Important conclusions should not depend on a path available only on one maintainer's
machine. Local artifact inventory and archives improve recovery, but canonical acceptance
must remain intelligible without them.

## Preserve negative evidence

Failures, skips, timeouts, invalid results, and unavailable tools are part of the record.
They show the boundary of what was demonstrated. Re-running a check later creates new
evidence; it should not erase the earlier failed attempt when that history matters to the
decision.

The same applies to expected-but-missing and unexpected paths. Acceptance rationale may
dispose them, but the discrepancy remains traceable.

## Avoid evidence dumping

Committing every console line makes review harder and can expose secrets or environment
data. Preserve structured results and bounded diagnostic artifacts under the applicable
retention policy. Promote only the durable facts needed to explain validation and risk.

Redaction must not turn a known-sensitive source into acceptable content. Credentials and
private keys remain invalid evidence inputs, not text to sanitize optimistically.

## Design for a future reviewer

Ask whether someone six months later can tell what was checked, against which version,
what failed or was unavailable, who accepted the result, and where the remaining risk
went. If the answer depends on access to the original agent session, issue comments, or a
developer's memory, the evidence is not yet durable.

## Test the record by removing local state

Imagine that the agent session, terminal scrollback, temporary worktree, and `.cis/local/`
directory are gone. The repository should still show the approved obligation, exact
evidence summary, result, reviewer, deferrals, and residual risk. Where an external
artifact was essential, the record should identify it and its retention limit honestly.

This deletion test exposes whether durable meaning has leaked into convenience stores.

## Takeaway

Write evidence so another reviewer can understand what was checked after the chat,
agent, or local run state is gone. Durable acceptance depends on durable evidence identity.

## Canonical CIS sources

- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [`cis verify evidence`](../manual/cis_verify_evidence.md)
- [Local artifact retention and recovery](../specs/local-artifact-retention-spec.md)
