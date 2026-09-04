---
title: "cis impact policy analyse"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on policy target or analysis change"
cis:
  stable_id: change-impact-studio:manual:cis-impact-policy-analyse
---

# `cis impact policy analyse`

Generate bounded deterministic candidates for a policy's governed targets.

```text
cis impact policy analyse --policy <relative-markdown> [--strict]
  [--repo <path>] [--format <human|json|agent>]
```

Reports are derived under `.cis/local/impact/policy/`. Explicit `targets:` are required
for a strict clean result. Supported targets are backend, frontend, documentation,
infrastructure, security, api, testing, verification, release, and repository-governance.
