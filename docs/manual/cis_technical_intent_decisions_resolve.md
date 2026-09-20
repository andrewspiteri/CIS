---
title: "cis technical-intent decisions resolve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-technical-intent-decisions-resolve
---

# `cis technical-intent decisions resolve`

```text
cis technical-intent decisions resolve <id> --input <json-file> --actor <human> [--workspace <path>] [--format <human|json|agent>]
```

Records one human answer in the canonical technical-intent decision row's rationale.
The high-level wizard automatically prefills one editable **Answer** field, then saves through
this command and refreshes readiness. The selected row becomes `Resolved`.
Other decisions, authored narrative, source baselines and prior approval metadata are preserved.
Changing approved content makes that approval stale under the existing digest checks.

Read `technical-intent status --format json` for `validation.decisions`. Recognized scaffold
questions expose `relatedAnswers`, `questionnaireOverlap`, `remainingReview` and a suggested
resolution assembled from current questionnaire answers and advisory direction for the topic.
Additional choices receive topic-specific proposals, with a general review proposal for unknown
topics. CIS generates these locally whenever status is read; no manual document seeding or
model process is needed. Matching uses question wording, never the `TI-DEC-*` number or a product
name. Human answers and repository-derived answers retain separate provenance. Specific product
policy decisions are not automatically treated as duplicates.

Review the prefilled suggestion, include any context or exception in that same answer, and save.
Prefilling does not resolve or approve anything. Saved answers and unsaved edits take precedence
over suggestions. Previous separately entered reason text is preserved in the answer when the
form is upgraded. Unsaved edits survive refreshes, with explicit review required when their
underlying decision or questionnaire changes.

The input file is a JSON object, at most 256 KiB:

```json
{
  "resolution": "Keep the existing component boundaries and contract ownership to preserve the reviewed architecture and current product scope.",
  "reviewToken": "<copy this decision's token from current status>"
}
```

Resolution is limited to 16,384 characters and actor to 200. A separate `reason` remains optional
for older clients, limited to 4,096 characters. Empty answers or identities, placeholders and
HTML comment markers are rejected. The token binds the row, questionnaire and
business baseline. A changed or ambiguous row is rejected without writing. The command takes
a local write lock and rechecks file and questionnaire contents before atomically replacing
the selected document. Actor, time, answer and any explicitly supplied separate reason are retained in a
row-bound audit comment. Manual row edits invalidate that audit projection.

Saving a decision remains available during technical document review, even while unrelated
readiness findings remain. It never approves the document or waives downstream gates. JSON
reports `status`, `applied`, `errors`, and the existing technical-intent result fields. A successful
save exits 0; invalid input or conflicting evidence exits 2; unavailable write access exits 5;
a missing canonical document exits 4. Refresh status to inspect remaining decisions and readiness.
