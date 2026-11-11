---
title: "cis ui-direction questions"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-11-11"
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

An explicit no-visual-UI direction in the current accepted technical baseline can
resolve applicability even when it spans several paragraphs or no technical
questionnaire has been saved. CIS keeps source provenance; it does not record the
fact as a newly supplied human answer. Machine-facing HTTP, CLI or API contracts
alone do not select a visual interface. Conflicting recorded or canonical visual
direction blocks continuation. The resulting `design/ui-direction.md` record still
requires explicit human approval through `cis ui-direction approve`.
