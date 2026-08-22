---
name: cis-design-review
description: Define textual screen behavior, reuse application-shell and component templates, generate self-contained Sharp/SVG renderers and PNG packs, and preserve human design approval as a global delivery gate.
---

# CIS Design Review

1. Classify every screen as exactly `public`, `customer`, or `backoffice`, then complete `wireframes.md` with stable routes, states, actions, side effects, destination paths, and negative behavior.
2. Obtain explicit human wireframe approval and record the exact digest.
3. Run `cis design templates --format agent` before writing renderer helpers. Reuse `shell.standard-app` and every applicable governed component template—including buttons, fields, selects/dropdowns, choice controls, tabs/navigation, dialogs/alerts, cards, forms, tables, and states—to reduce repeated code and token usage.
4. Run `cis design scaffold <change-id> --feature <slug> --component <template-id> --format agent`; customize only feature content while preserving the shared shell and component behavior.
5. Run `cis design validate <change-id>` and resolve guideline, shell, component, provenance, or renderer errors.
6. Run `cis design render <change-id>`. Successful rendering enters `PausedForReview`; stop all non-review work immediately.
7. Present the PNG pack at original resolution. Only a human may run `cis design approve|reject` with reviewer identity and rationale.
8. On rejection, revise only wireframes/design assets and resubmit; all other delivery work remains paused.
9. On approval, preserve exact renderer and PNG-manifest hashes before downstream work resumes.

Never hand-edit generated PNGs, bypass the application shell, redraw a common control where a governed template applies, use network assets, continue implementation during review, or infer approval.
