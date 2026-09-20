---
title: "cis agent review brd"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-14"
review_cadence: "on command or provider-policy change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-review-brd
---

# `cis agent review brd`

Runs an independent, read-only review of the canonical business requirements document
through a discovered agent provider.

```text
cis agent review brd --provider <id> --actor <human>
  [--transport <name>] [--timeout-seconds <30..86400>]
  [--include-authoring-evidence] [--repo <path>]
  [--format <human|json|agent>]
```

The review provider must differ from the provider on the latest successful
`PRODUCT/BRD-DRAFT`, `PRODUCT/BRD-REVISION`, or `PRODUCT/BRD-QUESTION-REVISION` run. The review runs as
`PRODUCT/BRD-REVIEW` with `review` mode and a `read-only` permission ceiling inside a
minimal isolated Git scratch repository. Any file mutation makes the run invalid.

For Claude, CIS uses restricted safe mode with only `Read`, `Glob`, and `Grep`, a
non-interactive `dontAsk` permission policy, and a JSON Schema for the review contract.
It deliberately does not use Claude's `plan` permission mode because that mode can defer
the final result into a provider-owned plan file instead of returning governed evidence.

By default, the provider receives the canonical BRD and applicable governance context.
`--include-authoring-evidence` additionally discloses the latest digest-matching BRD
authoring envelope, including its previously extracted source evidence and exact
implementation/test snapshots when repository discovery was used. Snapshot manifests and
files are verified against the authoring digests before disclosure; missing or changed
cache evidence blocks the review. Later edits to live participant code do not silently
replace the files used to author the BRD. This is opt-in
because selecting a different provider expands the disclosure boundary. If the evidence
is missing or no longer matches the successful authoring run, CIS fails closed.
When references are not included, the isolated workspace explicitly tells the reviewer
that omitted artifacts are unknown rather than absent; a bounded review cannot claim a
repository-wide search or source non-existence.

The provider must return a structured recommendation of `ready`, `revise`, or `blocked`,
strengths, and at most 100 findings. Each finding records a stable `BRD-REV-*` identity,
severity, category, location, observation, and recommended action. A `ready`
recommendation cannot contain blocking or major findings.

The initial review assesses the rendered BRD as a business reader with no technical
background. A missing or unusable product narrative is a major finding requiring revision
in that pass; readability is not deferred until questions are answered. The narrative
must explain complete journeys, participants, business rules, operating variations,
exceptions and outcomes. Source links and technical evidence mappings are read from HTML
comments, and exposed references are actionable readability findings. Agreement with a
bounded declaration inventory cannot establish complete product coverage. These criteria
do not broaden the exact scope of later closure-only or answer-incorporation reviews.

CIS retains the JSON result and a human-readable Markdown rendering beneath:

```text
.cis/local/agents/runs/<run-id>/result.json
.cis/local/agents/runs/<run-id>/brd-review.md
```

After a successful review, the VS Code extension loads that run directly, initializes
its pending recommendation record when it has findings, and opens the report before
refreshing workspace views. A failure in this follow-up reports that the review itself
succeeded. Reopen the retained run or its recommendations to continue; a view refresh
failure does not require another provider execution.

The review is advisory derived evidence. It cannot edit or approve the BRD, answer open
questions, assess sources on behalf of a stakeholder, validate business currency, or
change lifecycle state. Use `cis brd review init/decide/approve` to record independent
human dispositions. Accepted findings may then be applied with `cis agent revise brd`.
After a revision, this command automatically includes the applied canonical disposition
record and becomes a closure-only verification through a provider different from the
reviser. That verification may report only an unapplied approved recommendation, an
introduced legacy-rejected recommendation, a regression or contradiction caused by the
bounded revision, or a protected evidence/scope change. It must not reopen broad BRD
completeness, raise pre-existing observations, or propose unrelated refinements. A clean
closure result ends the review cycle.

After `cis agent incorporate brd-questions`, this command is an answer-incorporation review.
It checks the exact governed answer digest and verifies that each human decision is reflected
accurately in the relevant BRD sections. It may report a missed or incorrect incorporation or
a concrete regression caused by that bounded revision. It must not reopen unrelated broad
refinement. A clean result permits BRD validation and explicit human approval to continue.
