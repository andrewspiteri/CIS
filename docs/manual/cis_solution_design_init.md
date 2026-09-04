---
title: "cis solution-design init"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-solution-design-init
---

# `cis solution-design init`

```text
cis solution-design init [--workspace <path>] [--format <human|json|agent>]
```

Requires an Active, current technical intent. Creates or reconciles
`architecture/overall-solution-design.md` and `references/component-sheet.md` as one governed
bundle, registers both in the documentation catalogue, and projects the structured `TI-MOD-*`
component ownership and applicable architecture sections. Reruns preserve human-authored
sections and a current approval when the exact bundle is unchanged. Source or managed-content
changes reset approval for both files.

Exit code `5` means an upstream readiness or validation gate is blocked; collision and invalid
workspace errors are reported without replacing files.
