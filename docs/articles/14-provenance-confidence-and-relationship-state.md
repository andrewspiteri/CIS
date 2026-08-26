---
title: "Provenance, Confidence, and Relationship State"
type: article
status: Draft
series: "Repository Knowledge and Context"
series_order: 5
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
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

## Confidence is not authority

Confidence describes how strongly the evidence supports the proposal. It does not grant
the right to establish product meaning.

A compiler-observed invocation may have high confidence as an implementation fact. It
still cannot prove that the call is desired behavior. A model may assign high confidence
to a likely ownership relationship, but only the relevant human or canonical declaration
can confirm that ownership.

Confidence helps route review; authority decides meaning.

## Relationship state preserves review

Useful states include discovered, proposed, confirmed, deprecated, and rejected. They
let the graph retain evidence without prematurely flattening it into fact.

- **Discovered** identifies deterministic implementation evidence.
- **Proposed** identifies an inference awaiting review.
- **Confirmed** records reviewed or canonical meaning.
- **Rejected** preserves why a plausible relationship was excluded.
- **Deprecated** retains history while indicating it should not drive current planning.

Rebuilds should preserve reviewed state when the underlying stable identities and
evidence remain current.

## Evidence should be inspectable

An edge should point to the file, table row, symbol, workflow, or declaration that
supports it. “The model said so” is insufficient provenance. A model-assisted proposal
needs the source set and route that produced the interpretation, while prompts and
responses may remain excluded for privacy.

This lets a reviewer challenge the evidence rather than argue with a graph label.

## Downstream behavior should respect strength

Impact analysis may include a proposed relationship but mark lower confidence and stop
automatic authority carry-forward. A context query may show the edge while making its
state visible. Verification should not claim a requirement is covered by a test based
only on naming similarity.

The graph becomes safer when every consumer uses evidence strength rather than treating
edge existence as proof.

## Takeaway

Relationships are engineering claims. Record who or what produced them, where the
evidence lives, how strong it is, and whether it has been reviewed.

Provenance makes a graph auditable. Confidence makes uncertainty visible. Relationship
state prevents inference from silently becoming authority.

## Canonical CIS sources

- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [Traceability matrix specification](../specs/traceability-matrix-spec.md)
- [Standards governance](../specs/standards-governance-spec.md)

