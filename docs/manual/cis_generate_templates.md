---
title: "cis generate templates"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-generate-templates
---

# `cis generate templates`

List deterministic repository-owned templates and variables.

```text
cis generate templates [--repo <path>] [--format <human|json|agent>]
```

Templates are discovered beneath the configured documentation `templates/` folder.
