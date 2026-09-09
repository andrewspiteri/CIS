---
title: "High-Level Product Definition Wizard Specification"
type: specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-04"
review_cadence: "on product-definition workflow change"
cis:
  stable_id: change-impact-studio:spec:high-level-product-definition-wizard
---

# High-Level Product Definition Wizard Specification

## Purpose

The definition wizard turns the product-wide business, technical, architecture, contract,
experience, and delivery baseline into one resumable journey before the repeatable feature loop.
It coordinates existing CIS authorities; it does not replace their canonical Markdown, validators,
or ownership boundaries.

## Pages

| Page | Intent | Canonical or derived outputs | Completion boundary |
| --- | --- | --- | --- |
| 1. Project foundation | Create a new empty CIS project or import and classify an existing implementation; establish authority, evidence, standards, and seeded references. | Repository/workspace metadata and classification-selected references. | The authority and at least one applicable governed dictionary exist. |
| 2. Business definition | Define problem, actors, outcomes, rules, constraints, exclusions, and measures. | BRD plus question, agent-authoring, and independent-review evidence. | The BRD is valid, current, and Ready for Approval or Active. |
| 3. Technical direction | Resolve product surfaces, technology, architecture, data, integration, hosting, security, and quality choices. | Technical questionnaire and technical intent. | The questionnaire is complete/current and technical intent is valid/current. |
| 4. Solution architecture and diagrams | Define modules, responsibilities, records, trust boundaries, integration points, deployment, and operations. | Overall solution design, component sheet, and high-level Mermaid diagram sheet. | The architecture bundle and four derived diagrams are complete/current. |
| 5. Contracts and dictionaries | Start the classification-selected cross-feature vocabulary. | Dictionary index plus applicable API, event, permission, data, route, state, ownership, problem, configuration, package, and traceability references. | At least one applicable reference is indexed. |
| 6. Experience direction and UI preview | Resolve shell, visual language, typography, colors, components, states, responsiveness, and accessibility. | UI questionnaire, UI direction, one-page Markdown summary, and self-contained SVG preview. | The questionnaire and direction are complete/current and the preview matches their source digest. |
| 7. Delivery map | Convert the baseline into outcome-sized feature preparation order. | High-level backlog with routing, classifications, dependencies, and shared obligations. | The backlog is valid/current against the same draft baseline. |
| 8. Review and activate | Review every page together and revise any page without losing place. | One activation record across the exact baseline. | All seven prior pages are complete/current. |

## Draft coordination

The Business definition page offers **Infer from existing project** for imported systems.
The CLI supplies eligible product-owned repository paths and graph freshness. The client
preselects these sources, accepts an explicit source/provider selection, and invokes the
existing protected BRD authoring command. Dependency repositories and a docs-only authority
are excluded from the proposed product evidence; an authority with implementation code may
be included. The agent distinguishes observed behavior from intended policy and preserves
unresolved business decisions as questions. The result remains Review Required. The page
then guides draft/source review, independent review, and question resolution. Its generic
Prepare button is omitted because page preparation does not infer business content.

Normal CIS commands continue to require Active upstream authorities. While the definition module is
preparing or evaluating this bounded wizard only, a valid/current Ready for Approval upstream draft
may feed the next draft. The allowance is process-local, nest-safe, and disposed after the command;
it cannot make a feature, change, or independent command treat a draft as Active.

Questionnaire answers have one action: **Save direction**. Saving an edited answer replaces the
same governed decision with actor provenance and keeps the user on the same wizard page. Existing
and derived answers remain editable. The retained VS Code panel updates in place and does not open a
new editor for each answer.

## Diagrams, dictionaries, and preview

The Architecture page offers **Infer from existing repositories**. The client uses the CLI's eligible
owned repositories, source/provider selection and disclosure confirmation, then calls
`cis agent author solution-design`. CIS prepares dictionaries and immutable implementation snapshots
and writes the architecture/component bundle with a validated four-view model. The client then
prepares the architecture page to render SVGs and refreshes the graph. Diagram cards show the images
directly and open the diagram sheet in Markdown preview. Repeated clicks share the authoring guard.
This review-only path may run while upstream drafts are under review; activation gates remain intact.
The generic preparation action remains the projection path for new products and the diagram-refresh
path for inferred architecture. It preserves inferred narrative.

