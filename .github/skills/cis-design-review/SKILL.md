---
name: cis-design-review
description: Define textual screen behavior, reuse application-shell and component templates, generate self-contained Sharp/SVG renderers and PNG packs, and preserve human design approval as a global delivery gate.
---

# CIS Design Review

1. Classify every screen as exactly `public`, `customer`, or `backoffice`, then complete `wireframes.md` with stable routes, states, actions, side effects, destination paths, and negative behavior.
2. Run `cis design wireframe-validate <change-id>` and resolve structural errors. A separate `wireframe-approve` is optional and is used only when the team explicitly wants an early behavior-only checkpoint.
3. Run `cis design templates --format agent` before writing renderer helpers. Reuse `shell.standard-app` and every applicable governed component template—including buttons, fields, selects/dropdowns, choice controls, tabs/navigation, dialogs/alerts, cards, forms, tables, and states—to reduce repeated code and token usage.
4. Before scaffolding, run `cis design reuse` for each exact earlier approved source screen that remains compatible with a target wireframe screen. Record why it is unchanged; do not infer reuse from similarity alone. CIS must verify source authority and hashes.
5. Run `cis design scaffold <change-id> --feature <slug> --component <template-id> --format agent`; customize only uncovered feature content while preserving the shared shell and component behavior. Reused target screens must be absent from the renderer.
6. Run `cis design validate <change-id>` and resolve guideline, shell, component, provenance, reuse-drift, coverage, or renderer errors.
7. Run `cis design render <change-id>`. Successful rendering enters `PausedForReview`; stop all non-review work immediately.
8. Present changed PNGs plus the declared reuse mappings. Only a human may run `cis design approve|reject` with reviewer identity and rationale. Approval records the exact validated wireframe digest, renderer, and PNG manifest together; that manifest combines reused and newly rendered evidence. For a provenance-only refresh after an already approved feature change, run `cis design reconcile`; it may carry authority forward only when the Approved plan pins that current feature and the combined manifest matches the earlier approval.
9. On rejection, revise only wireframes/design assets and resubmit; all other delivery work remains paused. Reused source PNGs are never deleted by target rejection.
10. On approval, preserve exact source-approval, renderer, and combined PNG-manifest hashes before downstream work resumes.

Never hand-edit generated PNGs, bypass the application shell, redraw a common control where a governed template applies, use network assets, continue implementation during review, infer compatibility, or infer approval.
