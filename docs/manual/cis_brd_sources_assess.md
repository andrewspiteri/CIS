---
title: "cis brd sources assess"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-11"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-sources-assess
---

# `cis brd sources assess`

Records explicit human source decisions without editing Markdown table rows manually.
The Business Definition wizard uses this command for **Save decisions**. An agent must
not select assessments or invent reasons on the user's behalf.

```text
cis brd sources assess --input <decisions.json> [--workspace <path>] [--format <human|json|agent>]
```

The input is an array of 1–100 decisions. Obtain each source ID and `reviewToken` from
the current `brd status` validation's `sourceReviews` or the business page's guidance in
`definition status`. Supply the exact human-selected assessment and reason:

```json
[
  {
    "id": "<current source ID>",
    "assessment": "Reference",
    "reason": "<the human's explanation>",
    "reviewToken": "<current source review token>"
  }
]
```

Assessment is `Adopted`, `Reference`, or `Rejected`. Each reason must be substantive,
at most 4096 characters, and contain no HTML comment markers. The input file is bounded
to 1 MiB. Pipes are escaped and line breaks are flattened in the Markdown cell.

The command verifies discovered evidence and every requested source row before writing.
A stale, duplicate, unknown, malformed, or unreconciled decision rejects the whole batch.
Unrelated source rows can be saved independently without invalidating other current tokens.
The write changes only the requested assessment and rationale cells, preserving source
identities, hashes, baselines, business narrative and existing approval metadata. Saving
source decisions does not approve the BRD or incorporate adopted requirements into its text.

Exit code `0` means saved or unchanged; invalid input, stale decisions or write failures
return a nonzero exit code with errors. Correct the input or refresh the checked evidence
before retrying. No automatic source assessment is performed.

In VS Code, unfinished edits survive page navigation and refresh. Changed source versions
require an explicit confirmation against the current source before retained edits can be
saved. The wizard saves edited entries together and refreshes readiness once per batch.

## Related commands

- [`cis brd status`](cis_brd_status.md)
- [`cis brd reconcile`](cis_brd_reconcile.md)
- [`cis definition status`](cis_definition_status.md)
