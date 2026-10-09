---
title: "cis test trace"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-test-trace
---

# `cis test trace`

Prove that every generated `TC-*` identity appears in a passed case in an exact reconciled run.

```text
cis test trace <change-id> --run <workflow-run-id> [--repo <path>] [--format <human|json|agent>]
```

Source-code mentions and file paths are routing evidence only; they cannot satisfy executed traceability.

In a workspace, run this command against the documentation authority that owns the catalogue. CIS reads execution manifests from registered repositories and validates the native suite profiles of the repositories that produced those manifests. A documentation-only authority does not need an executable suite profile unless it also produced test evidence. A manifest whose repository identity differs from its registered location is rejected.

A passed identity establishes a link to execution, not coverage of every criterion behind that identity. Review the individual cases and the full required-suite results before claiming acceptance.
