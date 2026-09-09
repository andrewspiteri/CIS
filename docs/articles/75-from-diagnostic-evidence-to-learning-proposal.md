---
title: "From Diagnostic Evidence to a Reviewed Learning Proposal"
type: article
status: Draft
series: "Product Evolution and Learning"
series_order: 5
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
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
flags. CIS refuses sensitive inputs and bounds every read. `cis diagnostics doctor`
checks the profile and sources before analysis, while `cis diagnostics export` produces
a bounded diagnostic package when evidence must be reviewed elsewhere.

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

## Diagnose source health before analysis

`cis diagnostics doctor` can validate configured source paths, enablement, sensitivity,
containment, readability, formats, and bounds. A missing source should not silently reduce
the evidence set; a sensitive source should be rejected rather than read and redacted
optimistically.

The profile makes the diagnostic boundary reviewable before a model or correlator sees
any event.

## Normalize events into bounded facts

Raw logs differ across tools and may contain source, commands, identifiers, or secrets.
Analysis extracts only the permitted fields needed for patterns such as failure class,
component, time window, duration, retry count, or result validity. Defense-in-depth
redaction applies after source admission, not instead of it.

An export can package bounded normalized evidence with manifest and hashes for review. It
should disclose omissions, truncation, and source identity without reconstructing raw
content.

## Correlate without inventing causality

Suppose graph builds fail frequently after repository imports and usage feedback shows
repeated `repo doctor` invocations. Correlation can identify the pattern. It cannot prove
that import caused every graph failure. The proposal should describe the observation,
confidence, competing explanations, and evidence required to confirm the cause.

This is more useful than a confident generic recommendation such as “make graph builds
more reliable.”

## Form one actionable proposal

A bounded proposal might state:

> After confirmed multi-repository import, run a pre-build registry and documentation-root
> consistency check and surface the affected repository before graph mutation.

It names the trigger, expected behavior, benefit, evidence, and affected capability. It
does not include source patches or approve a new policy.

## Review privacy and control impact

The reviewer checks whether the recommendation needs more data collection, broadens remote
transmission, weakens a validation gate, conflicts with a standard, or moves authority.
An efficiency improvement is not acceptable if it makes failure less visible.

Accepted proposals enter canonical learning history with reviewer, rationale, timestamp,
source digest, and bounded recommendation. Rejected proposals retain useful review history
in derived state.

## Deliver through an ordinary change

Implementation receives its own baseline, impact, decisions, plan, tests, documentation,
and verification. The final product result can then be measured against the diagnostic
pattern that motivated it. If failures do not improve, revisit the causal assumption
rather than declaring learning complete.

## Measure whether the learning helped

After the governed change ships, compare the same bounded operational measures: failure
rate, diagnosis steps, output size, recovery time, or repeated usage. Preserve environment
and version differences and avoid claiming causality from one improved run.

If the result worsens another control—perhaps faster recovery hides collisions—open a new
learning proposal with the conflicting evidence. Reviewed learning is current product
knowledge, not an irreversible declaration that the first diagnosis was correct.

## Takeaway

Use bounded diagnostics to produce traceable recommendations, not autonomous policy.
Preserve privacy, evidence, confidence, and human review at every promotion step.

## Canonical CIS sources

- [Diagnostics profile](../references/diagnostics-profile.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [`cis learn collect`](../manual/cis_learn_collect.md)
- [`cis learn propose`](../manual/cis_learn_propose.md)
- [`cis diagnostics doctor`](../manual/cis_diagnostics_doctor.md)
- [`cis diagnostics export`](../manual/cis_diagnostics_export.md)
