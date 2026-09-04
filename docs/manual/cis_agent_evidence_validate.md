---
title: "cis agent evidence validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on evidence policy change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-evidence-validate
---

# `cis agent evidence validate`

Validate a handoff's CIS tooling evidence against changed-file obligations.

```text
cis agent evidence validate --evidence <relative-markdown>
  [--changed-file <relative-path> ...] [--strict]
  [--repo <path>] [--format <human|json|agent>]
```

Strict mode fails warnings. Evidence paths and changed files cannot be absolute or escape
the repository.
