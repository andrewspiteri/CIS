---
title: "Designing Feedback Without Collecting Source or Prompts"
type: article
status: Draft
series: "Product Evolution and Learning"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
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
estimated tokens, and defensible savings fields.

## Exclude content

The ledger omits option values, command output, prompts, responses, source, credentials,
and model keys. Recording failure must never change the command's result.

## Keep the ledger local and disposable

`.cis/local/feedback/tool-usage.jsonl` is excluded from Git. Canonical task evidence can
capture aggregate counts and a digest without copying sensitive detail.

## Be honest about estimates

When no defensible counterfactual exists, possible savings remain zero. Coverage and
confidence prevent estimated commands from representing the whole tool.

## Takeaway

Collect the minimum metadata needed to improve reliability and routing. Keep content out,
preserve estimate basis, and make feedback failure operationally harmless.

## Canonical CIS sources

- [Feedback loop](../specs/feedback-loop-spec.md)
- [`cis feedback usage`](../manual/cis_feedback_usage.md)
- [`cis feedback opportunities`](../manual/cis_feedback_opportunities.md)

