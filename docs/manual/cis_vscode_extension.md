---
title: "Change Impact Studio for Visual Studio Code"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on VSIX or consumed CLI contract change"
cis:
  stable_id: change-impact-studio:manual:vscode-extension
---

# Change Impact Studio for Visual Studio Code

The extension is a native delivery workspace over the `cis` CLI and canonical Markdown.
It helps a controller inspect delivery state and request governed work; it does not contain
a second planning, graph, approval, testing, security, verification, or agent engine.

## Install from a VSIX

1. Install a compatible `cis` CLI and verify `cis --version` in a terminal.
2. In VS Code, open **Extensions: Install from VSIX…** from the Command Palette.
3. Select the packaged `change-impact-studio-<version>.vsix`.
4. Open the repository or multi-root workspace to govern.
5. If the CLI is not on `PATH`, set **Change Impact Studio: Executable Path** to the
   executable file. The setting is not a shell command and accepts no arguments.

The Welcome View distinguishes no folder, missing/incompatible CLI, new empty project,
existing unimported repository, initialization collision, untrusted workspace, and a
ready repository.

## First use

- Trust only a workspace you control before running CIS processes.
- For a new empty project, choose the required repository-relative documentation root and
  run **CIS: Initialize Repository**.
- When source manifests or implementation files already exist, the Welcome View instead
  offers **CIS: Import Existing Repository**. CIS dry-runs the bounded self-import, asks for
  one confirmation, registers the repository as workspace authority, and builds its context
  graph without copying or rewriting the implementation.
- Once initialized, open **High-level product definition wizard** in the Workspace View or
  run **CIS: Open High-Level Product Definition Wizard**. The retained eight-page surface
  coordinates foundation, BRD, technical direction, architecture and diagrams, dictionaries,
  UI direction and preview, backlog, and one consolidated activation. Repository initialization
  alone does not invent product requirements.
- For a multi-root workspace, select the authority repository explicitly. The extension
  persists that choice for the workspace and never silently switches it.
- Run Repository Doctor, then refresh the Workspace View.

### Specify a new product or project

The specification journey is state-driven and Markdown-first. Every page links to its canonical
artifacts, and the focused legacy **Next** actions remain available:

| Stage | What the controller does | Canonical document |
|---|---|---|
| Business requirements | Describe users, outcomes, functional requirements, quality obligations, scope, and success criteria. Validate, then explicitly approve when ready. | `specs/business-requirements.md` |
| High-level technical direction | After BRD approval, answer one consolidated questionnaire for product surfaces, technology stacks, architecture, data, integration, identity, hosting, operations, quality, AI, constraints, and exclusions. | `specs/technical-intent-questionnaire.md` |
| Technical intent | Generate from the completed questionnaire, approved BRD, repository classification, graph, and Active standards. Review components, interactions, guidelines, and decisions; validate and explicitly approve. | `specs/technical-intent-spec.md` |
| Overall solution design | Generate the implementation-independent architecture and structured component inventory as one review point; validate and explicitly approve the exact bundle. | `architecture/overall-solution-design.md`, `references/component-sheet.md` |
| High-level UI direction | Review or answer one consolidated look-and-feel questionnaire, generate shared shell, visual, component, responsive, and accessibility direction, then explicitly approve it. | `specs/ui-direction-questionnaire.md`, `design/ui-direction.md` |
| Diagrams and dictionaries | Review the generated system, component, integration/trust, and deployment diagrams and the classification-selected reference index. | `architecture/high-level-architecture-diagrams.md`, `references/dictionary-index.md` |
| UI-system preview | Inspect one page showing selected typography, colors, shell, common controls, data presentation, feedback, and dialog treatment. | `design/ui-system-preview.md`, `design/ui-system-preview.svg` |
| High-level backlog | Build from the approved intent, review outcome boundaries and dependencies, validate, and explicitly approve. | `plans/high-level-backlog.md` |
| Feature specification | Start the next dependency-ready `HLT-*` item, expand the scaffold manually or through a bounded one-file agent draft, review its detailed requirements and acceptance criteria, validate, and explicitly approve. | `specs/features/<item>/feature-specification.md` |

The Workspace tree shows these stages under **Product definition** and exposes one
prominent **Next** action. Clicking a document stage opens the exact canonical Markdown.
Validation is mechanical and needs no approval. The wizard's final page shows all page states,
lets the user return to any page, and records one identity-bound activation without repetitive
rationale prompts. CIS validates again, protects the exact file set transactionally, and rebuilds
the graph. A failed activation restores the pre-activation files. Focused standalone approval
commands remain available and retain their owning artifact's rationale contract.

