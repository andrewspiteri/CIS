---
title: "cis definition status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-05"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-definition-status
---

# `cis definition status`

```text
cis definition status [--workspace <path>] [--summary] [--format <human|json|agent>]
```

Reports all eight pages, current/completion state, canonical artifact paths, issues, applicable
dictionaries, generated diagrams, UI-preview metadata, session identity, and consolidated
activation readiness. It is read-only and does not weaken the ordinary Active-document gates.

The wizard also searches the authority project for existing Markdown requirements,
technical intent, architecture, component sheets, diagrams, UI direction, and backlog
documents. It excludes generated dependencies, agent instructions, templates and routing
cards. Candidates are suggestions; finding a document does not approve it.

Use **Load existing documents** in the VS Code wizard or command palette to open the eligible
file list. Each row shows the path, byte size, last modified date, an expandable summary
excerpt, and **Select file**. Filter the list by document type. Each wizard page also offers **Load document manually…**
to open a file picker without running discovery first. Pages with multiple document types
ask which type to load. **Browse** in the discovery results also lets you select a project
document with another name. CIS records the selected path in `.cis/product-documents.json`,
refreshes graph evidence, runs the document's reconciliation command, and refreshes context
and wizard findings. If a later step fails, the file remains selected and the failure is shown.

Loading technical intent first prepares its technical questionnaire. If choices remain
unanswered, the file stays loaded and CIS asks you to review those choices; this is not an
import failure. Once the choices are complete and current, reconciliation continues.
An explicitly selected technical document without CIS metadata is backed up under
`.cis/local/document-import/` and adopted as a Draft, preserving its authored narrative.
Its supplied status does not grant CIS approval, and conflicting CIS identities remain blocked.
For unanswered technical choices, CIS prefills relevant passages from the selected document
locally and shows their source locations. These are excerpts for review and may cover only
part of a question. Saving records your confirmed or edited direction; displaying an excerpt
does not answer the question. Saved answers remain unchanged, and pending suggestions refresh
when the supplied project copy changes. No model request is needed for these excerpts.

When the technical questionnaire is complete, its screen shows **Review technical intent**,
**Refresh readiness**, and **Approve technical intent**. Approval is enabled only when the
questionnaire and document are current and validation passes; otherwise the document findings
appear beside the disabled button. Approval still requires the reviewer and confirmation.
For selected imported documents, validation recognizes numbered headings and equivalent
section titles. A decision-closure section must explicitly state closure, and any open status
in its decision table still blocks approval. The imported narrative is not rewritten to pass validation.

For an explicitly selected legacy BRD without CIS metadata, reconciliation first backs up its
original bytes under `.cis/local/document-import/`, preserves its authored narrative and
existing metadata, adds CIS identity and managed evidence sections, and sets **Review Required**.
Conflicting CIS identities or partial managed sections remain blocked. Source assessments,
unanswered questions, missing required sections, and downstream approval prerequisites still
require review. Selecting a file never approves it.

The equivalent CLI operations are:

```text
cis definition documents --workspace <path> --format json
cis definition documents --workspace <path> --role business --path docs/project/brd-spec.md --format json
```

Roles are `business`, `technical`, `architecture`, `components`, `diagrams`,
`experience`, and `delivery`. `--path` selects an existing writable Markdown file inside
the authority project; symbolic links and escaping project paths are rejected.

To import a supplied file from elsewhere, use `--source` instead of `--path`:

```text
cis definition documents --workspace <path> --role business --source <absolute-markdown-path> --format json
```

The source must be a fully qualified `.md` path and no larger than 20 MiB. CIS copies
it to a unique `<documentation-root>/imports/<role>/<id>/` directory and records the
project copy as selected. Each import preserves the original and any previous import.
Supply exactly one of `--path` and `--source`, together with `--role`. The CLI selection
command records the file; the editor's load workflow subsequently performs the graph,
reconciliation and readiness steps described above. Neither operation approves it.

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

For a new solution, architecture preparation can project an explicitly selected technical
intent's ownership table without implementation dictionaries. Supported tables name a
process, component or module and specify what it owns and excludes. CIS preserves that
text and assigns stable draft identities in the component sheet. The resulting SVG shows
documented boundaries as proposed structure; membership lines do not claim runtime calls
or data flows. Missing integration details are left for architecture review. Preparation
reports a failure if it cannot produce current diagrams, instead of reporting success
with no output. Neither preparation nor dictionary absence grants approval.

When the recorded frontend decision explicitly excludes a visual UI, preparing the
experience page derives that scope from the technical questionnaire. A human can also
record “No user-facing UI” in the UI surface question. The other visual-design questions,
frameworks and screen previews then do not apply. CIS retains a short scope record for
final review; it does not invent visual choices or approve them. The wizard can continue
to delivery, and consolidated activation does not require preview files. Changing the
scope to include a visual interface reopens the visual-design requirements.

Delivery preparation uses the same requirement reader as imported-BRD validation.
Numbered headings, identified narrative paragraphs and requirement tables produce
traceable candidate items without renumbering or rewriting the approved BRD. Equivalent
non-functional-requirement headings supply global obligations. A recorded no-visual-UI
scope prevents word matches such as “public” or “user” from creating frontend tasks.

After activation, approved backlog outcomes appear in High-level features even before
individual feature requests exist. Their prerequisite definitions and current product
approval determine whether they can start. Opening a ready outcome creates a linked
product-level draft and enters the feature wizard; reopening resumes the same draft.
This handoff retains its BRD requirement and acceptance intent, preserves product and
backlog approvals, and leaves implementation repository links to story planning. Feature
review and task-plan approval remain required before execution.

BRD status and registered repository-source checks also use fresh graph metadata. Explicit
BRD discovery, validation and approval continue to perform deep graph validation. For local
phase timings on standard error, set `CIS_PERF_TRACE=1`; see
[`cis graph build`](cis_graph_build.md#local-performance-tracing).

`businessInference.lastPreparation` contains the last explicit existing-system inventory report
for owned repositories: dictionary paths, discovered/added row counts, actions, preservation
warnings and errors. Reading status does not repeat discovery or rebuild graphs. Rerun business
preparation after source changes; these counts are not a current semantic coverage assessment.

## Compact status

Add `--summary` to JSON status output to retain page identity, status, completeness,
currency, primary paths, warnings and errors while omitting full documents, questions
and artifact payloads. `detailsOmitted=true` identifies this projection. Use the same
command without `--summary` for the complete result; existing full JSON is unchanged.
