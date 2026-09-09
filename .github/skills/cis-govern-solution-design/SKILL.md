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
`cis definition prepare --page architecture` after authoring to render its four SVG views.

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
