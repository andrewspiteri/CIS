---
title: "cis brd feature wizard reimport"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-20"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-feature-wizard-reimport
---

# `cis brd feature wizard reimport`

Updates the source BRD of an existing high-level feature without recreating its
repository or discarding its reviewed answers and repository work.

```text
cis brd feature wizard reimport --slug <feature-slug> --source <updated.md>
  --actor <person> --workspace <authority> --dry-run [--format <human|json|agent>]
cis brd feature wizard reimport --slug <feature-slug> --source <updated.md>
  --actor <person> --workspace <authority> --yes --expected-plan <preview-planHash>
  [--format <human|json|agent>]
```

Preview reports the old and new source hashes and byte counts, questions added or
removed, matched saved decision answers and the pages that need renewed review.
It writes no files. Apply requires the exact preview hash; changed inputs or a
changed feature review require a new preview. Identical source bytes are a no-op.
Sources must be safe local UTF-8 Markdown files no larger than 2 MiB.

The feature keeps its identifier, implementation repository, integration boundary,
narrative answers and repository work. Decision answers follow an unchanged question
only when its text occurs exactly once in both sources. Changed, removed or ambiguous
questions never acquire an unrelated answer by ordinal position. Earlier answers
remain in revision history. All substantive pages and the final review require renewed
human review; reimport grants no approval and leaves the product baseline unchanged.

Source revisions and prior request bytes are retained beneath
`.cis/inputs/features/<slug>/`. A single atomic request replacement selects the new
revision. Interruption before that replacement leaves the previous request usable;
existing revision files are never overwritten. Human prose outside generated source
fields and the managed review is preserved. Externally modified generated fields
block reimport until their edits are preserved and the fields restored.

In VS Code, open **High-level features**, select the feature and use **Reimport BRD**.
Choose the updated file, open **Compare BRD changes** for a separate diff tab and then
select **Apply updated BRD**. Save or discard unsaved page edits before applying an
update. **Previous BRDs and saved answers** provides revision links after reopening.
If another process reimports while drafts are open, those drafts remain available in
a separate recovery section and are not assigned to the new questions automatically.

Exit codes: `0` successful preview/update or unchanged; `3` confirmation required;
`5` invalid input, stale preview or blocked update.
