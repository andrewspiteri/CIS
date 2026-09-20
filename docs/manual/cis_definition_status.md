---
title: "cis definition status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-11"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-definition-status
---

# `cis definition status`

```text
cis definition status [--workspace <path>] [--format <human|json|agent>]
```

Reports all eight pages, current/completion state, canonical artifact paths, issues, applicable
dictionaries, generated diagrams, UI-preview metadata, session identity, and consolidated
activation readiness. It is read-only and does not weaken the ordinary Active-document gates.

Adding a draft feature does not reset a completed product definition. Intake documents
and a newly reserved, unchanged scaffold repository belong to the feature wizard until
they enter the product baseline or substantive repository work begins. Existing answers,
approval metadata and the activation session remain unchanged; genuine baseline drift
still marks the affected pages stale.

The page projections share repository classification, graph metadata, input hashes,
BRD discovery, and validation results within this calculation. Status reads graph
headers instead of loading all nodes and edges merely to obtain build identity.
Technical and UI questionnaires are included in the JSON result as `technicalQuestions`
and `uiQuestions`, so clients need not launch separate questionnaire status commands.

The technical page also includes CLI-owned `guidance`, with separate questionnaire and
document readiness. Resolving every `TI-Q-*` answer does not resolve an existing
`TI-DEC-*` row in an implementation-authored document. `guidance.technicalDecisions`
contains the decision text, required gate, recorded status and rationale, outstanding
findings, and a one-based line in the original Markdown when the row is unambiguous.
The wizard displays pending document decisions before the completed questionnaire,
opens their exact rows in pinned tabs beside the wizard, and identifies preparation or
inference as Optional when current content already exists. Stale evidence and incomplete
document sections remain explicit next steps. A valid/current document and a completed,
current questionnaire allow continuation without another approval step on this page.

`businessInference` reports whether the BRD can be drafted and the product-owned repositories
available for existing-project inference, including their graph freshness. Dependencies and
a docs-only authority are excluded. These sources come from the canonical workspace registry;
reporting them neither invokes a provider nor changes the BRD.

The business page includes CLI-owned `guidance`: a readable summary, grouped reasons,
one `nextActionId` with an explanation, and action labels marked Needed, Complete,
Optional, Later, or Ready. An existing BRD is not treated as a request to draft again.
Out-of-date BRD evidence is shown even when it appears only as a validation warning.
The next step is evidence refresh, source decisions, unanswered questions, document
corrections, or continuation according to the owning BRD checks. Independent review
is available as an advisory action; this projection adds no approval gate.

`guidance.sourceReviews` identifies each source by repository and path, its existing
assessment and rationale, readable outstanding issues, whether reconciliation is
required, a checked `reviewToken`, and the one-based BRD assessment-row line. The client
shows source names and direct choices with a reason field. **Save decisions** submits
only explicitly edited entries through [`cis brd sources assess`](cis_brd_sources_assess.md)
and refreshes readiness once per batch. Unfinished edits are retained, and stale evidence
requires review before saving. Detailed validator messages remain available.

Each source's `summary` includes readable text, its kind (`local-model`, `excerpt`,
`repository`, or `unavailable`), content hash, model provenance where applicable, and
whether local generation is available. Document-title links open pinned tabs beside the
wizard. [`cis brd sources summarize`](cis_brd_sources_summarize.md) generates missing
summaries explicitly; status only reads current cached summaries or extracts document
text, so opening the wizard never waits for an LLM.

The next calculation rechecks current inputs and file existence. Disposable graph
validation and database-hash caches avoid repeating unchanged work; see
[`cis graph validate`](cis_graph_validate.md) for invalidation and filesystem behavior.
Approval requirements and stale-document gates are unchanged.

BRD status and registered repository-source checks also use fresh graph metadata. Explicit
BRD discovery, validation and approval continue to perform deep graph validation. For local
phase timings on standard error, set `CIS_PERF_TRACE=1`; see
[`cis graph build`](cis_graph_build.md#local-performance-tracing).

`businessInference.lastPreparation` contains the last explicit existing-system inventory report
for owned repositories: dictionary paths, discovered/added row counts, actions, preservation
warnings and errors. Reading status does not repeat discovery or rebuild graphs. Rerun business
preparation after source changes; these counts are not a current semantic coverage assessment.
