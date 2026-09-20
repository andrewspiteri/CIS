---
title: "cis ui-direction questions"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ui-direction-questions
---

# `cis ui-direction questions`

```text
cis ui-direction questions init [--workspace <path>] [--format <human|json|agent>] [--summary]
cis ui-direction questions status [--workspace <path>] [--format <human|json|agent>] [--summary]
cis ui-direction questions answer <UI-Q-ID> --answer <text> --actor <human> [--workspace <path>] [--format <human|json|agent>]
```

Requires an Active/current overall solution design and component sheet. `init` creates or
reconciles the twelve-question canonical questionnaire. Existing-project facts may be Derived from
approved surfaces and UI-framework profiles; all remaining choices require a named human answer.
An answer can be edited and saved again. Source drift reopens prior human answers for review rather
than silently carrying them into a changed architecture.

Initialization also checks the owned UI implementation through [`cis ui-direction baseline`](cis_ui_direction_baseline.md).
Observed shell, styles, typography, components and responsive behavior prefill advisory
directions. When code supplies an existing component system, it is presented for human
confirmation instead of automatically adopting a generic framework-profile choice.
Existing human answers are preserved. Discovery never resolves subjective choices itself.
