---
name: cis-govern-ui-direction
description: Capture, generate, validate, and approve the workspace-level UI look and feel after solution-design approval and before high-level backlog or feature design.
---

# Govern High-Level UI Direction

1. Confirm `cis solution-design status --workspace <workspace>` reports Active, valid, and current.
2. Run `cis ui-direction questions init --workspace <workspace>`. Preserve evidence-supported Derived choices from approved surfaces and UI-framework profiles. An accepted no-visual-UI direction remains a source fact across paragraphs and without a saved technical questionnaire; retain its source provenance instead of transcribing it as a new human answer. Absence of frontend code is not that fact. Leave applicable subjective product character, shell, visual language, density, accessibility, state, and constraint choices for a human.
3. Present only unresolved, applicable `UI-Q-*` choices in context. Report conflicting scope before continuing; do not ask visual-design questions for an accepted no-UI scope. Record only a human's new accepted or edited direction through `cis ui-direction questions answer <id> --answer <text> --actor <human>`; an advisory starting direction is not authority by itself. The canonical no-UI applicability record still requires its normal approval.
4. Run `cis ui-direction init` after every question is resolved. Review `design/ui-direction.md` as the common authority for surfaces, product character, shell/navigation, tokens, density, typography/content, reusable components, state/feedback/motion, responsive adaptation, accessibility, and exclusions.
5. Preserve the evidenced UI framework already used by an existing product. When none exists, use the platform-specific default in `references/ui-framework-profile.md`; do not install or replace packages during this stage.
6. Keep this artifact high level. Feature textual wireframes later define screen structure, actions, paths, and states; deterministic Sharp/SVG packs prove each feature's visual interpretation. Neither may silently contradict the active direction.
7. Run `cis ui-direction validate`, resolve provenance or completeness findings, and present the exact document once. Run `cis ui-direction approve --reviewer <human> --reason <rationale>` only with explicit human authority.
8. Rebuild the graph after approval. A solution-design, questionnaire, design-guideline, UI-framework-profile, or approved-content change makes the direction stale and blocks backlog and feature delivery until reconciled.

Never derive subjective brand choices from weak code markers, approve for the user, redraw shared controls per feature, or treat a generated document as Active.

## Human-readable content

Apply the [shared content standard](../../../docs/standards/human-readable-content-standard.md) to in-scope prose. Use [cis-technical-writing](../cis-technical-writing/SKILL.md) for documents and reports, [cis-ux-writing](../cis-ux-writing/SKILL.md) for interface wording, and [cis-content-review](../cis-content-review/SKILL.md) for source-aware review followed by a separate reader check. Preserve this workflow's authority, evidence, privacy and lifecycle rules. Clarity does not remove legitimate uncertainty or mandatory specification sections.
