---
title: "cis brd feature wizard save"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-21"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-feature-wizard-save
---

# `cis brd feature wizard save`

For a per-story delivery planning choice, pass `deliveryReview` with empty `answers`.
This preserves page answers and repository work, validates the current implementation
hash and records the human choice without marking the story implemented. See
[delivery decisions](cis_brd_feature_wizard_delivery.md) for the input and wizard flow.

Saves human-reviewed answers for one feature-definition page without refreshing the full workspace.

```text
cis brd feature wizard save --input <answers.json> --workspace <authority>
  [--format <human|json|agent>]
```

The JSON input contains `slug`, `page`, `answers` (field ID to text), `actor` and the exact `expectedRevision` from status. Pages are business, technical, architecture, contracts, experience, delivery and review. Save accepts partial answers; blank answers and placeholders remain unresolved. It preserves source requirements and product approvals, writes a managed review block in the canonical feature request and returns the updated page projection. Stale revisions, unknown fields, unsafe paths and concurrent edits are rejected without overwriting work. Baseline evaluation occurs once per save; checked input hashes guard the write. Final review requires all prior pages and a current activated product baseline. It records review of a proposed definition, not backlog, implementation or release approval.

Open **CIS: Open Feature Definition Wizard** in VS Code for the guided interface.

Delivery user stories use the answer fields `delivery-stories-foundation`,
`delivery-stories-mvp`, and `delivery-stories-post-mvp`. Each holds a Markdown list
of stories with headings, narrative and acceptance outlines. Review the prefilled
lists, including uncommitted future candidates, and save them with the other delivery
answers. Hidden source comments are retained. The wizard's **Move to Post-MVP** and
**Promote to MVP** buttons move the complete story between these draft fields; saving
persists both fields together under the existing revision check. An omitted field preserves its saved
value; blank text leaves a required list unresolved. Saving records a proposed feature
breakdown and does not approve or populate the governed product backlog.

On the `delivery` page, the `delivery-ownership` answer records which existing capabilities must be reused and
which responsibilities belong to the feature. [Delivery reconciliation](cis_brd_feature_wizard_delivery.md)
uses this saved direction without approving stories or rewriting the source BRD.
The input may also contain `repositoryWork`, an array of
`id`, `repositoryId`, `title`, `scope`, `dependsOn` and `changeIds` objects. IDs are
unique within the feature. Each item targets a registered product-owned participant;
dependency cycles, missing sibling dependencies, unknown repositories and missing or
unsafe change links are rejected. An omitted array preserves the existing breakdown;
an empty array explicitly clears it. The Markdown request shows the saved work and
dependencies beside the human delivery review. Saving another page preserves it.

The extension offers **Add repository feature**, dependency and existing-change selectors,
and a single save for the delivery page and its breakdown. Unsaved work is retained per
feature; opening another feature does not replace it. New reviews bind to that feature's
repository boundary, so an unrelated repository registration does not invalidate them.
Current legacy bindings remain readable and are carried forward on the next explicit save.

See [feature intake](cis_brd_feature_intake.md) for the initial source and repository setup.

To review a generated screen, save the `experience` page with empty `answers` and a
`screenReview`. Take the wizard revision, screen ID, screen revision and preview input
hash from current status results. For example:

```json
{
  "slug": "example-feature",
  "page": "experience",
  "answers": {},
  "actor": "Reviewer name",
  "expectedRevision": "<wizard revision>",
  "screenReview": {
    "screenId": "<screen plan id>",
    "screenRevision": "<screen revision>",
    "previewHash": "<preview input hash>",
    "decision": "amend",
    "feedback": "Rename Submit to Continue and remove the optional phone field."
  }
}
```

Decisions are `amend`, `not-needed` and `restore`. Amendments require feedback; the
other decisions accept an optional note. Feedback is limited to 2,000 characters.
Saving records the human choice and image binding in the feature request, preserving
review history without marking questionnaire answers reviewed. It does not call a
model. Use [screens prepare](cis_brd_feature_wizard_screens.md) with the returned wizard
revision and screen key to apply an amendment. The extension combines these two steps
and retains the request if generation fails. An excluded screen whose image cache was
lost can be restored using its saved review key as `screenId` and its saved image revision.
