---
title: "Human-readable content terminology"
type: reference
status: Draft
version: "0.1"
scope: Repository
last_reviewed: "2026-09-20"
review_cadence: on terminology change
owner: "Andrew Spiteri"
cis:
  stable_id: change-impact-studio:reference:human-readable-content-terms
---

# Human-readable content terminology

These are candidate explanations, not approved global renames. Select the wording only after checking the domain meaning in that context. Keep exact technical names in source references, commands, identifiers, and detail views.

| Technical concept | Candidate explanation | Meaning to preserve |
| --- | --- | --- |
| Technical intent | Technical direction: the product's technical choices and constraints | More than a single technology choice; distinguish the document from questionnaire answers. |
| Change dossier | Change record: the outcome, scope, decisions, and evidence for a proposed change | Do not imply a generic ticket is the complete governed record. |
| Canonical document | The authoritative document | Do not imply an AI summary replaces it. |
| Derived view | A view rebuilt from source records | Its freshness and existence do not establish approval. |
| Evidence drift | Relevant source evidence has changed | Do not assert which file changed unless the evidence identifies it. |
| Bounded plan | An implementation plan with explicit limits on scope | Preserve exclusions, approval scope, and conditions. |
| Stale | Needs review or refresh because its source has changed | Choose review versus refresh from the actual rule, not from the label alone. |
| Ready for approval | The required checks are complete; human approval is still needed | Never shorten to Approved or Ready to implement. |

## Examples from the reviewed extension

The current technical page uses the action label `Infer from existing repositories`. A candidate is `Draft technical direction from code`, with helper text explaining that it creates a draft and does not resolve human decisions. Verify the handler's scope before adopting the label.

The current experience page says `Refresh UI direction to reconcile the recorded answers with current evidence. Existing human answers are preserved.` A candidate is `Refresh UI direction to check your saved answers against the latest evidence. Your saved answers are kept.` Preserve the actual refresh behavior and the distinction between saved answers and approval.

For `Preview available · Direction pending`, a candidate is `Preview available. UI direction is not ready for approval.` This wording is appropriate only for the branch where the checked page is not complete and current. Retain any specific blocker and its next action nearby.
