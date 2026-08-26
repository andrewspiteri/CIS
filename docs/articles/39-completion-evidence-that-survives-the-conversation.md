---
title: "Recording Completion Evidence That Survives the Conversation"
type: article
status: Draft
series: "Verification and Assurance"
series_order: 6
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
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

## Takeaway

Write evidence so another reviewer can understand what was checked after the chat,
agent, or local run state is gone. Durable acceptance depends on durable evidence identity.

## Canonical CIS sources

- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [`cis verify evidence`](../manual/cis_verify_evidence.md)

