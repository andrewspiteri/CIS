---
title: "Designing Feedback Without Collecting Source or Prompts"
type: article
status: Draft
series: "Product Evolution and Learning"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on feedback privacy change
summary: "Measure command reliability, latency, output, and routing opportunities with sanitized metadata instead of repository content."
cis:
  stable_id: change-impact-studio:article:feedback-without-source-prompts
---

# Designing feedback without collecting source or prompts

An engineering tool needs evidence about reliability and cost. Collecting raw commands,
prompts, responses, and source would create a privacy and governance problem larger than
the feedback benefit.

## Record operational metadata

CIS records command path, option names, timestamps, duration, exit code, output sizes,
estimated tokens, and defensible savings fields. Agent, CI, diagnostics, and local artifact
records may provide separate operational evidence, but they do not broaden what the
feedback ledger is allowed to collect.

## Exclude content

The ledger omits option values, command output, prompts, responses, source, credentials,
and model keys. Recording failure must never change the command's result.

## Keep the ledger local and disposable

`.cis/local/feedback/tool-usage.jsonl` is excluded from Git. Canonical task evidence can
capture aggregate counts and a digest without copying sensitive detail.

## Be honest about estimates

When no defensible counterfactual exists, possible savings remain zero. Coverage and
confidence prevent estimated commands from representing the whole tool.

## Begin with the improvement question

Collect a field only when it supports a defined operational question: Which commands fail
repeatedly? Where does output become too large? Which deterministic route could replace a
model call? Which capability has high latency or low cache reuse?

“It may be useful later” is not enough to justify storing repository content or prompts.
Data minimization is easier when every field has an owner and purpose.

## Use content-free event shapes

A usage event can record command identity, option names without values, start and finish,
duration, exit class, output byte counts, estimated tokens, provider and model identity
where applicable, cache state, and a defensible savings basis. It does not need the search
term, file path value, prompt, response, source excerpt, or console output.

Rare combinations can still reveal sensitive behavior, so retention and access remain
bounded even when content is excluded.

## Separate feedback from task evidence

The local feedback ledger measures tool operation. A canonical task may need exact command
and artifact evidence for acceptance. Those are different stores with different purposes.
Do not copy a content-free usage event into verification and pretend it proves behavior;
do not copy detailed task evidence into the usage ledger to improve analytics.

Agent, CI, diagnostic, and artifact systems similarly retain their own bounded evidence.
Links and aggregate counts are preferable to creating one universal telemetry store.

## Make recording failure harmless

Feedback is secondary. A lock, malformed ledger, full disk, or write failure must not
change the command's domain result or corrupt canonical repository state. Record a bounded
diagnostic where possible and continue with the original exit semantics.

Concurrency and partial writes need append-safe handling so several CIS processes cannot
turn the ledger into an operational dependency.

## Estimate savings conservatively

A deterministic index result can compare its compact output with the complete selected
sources. That is a defensible directional estimate. A command with no known alternative
keeps zero possible savings and confidence `none`.

Report estimate coverage and confidence so a small measured subset is not presented as
the whole product's cost reduction.

## Keep retention local and disposable

Exclude the ledger from Git, bound its size and age, and support deletion without loss of
product meaning. Aggregate reports may guide learning proposals, but promotion requires
human review and should preserve only the conclusion and evidence basis needed for the
governed improvement.

## Review the privacy threat model

Content-free does not mean risk-free. Command timing, repository identity, provider use,
rare option combinations, and repeated failures can reveal sensitive work patterns. Keep
the ledger local, minimize identifiers, restrict access, bound retention, and avoid
central aggregation unless a separate authority and privacy review explicitly permits it.

Test that likely secrets and option values cannot enter events through error text,
serialization fallbacks, or newly added fields. Schema review should treat every expansion
as a new collection decision.

## Takeaway

Collect the minimum metadata needed to improve reliability and routing. Keep content out,
preserve estimate basis, and make feedback failure operationally harmless.

## Canonical CIS sources

- [Feedback loop](../specs/feedback-loop-spec.md)
- [`cis feedback usage`](../manual/cis_feedback_usage.md)
- [`cis feedback opportunities`](../manual/cis_feedback_opportunities.md)
- [Local artifact retention](../references/local-artifact-retention.md)
