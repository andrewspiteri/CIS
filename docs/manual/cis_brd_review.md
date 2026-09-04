---
title: "cis brd review"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-01"
review_cadence: "on BRD review-governance change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-review
---

# `cis brd review`

Turns advisory agent review findings into an exact, human-approved BRD remediation scope.
The canonical Markdown record is stored at
`<documentation-root>/reviews/brd/<review-run-id>.md`.

```text
cis brd review init <review-run-id> [--workspace <path>]
cis brd review status <review-run-id> [--workspace <path>]
cis brd review freshness <review-run-id> [--workspace <path>]
cis brd review decide <review-run-id> <finding-id>
  --decision accepted --actor <human>
  [--approved-recommendation <edited-text>]
  [--workspace <path>]
cis brd review decide <review-run-id> <finding-id>
  --decision rejected --actor <human> --reason <rationale>
  [--workspace <path>]
cis brd review accept-all <review-run-id>
  --actor <human> [--workspace <path>]
cis brd review approve <review-run-id>
  --reviewer <human> [--workspace <path>]
```

All commands support `--format human|json|agent`.

`init` requires a successful, digest-valid `PRODUCT/BRD-REVIEW` run bound to the current
BRD. It copies each structured finding into a canonical Markdown disposition document as
`pending`; it does not decide anything. Repeating `init` is idempotent and never replaces
human-managed decisions.

`freshness` validates the successful review manifest, structured result, and retained
read-only BRD snapshot, then compares that reviewed baseline with the canonical BRD. It
returns `current` for an exact match, `question-answers-only` when only answers and their
human provenance changed in the governed Open questions table, and `stale` for every other
content change. Both `current` and `question-answers-only` are compatible with the review;
changing a question identity or its text is substantive and returns `stale`.

`decide` handles one finding at a time. For the normal path, the human either approves the
reviewer's recommendation as written or supplies the exact edited remediation text through
`--approved-recommendation`. Approval requires a named human actor but no free-text rationale:
the approved text is the authority. The backward-compatible rejection path remains available
for an exceptional guardrail and requires a rationale. The final individual decision
mechanically locks the exact recommendation set by digest and returns `approved`; it does not
ask the human to approve the same scope again.

`accept-all` approves every still-pending recommendation exactly as written in one atomic
operation. Recommendations that the human previously modified and approved remain unchanged.
The command records one actor and timestamp for the batch and immediately locks the complete
set by digest. Its normal client presentation is a consolidated list: the human can inspect
every observation and recommendation before choosing the batch action.

`approve` remains as a recovery command for complete legacy disposition records created before
automatic locking. It fails while any finding is pending. The approved set is immutable; a new
independent review is required to replace it. Agents cannot run these commands on behalf of a
human or acquire disposition authority.

Apply the approved set with `cis agent revise brd`. Legacy rejected findings remain visible
guardrails for backward compatibility.
