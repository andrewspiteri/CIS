---
title: "cis standards validate"
type: manual
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-standards-validate
---

# `cis standards validate`

Validates the canonical standards collection and rule-level conformance matrix.

```text
cis standards validate [--repo <path>] [--strict] [--format <human|json|agent>]
```

Validation checks location below `standards/`, catalog type and authority, required metadata and sections, lifecycle values, stable document-ID alignment, repository-wide rule-ID uniqueness, allowed enforcement states, and a conformance row for every active rule.

`advisory-model` and `not-mapped` enforcement produce warnings because they cannot prove a breach or compliance. `--strict` turns warnings into failures. Exit code `0` means valid under the selected strictness; `5` means errors or strict warnings exist.