After the solution-design bundle is Active, **Define high-level UI look and feel** opens a
twelve-card editor for product surfaces, character, shell/navigation, density, color/theme,
typography/content, component reuse, responsive behavior, accessibility, feedback, recurring
patterns, and constraints. Existing UI-framework evidence is prefilled where deterministic;
subjective decisions remain human. Saving the final answer generates `design/ui-direction.md`.
This stage does not create feature screens or PNGs. It defines the common authority those later
wireframes and deterministic Sharp/SVG review packs must follow.

Starting a feature creates its canonical schema and immediately offers **Draft with agent**.
The selected provider sees only the current governed product, technical, architecture,
component, experience, standards, security, and testing evidence in an isolated scratch
repository. CIS applies the result only when the feature is the sole changed file, frontmatter
is preserved, all required sections and structured requirements remain, and every template
placeholder has been removed. Existing scaffolds with placeholders expose the same drafting
action as the prominent next step.

When a BRD still contains incomplete sections, **Next: Draft business requirements from
reference** opens a multi-file picker. Select non-sensitive plain-text evidence or Word
Open XML (`.docx`) documents, choose a discovered Codex or Claude provider and its
transport, review the disclosure and isolated permission boundary, and start the
foreground run. The CLI extracts bounded readable text from `.docx`; formatting, images,
embedded objects, macros, and legacy `.doc` files are excluded. The extension invokes `cis agent
author brd`; it does not send references to providers itself. CIS applies the result only
when the isolated diff contains the BRD alone and all lifecycle frontmatter and managed
blocks are unchanged. When the BRD was agent-authored, **Next: Review BRD with independent
agent** selects a different review-capable provider and invokes `cis agent review brd` with a
read-only permission ceiling in an isolated workspace. The review produces structured advisory
findings under `.cis/local/agents/runs/<run-id>/brd-review.md`; it cannot edit, validate, answer
questions in, or approve the canonical BRD. Original authoring references are shared with the
review provider only when the controller explicitly opts in after seeing the disclosure prompt.
If recommendations exist, **Next: Review recommendations** opens an editor-area review screen
that presents every finding's severity, category, location, full observation, and full
recommendation as one consolidated list. A human may approve items as written, edit and approve
individual remediation text, or approve all unchanged pending recommendations as one atomic
batch; earlier edited approvals remain intact. There is no separate aggregate approval.
Accepted findings route to a different implementation provider in an isolated one-file BRD
revision; legacy rejections remain guardrails. CIS then starts provider selection for a
closure-only verification that must differ from the reviser and cannot reopen broad refinement.
Only after that verification is current does the guided journey
return to open-question resolution and human BRD approval.

**Next: Answer open BRD questions** opens a consolidated editor page rather than sequential
input prompts. Each card shows the question, up to three CIS-selected BRD excerpts, current
answer provenance, and any advisory AI suggestion. The answer remains editable in place.
Selecting **Accept suggestion** or **Save answer** is the human authority boundary; merely
opening the page or generating guidance never changes the BRD. CIS tries an available local
model without external disclosure. A remote model requires explicit provider selection and a
modal confirmation that the displayed questions and context excerpts will be transmitted.

After BRD approval, **Next: Define high-level technical direction** opens one editor-area page
containing all 16 `TI-Q-*` decisions. Each card explains why the choice matters, shows common
options and an advisory starting direction, and provides an editable recorded direction. Existing
projects are pre-filled only where repository evidence supports a derived answer, with confidence
and provenance displayed; unresolved decisions remain for the human. Greenfield projects start
unanswered. Every card has one action: it saves the exact text currently in the editor, whether
that text began as a suggested, derived, or previously recorded direction. A completed questionnaire
remains reopenable from the Workspace and Journey Map; updating a recorded direction regenerates
the technical intent, component map, product-module architecture, and integration-point catalog.
The generated module profiles identify ownership, inputs, outputs, state, policy, recovery, and
verification responsibilities; integration records identify the source, trigger, target, contract,
delivery semantics, trust boundary, failure handling, and BRD authority. Individual saves remain
in the questionnaire, retain its
scroll position and focused card, and do not open a command-detail editor on success. When the final unanswered direction is recorded, the
extension performs that same generation automatically; this adds no second questionnaire approval.

## Navigate the workspace

- **Workspace** shows authority, health, freshness, local AI, active change, blocking
  review, and one next meaningful action.
- **Journey Map** shows the current high-level product definition, technical definition,
  and the repeatable feature-delivery path from specification through acceptance.
- **Changes** opens current and closed dossiers without mutating them.
- **Evidence** opens canonical Markdown and runs bounded context/relationship queries.
- **Runs** separates agent, workflow, test, security, diagnostics, and assurance evidence.
- **Governance** shows skills, instructions, standards, references, providers, conflicts,
  quarantines, and Doctor findings.

