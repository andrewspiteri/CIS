---
title: "When Deterministic Tools Should Replace Model Calls"
type: article
status: Active
series: "Human and Agent Execution"
series_order: 5
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
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

CIS also records deterministic toolkit applicability and evidence. A formatter, build
target, test runner, contract comparator, or policy scanner should be selected from
repository evidence and invoked through its exact argument contract rather than being
reconstructed as model advice.

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

Where a model route is required, qualification evidence can probe availability, benchmark
the named capability, and bind approval to an exact provider/model result. Model suitability
is therefore governed separately from the decision to use a model at all.

## Choose the evidence mechanism first

Before selecting a model, ask what would prove the claim. If the answer is a parser,
compiler, schema validator, Git comparison, test runner, or exact calculation, run that
mechanism first. A model can later explain the result, but it should not stand in for the
evidence-producing system.

This reverses a common workflow in which a model is asked to inspect broadly and then
suggests commands that may or may not run. Deterministic discovery narrows the facts;
probabilistic interpretation addresses the remaining ambiguity.

## A practical routing table

| Need | Primary route | Optional model role |
|---|---|---|
| Catalog or schema health | Parser and validator | Explain a cluster of failures |
| Actual changed paths | Git | Summarize impact for a reviewer |
| Symbol and call relationships | Compiler or supported extractor | Propose meaning for unresolved links |
| Contract compatibility | Version-aware comparator | Explain migration options |
| Test outcome | Test runner and result adapter | Summarize bounded failures |
| Product ambiguity | Human authority with source evidence | Draft questions and alternatives |
| Risk acceptance | Human reviewer | Organize evidence, never decide |

The table avoids two errors: using a model where exact proof exists and using a rigid
rule where stakeholder judgment is required.

## Preserve raw evidence before explanation

When a build fails, retain the command, exit state, structured result, and bounded logs
before asking a model to summarize. The summary should point to that evidence and disclose
its input boundary. If model routing is unavailable, the failure remains fully usable.

A successful workflow normally needs no narrative. Automatically generating celebratory
prose adds latency and a new failure mode without improving assurance.

## Deterministic does not mean trustworthy by default

Tools also need governance. A scanner may be misconfigured, a parser may ignore an
unsupported construct, and a test adapter may accept a zero exit code without the
expected artifact. CIS records applicability, version, arguments, result semantics, and
known limits so “the tool passed” can be evaluated.

Deterministic evidence is strong because it is reproducible within a declared contract,
not because every executable is automatically authoritative.

## Use models where semantics remain open

Models are well suited to synthesizing several bounded sources, proposing missing
documentation, clustering diagnostics, comparing architectural options, or drafting an
explanation for human review. These activities require interpretation rather than exact
enumeration.

The output should keep source provenance, route identity, and advisory lifecycle. A useful
proposal becomes canonical only through the authority that owns its meaning.

## Measure replacement honestly

Replacing a model call can reduce tokens, latency, privacy exposure, and variability.
Savings claims need a defensible counterfactual. If CIS cannot estimate what the prior
route would have used, it should report no quantified savings rather than inventing an
impressive number.

Correctness remains the primary metric. A cheap deterministic command that answers the
wrong question is not an improvement.

## Takeaway

Use the strongest deterministic evidence available before asking a model to interpret
the repository. Reserve probabilistic reasoning for work that genuinely needs it.

## Canonical CIS sources

- [AI routing profile](../references/ai-routing-profile.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Feedback loop](../specs/feedback-loop-spec.md)
- [Deterministic toolkit evidence](../specs/deterministic-toolkit-evidence-spec.md)
- [AI model qualification](../specs/ai-model-qualification-spec.md)
