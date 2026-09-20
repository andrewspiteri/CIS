---
title: "cis technical-intent status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-11"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-technical-intent-status
---

# `cis technical-intent status`

```text
cis technical-intent status [--workspace <path>] [--format <human|json|agent>]
```

Reports `Missing`, `Review Required`, `Ready for Approval`, `Active`, or `Stale` from
canonical content, BRD and participant baselines, decisions, and approval evidence.
An inactive or changed BRD and unavailable or stale participant graphs make an existing
technical intent non-current; they are reported as lifecycle warnings rather than as
structural command errors. Thus an otherwise valid approved document becomes `Stale`.
Status is read-only. `cis change create`, `cis plan build`, `cis plan import-spec`, and `cis plan derive`
enforce Active/current readiness when run in a workspace authority.

The structured `validation.decisions` list projects the same decision checks used by
validation: ID, decision, required gate, recorded status and rationale, `needsReview`,
outstanding issues, and `documentLine` for a unique row in the original Markdown. A
missing or ambiguous line is `null`; clients can open the document without assuming a
row position. These are document decisions, separate from questionnaire answers.
Reading status never resolves, defers, or approves them.

Each unambiguous row includes a `reviewToken` for safe saving. Known scaffold questions link
current recorded questionnaire answers through `relatedAnswers`, with `questionnaireOverlap`,
`remainingReview` explaining any remaining policy. Every decision receives advisory
`suggestedResolution` text for automatic prefilling, including additional and newly discovered
choices. Suggestions use current answers and local topic-specific proposals; they are not
recorded choices. Saved
wizard resolutions expose separate `recordedResolution`, `recordedReason` and `recordedBy`
fields when their audit still matches the row. Use
[`cis technical-intent decisions resolve`](cis_technical_intent_decisions_resolve.md) or the
wizard's single Answer field to record a human resolution; reading these suggestions records nothing.
