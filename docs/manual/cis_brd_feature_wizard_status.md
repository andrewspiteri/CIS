---
title: "cis brd feature wizard status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-feature-wizard-status
---

# `cis brd feature wizard status`

Projects the eight feature-definition pages from the saved intake, reviewed answers and current product baseline.

```text
cis brd feature wizard status --slug <feature-slug> --workspace <authority>
  [--format <human|json|agent>]
```

The result contains `plan`, `revision`, `baselineCurrent`, `reviewed` and `pages`. Each page reports status, outstanding items, saved answers, suggested BRD excerpts and links to product baseline documents. Suggestions are not saved answers. Source decisions remain unresolved until explicitly answered. Missing, changed or unsafe retained source files block the read. A baseline or repository-boundary change retains answers and requires renewed review.

Open **CIS: Open Feature Definition Wizard** in VS Code for the guided interface.
See [feature intake](cis_brd_feature_intake.md) for the initial source and repository setup.

Use [reimport](cis_brd_feature_wizard_reimport.md) when the source BRD was updated
externally. Status returns the selected retained source in `plan.sourcePath` and
previous BRD/review links in `sourceHistory`. A source update retains answers but
requires renewed review against the new requirements.

Technical direction has four distinct questions; architecture, contracts and experience
each have three; delivery has four. Each uses stable field IDs and relevant BRD excerpts,
with existing product context as a fallback. Nested source sections stay with their parent,
and excerpts retain headings and table formatting. C4 coverage is an explicit proposal.
Suggestions do not become reviewed answers on read. Additional narrative notes are optional.
An older reviewed narrative remains available and valid against its existing binding; the
new questions are optional refinements for that legacy review, without fabricated answers.

The extension shows numbered question navigation, formatted answer previews and an
**Edit answer** control. **Save and continue** advances only if the CLI reports the page
complete. Partial answers remain on the current step, and **Save without leaving** saves
in place. The final step still requires all preceding reviews and a current product baseline.
