---
title: "cis plan validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-22"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-plan-validate
---

# `cis plan validate`

Validates accepted-impact coverage, work-item identity, complexity, required
decomposition of high work, parent and dependency existence, dependency cycles,
acceptance criteria, validation, outcome criteria, and unresolved decisions.

Imported feature plans additionally require every catalogued task document and its
mandatory sections, the design dependency chain and `design.md` for frontend scope,
the feature requirement provenance set, and `verification.md`. CIS resolves the
recorded feature-specification path inside the repository and compares its current
SHA-256 with the plan provenance. A missing, escaping, or changed source makes the plan
invalid even if its front matter still says `Approved`.

```text
cis plan validate <change-id> [--repo <path>] [--format <human|json|agent>]
```

Exit `0` means valid. Exit `5` means the plan is structurally present but cannot be
approved. Exit `2` means the change or request is invalid.
