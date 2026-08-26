---
title: "When Deterministic Tools Should Replace Model Calls"
type: article
status: Draft
series: "Human and Agent Execution"
series_order: 5
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on AI-routing or deterministic-tool change
summary: "Use models for interpretation where needed, not for structural facts and successful workflows that deterministic tools can establish."
cis:
  stable_id: change-impact-studio:article:deterministic-tools-replace-model-calls
---

# When deterministic tools should replace model calls

Models are useful when a task requires interpretation, synthesis, or explanation. They
are a poor default for facts a deterministic tool can establish exactly.

## Structural questions have structural answers

Use deterministic routes for questions such as:

- Does the catalog contain duplicate IDs?
- Which projects reference this package?
- Did the build pass?
- Which files changed from the baseline?
- Does the plan contain a dependency cycle?
- Is the OpenAPI change compatible with supported baselines?
- Does a public handler access persistence directly?

A model can discuss these questions, but its answer should not replace the scanner,
compiler, parser, Git query, or validator that can prove them.

## Successful workflows rarely need explanation

A passing deterministic workflow can return its result without a model call. A failed
workflow may benefit from a compact explanation after raw evidence is preserved and
sensitive content is removed.

This creates sensible routing:

```text
Fact or validation         → deterministic tool
Compact failure summary   → local model when useful
Complex permitted analysis → authorized remote model
Policy or risk decision   → human
```

## Determinism improves more than cost

Avoiding unnecessary model calls reduces latency and spend, but the larger benefits are
reproducibility, privacy, testability, and clear failure semantics. A validator can have
stable exit codes and regression tests. A model response varies with provider and prompt.

## Models still have a role

Models can propose documentation, identify likely overlap, summarize bounded evidence,
or surface questions that deterministic rules do not represent. Their output keeps
provenance and advisory state until reviewed.

## Takeaway

Use the strongest deterministic evidence available before asking a model to interpret
the repository. Reserve probabilistic reasoning for work that genuinely needs it.

## Canonical CIS sources

- [AI routing profile](../references/ai-routing-profile.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Feedback loop](../specs/feedback-loop-spec.md)

