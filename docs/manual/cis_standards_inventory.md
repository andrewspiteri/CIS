---
title: "cis standards inventory"
type: manual
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-standards-inventory
---

# `cis standards inventory`

Lists standards registered in the configured documentation catalog.

```text
cis standards inventory [--repo <path>] [--target <target>...] [--stack <stack>...] [--status <status>] [--format <human|json|agent>]
```

Target and stack filters use case-insensitive exact values and match any repeated value. A standard declaring `generic` or `all` stack matches every requested stack. Status is not filtered unless supplied. The command reads canonical Markdown and does not invoke a model or mutate repository state.

Exit code `0` means the inventory loaded; `5` indicates invalid repository configuration, catalog, standards, or conformance state. Use `cis standards validate` for detailed diagnostics.
