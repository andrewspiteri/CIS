---
title: "cis standards conformance"
type: manual
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-standards-conformance
---

# `cis standards conformance`

Displays the canonical rule-to-enforcement mappings.

```text
cis standards conformance [--repo <path>] [--gaps-only] [--format <human|json|agent>]
```

`--gaps-only` returns mappings that are `not-mapped`, `advisory-model`, or not active. The command does not run evaluators and does not claim compliance; it shows the declared enforcement route and evidence location so gaps remain visible.

Exit code `0` means the matrix loaded; `5` indicates invalid repository, catalog, standard, or matrix state.
