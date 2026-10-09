---
name: cis-govern-solution-design
description: Generate, refine, validate, and approve the atomic overall solution design and component sheet after technical-intent approval and before backlog or UI-facing design work.
---

# Govern Overall Solution Design

For an imported existing product, `cis agent discover solution-design` prepares a local evidence
preview and review-only scaffold; `cis agent author solution-design` infers the architecture bundle
from selected owned repositories and the current BRD/technical intent. This draft-only path may run
while upstream documents are under review. It does not grant approval or weaken the ordinary gates
below. Inspect every implementation area, preserve human notes and stable component IDs, keep source
citations in comments, and distinguish observed, proposed and unresolved facts. Run
`cis definition prepare --page architecture` after authoring to refresh its C4 SVG views in the wizard. CIS embeds context, container and scoped component diagrams inside the overall design when applying inference. Use schemaVersion 2: context contains people and software systems; containers are applications/data stores inside the product; each component view zooms into one container. Preserve evidence status and do not equate containers with deployment instances. Use `cis solution-design diagrams` to repair the embedded views of a review-only C4 draft.

1. Confirm `cis technical-intent status --workspace <workspace>` is Active, valid, and current.
2. Run `cis solution-design init --workspace <workspace> --format agent`.
3. Review `architecture/overall-solution-design.md` for system context, logical topology, data ownership, integration, trust, deployment, recovery, verification, traceability, and a bounded UI-design handoff.
4. Review `references/component-sheet.md` for one stable `TI-MOD-*` row per logical component, explicit responsibility and ownership, explicit exclusions, and BRD authority.
5. Do not equate a logical component with a repository, process, service, or deployment unit unless the approved topology says so. Keep cross-component access behind owned `TI-INT-*` contracts.
6. Record substantive refinements outside managed blocks or through an upstream technical-intent decision. Use an ADR for a durable exception.
7. Run `cis solution-design validate` and resolve every structural, traceability, collision, drift, and bundle-integrity finding.
8. Present both files as one review point. Run `cis solution-design approve --reviewer <human> --reason <rationale>` only with explicit human authority.
9. Rebuild the graph after approval. Continue with `cis ui-direction questions init` and `cis ui-direction init` for shared product character, shell, navigation, reusable interaction patterns, visual direction, responsive behavior, and accessibility. Detailed screens remain feature-level work.

Never approve per component, invent business scope, overwrite human sections on rerun, bypass stale source evidence, or treat generated content as Active.

### Retaining a design after source changes

When an explicit architecture still fits changed technical direction, inspect both canonical documents and the source differences. For a greenfield design this review does not require an implementation repository or an inference provider. Run `cis solution-design reconcile` with `--actor`, a substantive `--reason`, the reviewed files' raw SHA-256 values in `--expected-design-sha256` and `--expected-components-sha256`, and the current `technicalIntentVersion` from `cis solution-design status --workspace <workspace> --format json` in `--expected-technical-version`. The command preserves the narrative and diagrams, records reconciliation, and clears approval for the whole bundle. It rejects changed inputs, invalid diagrams and changed component identities. It does not establish that the design is correct or that an implementation exists. Revise an unsuitable design before reconciling. Validate and obtain explicit whole-bundle approval through the ordinary steps above; never supply a human identity for an agent's reconciliation.

## Human-readable content

Apply the [shared content standard](../../../docs/standards/human-readable-content-standard.md) to in-scope prose. Use [cis-technical-writing](../cis-technical-writing/SKILL.md) for documents and reports, [cis-ux-writing](../cis-ux-writing/SKILL.md) for interface wording, and [cis-content-review](../cis-content-review/SKILL.md) for source-aware review followed by a separate reader check. Preserve this workflow's authority, evidence, privacy and lifecycle rules. Clarity does not remove legitimate uncertainty or mandatory specification sections.
