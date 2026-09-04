---
title: "cis references diff"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-references-diff
---

# `cis references diff`

Compare canonical reference rows and discovered source identities with a Git baseline.

```text
cis references diff --base <commit|tag|branch>
  [--repo <path>] [--format <human|json|agent>]
```

The result reports added, removed, and changed canonical identities and warns when
source identities changed without the matching canonical document changing.
