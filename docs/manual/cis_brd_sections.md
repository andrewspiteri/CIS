---
title: "cis brd sections"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-sections
---

# `cis brd sections`

Suggest content for unmatched or empty BRD sections using facts already present in the
current BRD. Suggestions remain local proposals until a human reviews and applies the
exact diff. Applying a proposal is not business approval of the BRD.

```text
cis brd sections suggest --workspace <path> [--provider <provider>] [--model <model>] [--allow-remote] --format json
cis brd sections apply --workspace <path> --proposal <proposal-id> --actor <human> --format json
```

In the Business Definition wizard, select **Suggest missing sections**, choose a model,
and review the proposed additions and removals. Supporting excerpts are available above
the diff. **Approve changes and refresh graph** applies the reviewed proposal, rebuilds
the workspace graph, and reloads the wizard and workspace. Cancel or close the review
to leave the BRD unchanged. Failed graph refresh reports that the document was saved
and allows the graph refresh to be retried.
After saving, the wizard regains focus and shows the number of sections saved, graph
and workspace refresh progress, and any remaining review items. Completion messages
do not require dismissal before the wizard becomes available again. If only Open
questions remains missing, the next step is human review of that section; the model
cannot infer that no unresolved questions remain.

The prompt asks the model to choose each new section's location from the existing
document outline, preserving logical and chronological reading order. The diff uses
those proposed locations and the surrounding heading level. CIS validates the selected
anchors; it does not choose substitute locations or append sections when placement is
missing or invalid. Existing empty sections are filled in place. Existing numbering,
cross-references, and hidden evidence are preserved. Placement remains part of human
diff review. Older proposals without placement information must be regenerated.

The model picker includes local models, signed-in Codex and Claude Code accounts, and
configured compatible API providers. Unavailable providers show setup details. Choosing
an account provider requires permission to send the BRD before generation starts.
Remote providers receive one attempt of up to ten minutes using the full narrative or
selected excerpts for larger documents.
Local generation and retries share a four-minute budget. The wizard shows elapsed time;
failed requests include the error reason in the CIS output log. A timeout applies no
document changes. Unsupported sections remain review gaps.

The model receives the current BRD narrative, excluding front matter and hidden managed
evidence. BRD files up to 512 KiB are supported. When the narrative exceeds 128,000
characters for a remote model, or 24,000 for a local model, CIS selects relevant
passages from across the document with a separate allowance for each missing section.
Long paragraphs and tables are searched beyond their opening lines. The selected
evidence is bounded to 96,000 characters remotely or 24,000 locally. This does not
change or shorten the original BRD. The proposal clearly identifies excerpt-based
coverage; check the full document for omitted conditions and conflicting statements.
Remote providers require explicit disclosure permission. Every proposed section needs
exact supporting excerpts from the text actually supplied to the model; unverifiable or unsupported suggestions are omitted and
remain review gaps. Matching excerpts do not establish semantic correctness: review
the proposed meaning, scope, and uncertainty before applying it.

The proposal is bound to the original BRD and exact proposed content. New edits require
a new proposal. Applying preserves existing narrative and source decisions, saves a
backup, records the human actor, and resets document approval to Review Required.
Proposals are retained under `.cis/local/brd/section-proposals/`. CLI users must run
`cis graph build --workspace <path>` after applying changes; the wizard does this
automatically.

Exit code `0` means a proposal was produced, no supported changes were available, or
the reviewed proposal was applied. Exit code `2` reports a blocked operation, such as
stale content, unavailable model, invalid output, or missing disclosure permission.
