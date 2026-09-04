---
applyTo: "docs/{specs/ui-direction-questionnaire.md,design/ui-direction.md}"
---

# CIS high-level UI-direction authority

- Require an Active/current overall solution-design bundle before capturing or generating UI direction.
- Preserve stable identity, managed markers, questionnaire and source hashes, design-guideline and UI-framework-profile provenance, and approval metadata.
- Derive only objective facts supported by approved architecture or repository evidence. Product character, shell, visual language, density, accessibility, and experience constraints remain explicit human choices.
- Preserve an existing evidenced design system. When none exists, follow `docs/references/ui-framework-profile.md` and the Active `docs/specs/design-guidelines.md` baseline.
- Treat shell, navigation, tokens, common controls, feedback, responsive behavior, and accessibility as reusable product contracts. Do not redraw common buttons, fields, menus, tables, lists, dialogs, or states inside individual features.
- High-level direction contains no detailed feature screens or PNG approvals. A feature wireframe defines structure, actions, paths, and states; its deterministic Sharp/SVG pack remains a later human review point.
- Run `cis ui-direction validate` before requesting approval. Only explicit human authority may run `cis ui-direction approve`.
- Source or approved-content drift makes UI direction stale and blocks backlog, change, and plan work until it is reconciled and reapproved.
