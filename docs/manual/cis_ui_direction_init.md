---
title: "cis ui-direction init"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ui-direction-init
---

# `cis ui-direction init`

```text
cis ui-direction init [--workspace <path>] [--format <human|json|agent>]
```

Generates or reconciles `design/ui-direction.md` from the Active solution-design bundle, completed
questionnaire, Active design guidelines, and available UI-framework profiles. The managed section
is refreshed idempotently; human content outside it is preserved. Any material regeneration clears
approval and returns the document to Review Required.
