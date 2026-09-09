---
title: "Binding Change Analysis to an Exact Baseline"
type: article
status: Draft
series: "Change Impact and Planning"
series_order: 2
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
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

## An exact baseline is a set of identities

A useful baseline is more than the word `main` or “latest.” It identifies the concrete
commit or graph generation, the participating repositories, relevant input digests, and
the configuration that shaped discovery. Branch names move; exact identities do not.

In a multi-repository workspace, each participant can have its own Git and graph identity.
The authority records the set used for analysis, including unavailable or stale
participants. A workspace label without those constituent identities is not reproducible.

## Baseline drift has several forms

Not every movement has the same consequence:

- a source file changed after analysis;
- a canonical requirement or contract changed digest;
- a graph was rebuilt with a new extractor version;
- an owned repository was added or reclassified;
- a dependency became unavailable;
- the product-definition activation changed; or
- an approved decision was superseded.

Some movements affect only derived provenance; others change the meaning or coverage of
the plan. CIS should report the evidence rather than infer that a newer identity is always
equivalent.

This refusal protects earlier decisions. Imagine that a new consumer is registered after
impact was approved. Silently following the newest graph would add scope without review;
silently keeping the old graph without a warning would hide the new evidence. Staleness
forces the change authority to choose deliberately.

## Choose the response to movement

| Situation | Likely response |
|---|---|
| No reviewed work exists and the source addition is part of the intended starting state | Audited narrow rebaseline |
| The outcome remains the same but new evidence changes affected scope | Re-run impact and review the revised scope |
| Product intent or a controlling contract changed materially | Reconsider or replace the change |
| Unrelated repository activity moved HEAD | Keep the exact baseline and isolate the candidate diff |
| A dependency changed under another authority | Record coordination and assess compatibility evidence |

The choice belongs to the change authority because it can invalidate earlier reasoning.
Rebaseline is safest before the dossier has acquired meaning beyond its proposal. For
example, a maintainer may commit an omitted starter document immediately after creating
the change. If no findings, decisions, or tasks exist, an audited rebaseline can preserve
the intended work. After findings have been dispositioned, the same action would rewrite
what the reviewer actually assessed.

## Baselines improve diagnosis, not only audit

When a failure appears, a precise baseline helps answer whether the change introduced it,
whether it already existed, or whether the environment moved. A provider attempt can be
replayed from the same starting snapshot. A contract diff can compare the intended
versions rather than whichever artifacts happen to be installed. Review becomes faster
because participants are discussing the same system state.

The comparison also needs a clear end state. Verification observes the candidate
workspace or commits and subtracts the exact baseline. Planned paths are expectations;
Git supplies actual changes. Tests and contracts add behavioral evidence, but none can
repair an ambiguous starting point.

## Record limits with the identity

An exact identifier does not imply complete coverage. The baseline should preserve known
limitations: unsupported extractors, unavailable repositories, excluded sensitive paths,
or truncated discovery. Exact incomplete evidence is still incomplete. Its advantage is
that the limitation can be reviewed and reproduced rather than forgotten.

## Takeaway

Baseline identity turns impact from a temporary search result into reproducible
engineering evidence. Bind the proposal, findings, plan, and verification to the same
starting point, and make any movement explicit.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [`cis change rebaseline`](../manual/cis_change_rebaseline.md)
- [Context model and local graph](../specs/context-model-and-graph-spec.md)
