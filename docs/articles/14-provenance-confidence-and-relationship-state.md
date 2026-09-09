---
title: "Provenance, Confidence, and Relationship State"
type: article
status: Draft
series: "Repository Knowledge and Context"
series_order: 5
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on graph-evidence change
summary: "Why engineering relationships need evidence, extraction method, confidence, and review state instead of a single truth flag."
cis:
  stable_id: change-impact-studio:article:provenance-confidence-relationship-state
---

# Provenance, confidence, and relationship state

An engineering graph can claim that a requirement is implemented by a component, a
test verifies a symbol, or one service depends on another. The usefulness of that claim
depends on how the relationship was established.

Without provenance, every edge looks equally trustworthy.

## Several methods can produce the same relationship

A `calls` relationship might come from:

- compiler analysis;
- an explicit declaration;
- a static textual pattern;
- a naming convention;
- a model proposal; or
- human review.

The endpoints can be identical while the evidential strength is very different. CIS
records the extraction method and source location so consumers can make that distinction.

An evidence ladder is useful, provided it is not mistaken for a universal ranking:

| Evidence | Strong for | Weak for |
|---|---|---|
| Canonical declaration | Intended ownership, policy, or contract meaning | Proving implementation matches it |
| Compiler observation | A supported source-level call or symbol fact | Dynamic runtime behavior or stakeholder intent |
| Governed inventory row | Reviewed contract identity and lifecycle | Behavior outside the inventory's declared scope |
| Test extraction | Locating executable evidence | Proving the test asserts the whole requirement |
| Textual heuristic | Finding candidates cheaply | Establishing a relationship without review |
| Model proposal | Interpreting ambiguous evidence | Granting authority or deterministic certainty |

The right question is always “strong evidence for what claim?”

## Confidence is not authority

Confidence describes how strongly the evidence supports the proposal. It does not grant
the right to establish product meaning.

A compiler-observed invocation may have high confidence as an implementation fact. It
still cannot prove that the call is desired behavior. A model may assign high confidence
to a likely ownership relationship, but only the relevant human or canonical declaration
can confirm that ownership.

Confidence helps route review; authority decides meaning.

Confidence should be calibrated by extraction method and evidence quality, not by how
plausible the relationship sounds. A model's fluent explanation does not deserve a higher
score than a bounded heuristic merely because it is persuasive. Likewise, a deterministic
extractor should reduce confidence when it encounters unsupported language features or a
partial project load.

## Relationship state preserves review

Useful states include declared, discovered, proposed, confirmed, rejected, and
possibly-stale. They
let the graph retain evidence without prematurely flattening it into fact.

- **Declared** identifies an explicit canonical relationship.
- **Discovered** identifies deterministic implementation evidence.
- **Proposed** identifies an inference awaiting review.
- **Confirmed** records reviewed or canonical meaning.
- **Rejected** preserves why a plausible relationship was excluded.
- **Possibly stale** identifies a relationship whose earlier support no longer matches
  current evidence and requires warning or review.

Default traversal includes declared, discovered, and confirmed relationships. Proposed,
rejected, and possibly-stale relationships require explicit opt-in. Rebuilds should
preserve durable human dispositions when the underlying stable identities and evidence
remain current.

## States form a controlled history

Relationship state is not a simple progress bar. A discovered implementation edge may
remain discovered for its entire life because observation, not human approval, is its
correct provenance. A proposed semantic relationship may become confirmed after review.
A confirmed relationship whose source digest changes may become possibly-stale rather
than reverting silently to proposed.

Rejected relationships should normally remain queryable with their rationale. Otherwise
the same weak inference can reappear on every rebuild and consume repeated review effort.
Preserving rejection is especially useful for similar names across repositories where a
human has established that no dependency exists.

## Evidence should be inspectable

An edge should point to the file, table row, symbol, workflow, or declaration that
supports it. “The model said so” is insufficient provenance. A model-assisted proposal
needs the source set and route that produced the interpretation, while prompts and
responses may remain excluded for privacy.

This lets a reviewer challenge the evidence rather than argue with a graph label.

Inspectability also supports disagreement. Two extractors may produce conflicting owners,
or a declaration may contradict observed source. CIS should retain both claims with their
provenance and surface the conflict. Choosing whichever edge has the highest numeric
confidence would turn a calibration aid into hidden authority.

## Downstream behavior should respect strength

Impact analysis may include a proposed relationship but mark lower confidence and stop
automatic authority carry-forward. A context query may show the edge while making its
state visible. Verification should not claim a requirement is covered by a test based
only on naming similarity.

The graph becomes safer when every consumer uses evidence strength rather than treating
edge existence as proof.

Different consumers need different policies. A discovery view can show proposed and stale
edges to help a maintainer investigate. Default context routing should prefer declared,
discovered, and confirmed edges. Automatic authority carry-forward should be stricter
still, requiring an exact current source and approved boundary. Verification should demand
evidence capable of proving the specific completion claim.

## Review relationships deliberately

A reviewer should be able to answer:

1. What exact claim does this relationship make?
2. Which source and digest support it?
3. Which method produced it, and what can that method miss?
4. Is the confidence calibrated to that method?
5. Does confirming it establish new product meaning or only acknowledge an observed fact?
6. What change would make it stale?

That review is far more valuable than clicking “approve edge.” It records why the
relationship deserves its state and teaches future tooling where deterministic evidence
can replace repeated human interpretation.

## Takeaway

Relationships are engineering claims. Record who or what produced them, where the
evidence lives, how strong it is, and whether it has been reviewed.

Provenance makes a graph auditable. Confidence makes uncertainty visible. Relationship
state prevents inference from silently becoming authority.

## Canonical CIS sources

- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [Traceability matrix specification](../specs/traceability-matrix-spec.md)
- [Standards governance](../specs/standards-governance-spec.md)
