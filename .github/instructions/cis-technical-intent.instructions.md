---
applyTo: "docs/specs/technical-intent-*.md"
---

# CIS technical intent authority

- The workspace authority owns one canonical product-scoped technical intent; participant repositories retain repository-scoped supporting intent documents.
- Existing-system discovery may prepare a review-only draft through `cis agent discover technical-intent` / `cis agent author technical-intent` while the BRD is under review. Inspect the selected owned implementation snapshots, distinguish observed behavior from future direction, retain hidden citations and exact coverage, and never invent human decisions. This exception does not weaken initialization, approval or downstream gates.
- An Active/current BRD, completed/current governed technical questionnaire, and fresh product-owned participant graphs are required before technical-intent initialization or approval. Dependency graph drift is visible integration context, not product-authority evidence.
- Infer product frameworks, architecture, databases, and modules only from product-owned repositories. Record dependency repositories as directional integration points with their declared component scope; never absorb their implementation choices as product direction.
- Existing implementations derive only evidence-supported technical facts with confidence and repository provenance; ambiguous choices remain human questions. Greenfield projects require human answers for every direction. Advisory starting directions become authority only through an explicit human answer.
- Initialization creates a Draft from questionnaire answers, the BRD, classifications, graph, and Active standards, including logical components, BRD-derived product modules, detailed responsibility profiles, and stable integration points.
- Review every `TI-MOD-*` candidate against the module completeness fields: purpose, BRD authority, ownership and exclusions, inputs, outputs, data/state, security/policy, failure/recovery, and verification. Do not equate a module with a deployable unless topology requires it.
- Review every `TI-INT-*` handoff for source, trigger, target, contract/data, delivery/consistency, trust/authorization, failure/recovery, observability, compatibility, and test evidence. Refine exact operations in the owning reference dictionary.
- Preserve the baseline, business, questionnaire, component-map, module-architecture, integration-point, technical-surface, standards, and decision-evidence markers, their source digests, stable identity, schema, and approval digest.
- Preserve substantive human-authored sections on rerun; CIS upgrades only recognized untouched starter sections.
- Complete every required technical section and keep unresolved choices in the structured `TI-DEC-*` table.
- Agents may draft technical direction and options. Only explicit human authority may resolve or defer decisions and run `cis technical-intent approve` with the exact reviewer and rationale.
- After approval, project the intent through `cis solution-design init` and approve the overall design plus component sheet as one bundle. Then capture and approve `cis ui-direction`; downstream backlog, change, and plan work requires all three authorities to remain Active and current.
- `cis change create`, `cis plan build`, `cis plan import-spec`, and `cis plan derive` are blocked in a workspace authority unless technical intent, overall solution design, and high-level UI direction are Active and current.
- Rebuild the workspace graph after canonical edits or approval. Questionnaire, BRD, participant-baseline, or approved-content drift makes the intent non-current.
