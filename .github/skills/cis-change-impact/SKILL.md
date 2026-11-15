---
name: cis-change-impact
description: Prepare bounded CIS changes using existing outcome or feature authority where applicable, with exact baselines, relevant graph/impact assessment, decisions and planning. Preserve legacy review routes when no reusable authority covers the work.
---

# CIS Change Impact

## Workflow

1. Read `.cis/repository.yml`, the repository profile, catalog, and applicable specifications.
2. Use `cis-graph-context` to ensure a fresh graph and identify exact roots.
3. Use `cis-change-dossier` to create or inspect the exact-baseline change record.
4. Use `cis-impact-review` where impact assessment is needed. Reuse current outcome or exact feature authority for covered findings; present only unresolved material differences for a targeted human decision. Preserve required legacy dispositions.
5. Use `cis-decision-review` for unresolved blocking or advisory questions.
6. Use `cis-bounded-planning` to build and validate work under applicable authority and readiness. Draft creation and inspection remain possible before authorization; preparing a draft grants no execution authority.
7. Recheck planned versus actual scope with `cis-validate-completion` before closure.

## Guardrails

Never invent finding disposition, decision resolution, human approval or final
acceptance. Derive in-scope task organization and resource distribution only under
the actual delegation and unchanged overall limits. Record a human decision only
when explicitly authorized with its real identity and rationale.
