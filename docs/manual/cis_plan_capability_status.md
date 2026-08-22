---
title: "cis plan capability status"
type: command-reference
status: Active
owner: Andrew Spiteri
last_reviewed: "2026-08-13"
cis:
  stable_id: change-impact-studio:manual:cis-plan-capability-status
---

# `cis plan capability status`

## Synopsis

```text
cis plan capability status [--repo <path>] [--format <human|json|agent>]
```

Reads the canonical `<root>/references/task-type-capability-selections.md` and
compares it with task types registered by loaded assemblies. It reports repository
selections, unavailable selected types, mutually exclusive applicable providers, and
invalid replacement claims. The command is read-only.

Exit code `0` means selections resolve; `2` means configuration/selection errors;
`5` means an unresolved provider conflict requires human selection.