`definition init` and `definition prepare` maintain three canonical derived records:

- `architecture/high-level-architecture-diagrams.md` contains system-context, component-topology,
  integration/trust-boundary, and deployment/operations views. Inferred architecture uses local SVGs
  generated from its model; technical-intent projection retains Mermaid sources;
- `references/dictionary-index.md` reports applicability, lifecycle, entries, and links for the
  classification-selected dictionaries seeded by repository initialization;
- `design/ui-system-preview.md` and `design/ui-system-preview.svg` show one representative page
  containing the chosen shell, typography, colors, common controls, data presentation, feedback,
  and dialog treatment.

Each record carries its exact source digest. Re-preparation preserves an Active record whose source
digest has not changed and returns a changed record to Review Required. The SVG contains no script,
remote resource, credential, or runtime application behavior; it is a high-level visual-system
preview, not a feature design approval.

## Consolidated activation

The final page displays all seven page states and supports direct navigation to any page. It offers
one **Approve and activate** action only when the complete baseline is valid and current. The actor
confirms the exact set once; the wizard does not request a repetitive rationale. CIS records a fixed
wizard-activation reason and the actor/time/content hashes on every governed artifact.

Activation is transactional at the file boundary. CIS snapshots the protected canonical files,
activates the BRD, technical intent, solution-design bundle, UI direction, derived records, and
backlog, updates their catalogue states, and rebuilds the graph. A bounded I/O or lifecycle failure
restores every snapshot and reports `rolled-back`; partial activation is never presented as success.
Successful activation records a stable digest over the complete business, technical, architecture,
component, dictionary inventory, experience, preview, and semantic backlog artifact set. Managed
feature-specification links and their approval-hash refresh do not change that digest; an agent run's
scope digest separately binds the exact dictionary versions made available to it. Once a wizard session exists,
feature start and authoring require this activation; feature validation requires the exact digest.
Managed BRD source-assessment and feature-traceability blocks are likewise provenance rather than
new product intent. Reconciling an approved downstream feature into those blocks must not revoke the
activation; edits to human-authored business requirements still do.
Individual document approvals therefore cannot accidentally bypass the final product-definition review.
After activation, any material edit follows the ordinary owning artifact's drift and renewed-review
rules. The user may reopen the wizard to begin or resume a revision.

## Commands

```text
cis definition init [--workspace <path>] [--format <human|json|agent>]
cis definition status [--workspace <path>] [--format <human|json|agent>]
cis definition prepare --page <page> [--workspace <path>] [--format <human|json|agent>]
cis definition answer --page <technical|experience> --id <question-id> --answer <text> --actor <human> [--workspace <path>] [--format <human|json|agent>]
cis definition activate --reviewer <human> [--workspace <path>] [--format <human|json|agent>]
```

Session state is derived and disposable under `.cis/local/definition-wizard/session.json`.
Markdown and its human approval evidence remain canonical. Unknown pages, malformed answers,
missing workspace authority, incomplete baselines, path collisions, and stale artifacts fail closed.

### Existing-system evidence before business drafting

The business, technical and contracts pages prepare observed inventories before inference:
API declarations, data fields and relationships, status enums, permissions, routes, commands,
events, projection declarations, exceptions, rejection guards, configuration keys, packages,
ownership and traceability in product-owned repositories. The authority consolidates code-derived
facts from every owned repository, including its own implementation when present. Repository
attribution scopes identities without changing code identifiers. Dependency repositories are
excluded. Empty TODO starters count as zero entries. Direct discovery and authoring prepare the
same dictionaries and graphs before freezing selected repository evidence.
Show the last discovery counts, preservation warnings and navigable dictionary paths.
Inference automatically runs this explicit preparation after controller source/provider consent.
Refresh untouched managed Draft starters; add missing identities to human-edited Draft tables;
preserve reviewed documents and existing human rows. Keep recovery copies of changed content.
Never run source discovery from wizard status. Reuse unchanged graph builds.

Repository projections must retain relevant dictionary details and lifecycle with stable
citations, bounded UTF-8 size and visible per-family coverage. Alternate large inventory
families/entities so state and permission evidence is not starved by data fields. Translate
observed implementation into business capabilities without inventing transitions, policy,
approvals or stakeholder intent. The contracts page remains the later review/refinement step.
