---
title: "cis brd feature wizard screens"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-20"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-feature-wizard-screens
---

# `cis brd feature wizard screens`

Generate draft application screens from a feature's retained BRD, saved experience
answers and discovered owned UI baseline.

```text
cis brd feature wizard screens status --slug <feature> --workspace <authority>
cis brd feature wizard screens prepare --slug <feature> --expected-revision <revision>
  --workspace <authority> [--screen <screen-key>] [--format human|json|agent]
```

Use the revision from [wizard status](cis_brd_feature_wizard_status.md).
Preparation requires a local model and Node.js. Remote generation is not enabled.
CIS selects relevant BRD passages, checks a bounded data-only screen plan, then renders
it with trusted shared controls. Model-generated code and repository scripts are never run.
The screen proposal identifies frontend type, proposed route, fields, actions and states.
Routes and sample data are illustrative; human review remains necessary.

Derived screens live under `.cis/local/feature-screens/<feature>/`. Status never calls
the model. It reports missing, current, stale, changes-requested or no-ui. Source, experience direction,
canonical UI direction and discovered implementation styles bind the cache. Unrelated
page saves do not invalidate it. A per-feature lock prevents duplicate concurrent
generation; failed or stale runs preserve the previous gallery. No canonical product
document, answer or delivery approval is modified by these commands.

The VS Code Experience step explicitly saves the displayed answers before preparing.
It displays images, full-size tabs, JPG export and proposed behaviour for review.
Each screen has a feedback field and **Apply changes to this screen**. CIS first saves
the request with the human actor, then uses the local model to regenerate that screen.
Other images remain unchanged. Failed amendments retain the previous image and the
saved request, so it can be corrected or retried. Unsaved feedback survives navigation.

**Not needed** saves an exclusion without calling the model. Excluded screens collapse
in the gallery and stay excluded after reopening or regenerating, including after a
cache rebuild. Use **Include this screen again** to restore one. Reviews are matched to
their BRD heading and frontend type; section renumbering does not lose the decision.

Screen decisions are canonical feature review history, saved through
[wizard save](cis_brd_feature_wizard_save.md), rather than disposable image-cache state.
An amendment binds to the exact displayed image revision and preview hash. Stale tabs
cannot overwrite a newer image or review. Unsaved questionnaire answers do not block
screen feedback and remain unsaved: amendments apply to the displayed preview using
the saved direction. To include those questionnaire edits, save answers and regenerate.
If the saved context has actually changed, each disabled review control explains that
the preview needs regeneration and offers the button to do it.
`prepare --screen <screen-key>` applies the saved
amendment for that screen; ordinary preparation also applies pending requests.

This earlier definition preview does not enter or release the governed delivery design gate.
