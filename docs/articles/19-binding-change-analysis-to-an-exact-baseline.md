---
title: "Binding Change Analysis to an Exact Baseline"
type: article
status: Draft
series: "Change Impact and Planning"
series_order: 2
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on baseline or graph-identity change
summary: "Why impact and verification must refer to the same exact Git or graph state."
cis:
  stable_id: change-impact-studio:article:change-analysis-exact-baseline
---

# Binding change analysis to an exact baseline

“What does this change affect?” has no stable answer without a starting state.

Dependencies move, contracts evolve, tests appear, and architecture decisions change.
An impact analysis performed today may not describe the repository a week later.

## Baselines make findings reproducible

CIS binds a change proposal to an exact Git commit or graph build. Every finding and
plan can then be interpreted against a known repository state.

A Git commit is appropriate when source history exists. A graph build can provide an
exact identity for an unborn repository or a federated workspace where graph evidence
is the relevant snapshot.

## Analysis cannot silently follow HEAD

If a tool always analyses the current checkout, rerunning it can change reviewed scope
without a visible decision. New files, moved contracts, or changed dependencies become
part of an old change.

CIS refuses to mix an existing dossier with a different graph generation. A reviewer
must decide whether to rebuild the analysis, explicitly rebaseline, or start a new
change.

## Rebaseline is a narrow escape hatch

Legitimate source additions can occur immediately after creating a proposal. CIS allows
an actor-and-rationale-audited rebaseline only before findings or generated work exist.

Once review evidence exists, rebaseline is blocked. A new change or explicit scope
revision preserves the historical boundary rather than rewriting it.

## Verification returns to the same reference point

The baseline is not only for analysis. Verification compares actual Git state with the
approved targets and expectations established from that baseline.

This closes the loop:

```text
Exact baseline
  → reviewed impact
  → approved work
  → candidate implementation
  → actual diff from the same baseline
```

Without the shared baseline, “unexpected change” is undefined.

## Takeaway

Baseline identity turns impact from a temporary search result into reproducible
engineering evidence. Bind the proposal, findings, plan, and verification to the same
starting point, and make any movement explicit.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [`cis change rebaseline`](../manual/cis_change_rebaseline.md)
- [Context model and local graph](../specs/context-model-and-graph-spec.md)

