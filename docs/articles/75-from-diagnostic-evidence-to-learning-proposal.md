---
title: "From Diagnostic Evidence to a Reviewed Learning Proposal"
type: article
status: Draft
series: "Product Evolution and Learning"
series_order: 5
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on diagnostics or learning change
summary: "A traceable path from bounded, redacted runtime evidence to a human-approved recommendation."
cis:
  stable_id: change-impact-studio:article:diagnostics-to-learning-proposal
---

# From diagnostic evidence to a reviewed learning proposal

Runtime and workflow evidence can reveal repeated failures that source analysis cannot
see. Turning those observations into policy requires a controlled sequence.

## Configure sources explicitly

The diagnostics profile lists repository-relative sources with enabled and sensitive
flags. CIS refuses sensitive inputs and bounds every read.

## Normalize and redact

Analysis removes raw content, applies defense-in-depth redaction, and stores only the
normalized evidence needed to describe patterns.

## Correlate with sanitized feedback

Learning collection combines diagnostic counts with command outcomes, latency, and
repeated failures. It does not reconstruct prompts or source.

## Propose one bounded recommendation

A proposal identifies the observed pattern, source digest, recommendation, expected
benefit, and confidence. It remains derived until review.

## Review and promote history

A human accepts or rejects the recommendation with rationale. Application records the
approved learning in canonical history; implementation still requires a governed change.

## Takeaway

Use bounded diagnostics to produce traceable recommendations, not autonomous policy.
Preserve privacy, evidence, confidence, and human review at every promotion step.

## Canonical CIS sources

- [Diagnostics profile](../references/diagnostics-profile.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [`cis learn collect`](../manual/cis_learn_collect.md)
- [`cis learn propose`](../manual/cis_learn_propose.md)

