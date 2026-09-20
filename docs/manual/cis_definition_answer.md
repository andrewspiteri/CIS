---
title: "cis definition answer"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-definition-answer
---

# `cis definition answer`

```text
cis definition answer --page <technical|experience> --id <question-id> --answer <text> --actor <human> [--workspace <path>] [--format <human|json|agent>]
```

Saves or replaces one technical- or experience-direction answer with named human provenance.
Existing and source-derived answers remain editable. When the last answer is resolved, CIS
regenerates the dependent technical intent or UI direction against the new questionnaire digest.
Empty answers, missing actors, unknown IDs, and unsupported pages fail closed.

The result includes the updated wizard pages, questionnaires and preview. The VS Code wizard
uses this response directly after saving: one save operation and one page update, without
another product-definition load, unrelated questionnaire queries or UI-baseline discovery.
