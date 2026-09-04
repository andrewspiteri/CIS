---
title: "cis definition init"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-04"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-definition-init
---

# `cis definition init`

```text
cis definition init [--workspace <path>] [--format <human|json|agent>]
```

Starts or resumes the eight-page high-level product-definition session. It inventories the
classification-selected dictionaries and refreshes safe derived diagram, dictionary-index, and UI
preview records when their prerequisites exist. The resumable session is stored under
`.cis/local/definition-wizard/`; canonical product content remains Markdown.

The command is idempotent. It does not approve any artifact and reports incomplete pages as
warnings so the UI can guide the user rather than treating ordinary draft progress as a failure.
