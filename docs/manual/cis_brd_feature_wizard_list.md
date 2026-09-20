---
title: "cis brd feature wizard list"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-feature-wizard-list
---

# `cis brd feature wizard list`

Lists saved feature requests so the VS Code feature wizard can resume after closing or reloading.

```text
cis brd feature wizard list --workspace <authority>
  [--format <human|json|agent>]
```

Returns a `requests` array of intake plans. This read does not create a wizard session, change documents or invoke a model.

Open **CIS: Open Feature Definition Wizard** in VS Code for the guided interface.
See [feature intake](cis_brd_feature_intake.md) for the initial source and repository setup.