Selecting **Doctor: errors**, **Doctor: warnings**, the status-bar health item, or **CIS: Run
Repository Doctor** opens a dedicated Repository Doctor page. It groups errors, warnings, and
informational findings and shows each finding's code, category, complete message, evidence,
fixability, suggested fix, and optional CIS command. **Copy command** copies the exact suggestion
for review; it does not execute it. **Run Doctor again** refreshes the same page and the Workspace
health projection. Evidence links open only when they remain inside the selected authority's
permitted canonical or local-evidence boundary.

Rich summaries open in purpose-built editor-area screens: technical-direction questionnaire,
change overview, task detail,
Repository Doctor, context/graph detail, design review, agent request confirmation, recommendation review,
run detail, and long-running command progress. These screens use cards and visible state
hierarchy from the approved CIS workspace design; they no longer collapse those journeys
into a generic JSON/key-value table. Canonical Markdown opens in the normal VS Code editor
or built-in Markdown preview and remains the editable source of truth.

## Request agent work

From an eligible planned task, choose **Request agent work**. Review the exact provider,
repository, isolation, run mode, permission ceiling, and approval-request policy before
starting. The extension invokes `cis agent prepare` and `cis agent run`; it never connects
to Codex or Claude directly and never handles provider credentials.

The run remains a foreground, observable CIS operation. Cancel, recover, resume, and
result import appear only when CIS reports them as supported. A resume creates a new
attempt. Imported output is evidence and grants no approval, task transition, verification
acceptance, or closure authority.

## Settings

| Setting | Purpose |
|---|---|
| `cis.executablePath` | Exact CIS executable path or `cis` when available on `PATH`. |
| `cis.documentationRoot` | Pre-initialization fallback only; initialized metadata wins. |
| `cis.actorIdentity` | Optional human identity passed to audited CIS commands; when blank, the extension prompts for each decision or transition. |

Authority selection is stored in VS Code workspace state rather than a repository file.
Provider credentials remain outside extension settings. **CIS: Authenticate Agent
Provider** can start a provider-native browser or device flow; CIS does not receive or
store the resulting credential. An inconclusive status is advisory because an ambient
Codex Desktop/App Server session can still be usable.

## Troubleshooting

| State | Recovery |
|---|---|
| CLI missing | Configure an executable path and refresh. |
| CLI incompatible | Install the supported CIS version; detected and expected versions are shown. |
| Workspace untrusted | Inspect Markdown read-only or explicitly trust the workspace before execution. |
| New project is uninitialized | Run init with a maintainer-selected documentation root. |
| Existing repository is unimported | Run the import journey; review its dry-run summary before confirmation. |
| Initialization collision | Run Repository Doctor; existing files are preserved. |
| Stale view | Select **CIS: Refresh Workspace**. Watchers only mark evidence stale; one explicit refresh performs the bounded projection reload and stops when it completes. |
| Repeated read-only CLI calls | Reload the current extension build. Identical workspace queries share one in-flight five-second cache, invalidated by explicit refresh and mutations. |
| Invalid evidence | Open bounded diagnostics; a successful process without readable expected output is not passed. |
| Provider executable unavailable | Open provider diagnostics. Codex resolves an explicit `CIS_CODEX_EXECUTABLE`, the VS Code process `PATH`, then the current Windows Codex Desktop installation. Reload VS Code after changing its environment. |
| Claude executable unavailable | Open provider diagnostics. Claude resolves an explicit `CIS_CLAUDE_EXECUTABLE`, the VS Code process `PATH`, then the newest Claude Code native binary installed by VS Code, VS Code Insiders, Cursor, or Windsurf on Windows. Reload VS Code after changing its environment. |
| Provider authentication unverified | Run Repository Doctor. Continue when governed execution proves the ambient session works, or run **CIS: Authenticate Agent Provider** for native browser/device setup. |
| Provider authentication unavailable | Run **CIS: Authenticate Agent Provider**. CIS offers the native methods declared by the selected provider: Codex browser/device or Claude browser/console/SSO. Desktop application registration alone is not proof of CLI authentication. |
| Interrupted agent run | Use CIS recovery only when offered; resume always creates another retained attempt. |

## Security boundary

The extension requires workspace trust for processes, launches argument arrays without a
shell, enforces workspace path containment, bounds and redacts output, and does not read
credentials, `.env` content, stored prompts, tracker bodies, or unrestricted provider
transcripts. Webviews are used only for rich bounded detail and use a restrictive content
security policy and validated messages.

## Contributing and verification

Run the extension checks from `vscode-extension/`:

```text
npm test
npm run check
```

Release verification also installs the VSIX into a clean VS Code profile, binds a verified
CIS CLI, opens initialized and uninitialized fixtures, and exercises Repository Doctor.
Implementation must remain consistent with `docs/specs/vscode-client-spec.md` and the
approved `docs/changes/CIS-0001/` design evidence.
