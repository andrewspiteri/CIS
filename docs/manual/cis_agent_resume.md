---
title: "cis agent resume"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-28"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-resume
---

# `cis agent resume`

Existing-system `PRODUCT/TECHNICAL-INTENT-DRAFT` runs support the same guarded continuation
as BRD drafts. The canonical technical intent, BRD, questionnaire, original context and exact
snapshots must remain unchanged. Copy-back again checks protected decisions and the one-file scope.

Existing-system `PRODUCT/SOLUTION-DESIGN-DRAFT` runs can resume against the same two-file
architecture bundle and immutable evidence. The original overall design, component sheet,
BRD, technical intent, questionnaire and snapshots must still match. CIS rechecks coverage,
component identities, human notes, lifecycle and diagram validity before applying the pair.

Create a new retained attempt using a provider session from a terminal run.

```text
cis agent resume <run-id> --actor <identity> --reason <rationale> [--message <continuation>] [--approve-requests] [--repo <path>] [--format <human|json|agent>]
```

The provider must support resumption, the original worktree and unchanged envelope must remain available, and the prior run must be terminal. CIS sends the complete digest-bound task contract and artifact route on every attempt, followed by the optional continuation, so a failed pre-session attempt cannot resume with an unbounded or context-free prompt. The next attempt appends provenance and never erases the first result. Ctrl+C follows the same bounded cancellation behavior as `cis agent run`.

A `PRODUCT/BRD-DRAFT` run can also resume after a provider interruption or a copy-back
rejection, such as a missing coverage citation. CIS verifies that the canonical BRD still
matches the original input, the unexpired envelope and run evidence remain valid, all
context and implementation snapshots are unchanged, and the isolated diff contains only
the BRD. Use a bounded continuation explaining the exact correction. The new attempt uses
the same provider session and snapshots, then passes through every normal BRD copy-back
check. It cannot overwrite a changed canonical document or expand the evidence selection.
Other product-document tasks continue through their dedicated governed commands.
