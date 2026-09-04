---
name: cis-govern-ui-direction
description: Capture, generate, validate, and approve the workspace-level UI look and feel after solution-design approval and before high-level backlog or feature design.
---

# Govern High-Level UI Direction

1. Confirm `cis solution-design status --workspace <workspace>` reports Active, valid, and current.
2. Run `cis ui-direction questions init --workspace <workspace>`. Preserve evidence-supported Derived choices from approved surfaces and UI-framework profiles; leave subjective product character, shell, visual language, density, accessibility, state, and constraint choices for a human.
3. Present the full `UI-Q-*` set in context. Record only the human's accepted or edited direction through `cis ui-direction questions answer <id> --answer <text> --actor <human>`; an advisory starting direction is not authority by itself.
4. Run `cis ui-direction init` after every question is resolved. Review `design/ui-direction.md` as the common authority for surfaces, product character, shell/navigation, tokens, density, typography/content, reusable components, state/feedback/motion, responsive adaptation, accessibility, and exclusions.
5. Preserve the evidenced UI framework already used by an existing product. When none exists, use the platform-specific default in `references/ui-framework-profile.md`; do not install or replace packages during this stage.
6. Keep this artifact high level. Feature textual wireframes later define screen structure, actions, paths, and states; deterministic Sharp/SVG packs prove each feature's visual interpretation. Neither may silently contradict the active direction.
7. Run `cis ui-direction validate`, resolve provenance or completeness findings, and present the exact document once. Run `cis ui-direction approve --reviewer <human> --reason <rationale>` only with explicit human authority.
8. Rebuild the graph after approval. A solution-design, questionnaire, design-guideline, UI-framework-profile, or approved-content change makes the direction stale and blocks backlog and feature delivery until reconciled.

Never derive subjective brand choices from weak code markers, approve for the user, redraw shared controls per feature, or treat a generated document as Active.
