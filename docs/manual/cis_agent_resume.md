---
title: "cis agent resume"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-09"
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

Create a new retained continuation attempt from a terminal run. Session reuse is subject to the restart rules below.

```text
cis agent resume <run-id> --actor <identity> --reason <rationale> [--message <continuation>] [--timeout-seconds <seconds>] [--approve-requests] [--allow-command <command>] [--repo <path>] [--format <human|json|agent>]
```

The provider must support resumption, the original worktree and unchanged envelope must remain available, and the prior run must be terminal. CIS sends the complete digest-bound task contract and artifact route on every attempt, followed by the optional continuation, so a failed pre-session attempt cannot resume with an unbounded or context-free prompt. The next attempt appends provenance and never erases the first result. Ctrl+C follows the same bounded cancellation behavior as `cis agent run`.

For a direct task under adopted engineering defaults, resume re-admits the original accounting owner against current allocation, recovery state, and provider policy. It does not replenish time or logical attempts. Without an override CIS rechecks the recorded timeout; use `--timeout-seconds` to select a shorter supported bound when status reports less capacity. The requested total bound is at least 30 seconds and no more than the current profile maximum (3,600 seconds in the reviewed profile) or the capacity remaining after task/change restoration reserves. Known preparation consumes part of that reservation; the provider receives any positive remainder, including fractional seconds, with startup and idle limits bounded by it. A reason, message, provider-attempt increment, or `--approve-requests` grants no allocation authority. Use [`cis agent work`](cis_agent_work.md) for owner-specific holds, progress and permitted in-scope continuation. Product-document resumptions retain their existing governed flow.

A `PRODUCT/BRD-DRAFT` run can also resume after a provider interruption or a copy-back
rejection, such as a missing coverage citation. CIS verifies that the canonical BRD still
matches the original input, the unexpired envelope and run evidence remain valid, all
context and implementation snapshots are unchanged, and the isolated diff contains only
the BRD. Use a bounded continuation explaining the exact correction. The new attempt uses
the same provider session and snapshots, then passes through every normal BRD copy-back
check. It cannot overwrite a changed canonical document or expand the evidence selection.
Other product-document tasks continue through their dedicated governed commands.

For task implementations, `--allow-command` can authorize individual commands for this attempt
with a supporting provider. Previous lists are not reused. If this or the previous attempt has
an explicit command list, CIS starts fresh native session context while retaining the task
contract and continuation evidence. This option is unavailable for product-document authoring,
read-only runs and providers without the capability. See [command permissions](cis_agent_run.md#explicit-command-permissions).

Claude review sessions are nonpersistent. Resuming a review starts a fresh native session with the complete bound task context; it does not send `--resume` for the earlier review session. The retained envelope, worktree and authority checks still apply.
