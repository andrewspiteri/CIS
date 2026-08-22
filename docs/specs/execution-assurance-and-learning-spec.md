---
title: "Execution, Assurance, Diagnostics, and Learning"
type: specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on execution or authority change"
cis:
  stable_id: change-impact-studio:spec:execution-assurance-learning
---

# Execution, assurance, diagnostics, and learning

## Authority model

Canonical Markdown owns routes, workflow definitions, task requirements, evidence,
human reviews, acceptance, and applied-learning history. Derived envelopes, run state,
model cache, sanitized usage, verification snapshots, diagnostic analysis, and learning
proposals live under `.cis/local/` and can be deleted and rebuilt.

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
retains bounded output logs.

Agent preparation binds one task's canonical path and digest into a portable envelope.
Result ingestion validates envelope identity, current task digest, structured fields, and
repository-relative changed paths. It appends an evidence row but leaves lifecycle
transition and completion to the governed plan command and human authority.

## Verification

Verification captures an exact Git-baseline file snapshot, compares observed files with
task targets, and validates plan, design, task, and evidence gates. Evidence rows name the
exact task, check, command or artifact, result, and notes. Final verification acceptance
requires a clean validation plus a human reviewer and rationale.

## Diagnostics and learning

The diagnostics profile lists repository-relative sources and explicit enabled/sensitive
flags. CIS refuses sensitive sources, bounds every read, performs defense-in-depth
redaction, and persists only normalized derived analysis.

Learning collection aggregates sanitized feedback and diagnostic counts. Proposals remain
derived until human approval. Application promotes only the reviewed recommendation,
reviewer, rationale, timestamp, and source digest into canonical learning history; it does
not self-modify source code, instructions, policies, approvals, or acceptance.
