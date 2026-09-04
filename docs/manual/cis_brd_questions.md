---
title: "cis brd questions"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-01"
review_cadence: "on BRD question workflow change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-questions
---

# `cis brd questions`

List and answer human decisions recorded under the canonical BRD's `## Open questions`
section.

```text
cis brd questions list [--workspace <path>] [--format <human|json|agent>]

cis brd questions guidance [--workspace <path>] [--format <human|json|agent>]

cis brd questions suggest [--provider <provider>] [--model <model>]
  [--allow-remote] [--workspace <path>] [--format <human|json|agent>]

cis brd questions answer <BRD-Q-NNN> --answer <text> --actor <human>
  [--workspace <path>] [--format <human|json|agent>]
```

`list` recognizes the governed question table and numbered or bulleted lines ending in a
question mark. A numbered draft receives deterministic IDs in document order. `None` and
`No open questions` represent an empty question set.

`guidance` returns every question with up to three deterministic excerpts selected from
the canonical BRD. It also projects current advisory answer suggestions from
`.cis/local/brd/questions/suggestions.json`. Suggestions are bound to the exact question
text and contextual excerpts; answering another question does not invalidate them, while
a question or relevant BRD-context change does.

`suggest` submits only the displayed questions and bounded excerpts to a text-generation
provider. With no provider it selects an available local provider only. Remote use requires
both an explicit provider and `--allow-remote`. The model must return `null` when the
evidence cannot support an answer; CIS rejects unknown question IDs, duplicated suggestions,
unsupported context citations, placeholders, malformed JSON, and stale results. CIS batches
large question sets to remain within small-model context windows and retries a malformed batch
per question. An answer is accept-ready only when the model reports at least medium confidence
and cites one or more displayed context excerpts; low-confidence or uncited output is retained
only as an explicit no-supported-answer result. Suggestion
generation never writes the canonical BRD or supplies human actor provenance.

The first `answer` converts the recognized list to this canonical structure while
preserving every question:

```markdown
| ID | Question | Answer | Answered by | Answered at UTC |
| --- | --- | --- | --- | --- |
| BRD-Q-001 | Who owns the outcome? | Business sponsor | Andrew Spiteri | 2026-08-29T12:00:00Z |
```

Answers are bounded single-cell text and must not contain `TODO` or `TBD`. Each write
records the human actor and UTC timestamp. Repeating the same answer by the same actor is
idempotent. Changing canonical content clears prior BRD approval. All recognized
questions must be answered before `cis brd validate` can pass.

Recording answers does not immediately repeat the independent review. `cis brd review
freshness` recognizes only the governed answer, actor, and timestamp fields as a compatible
intermediate change while questions remain. When every question is answered, run `cis agent
incorporate brd-questions`: it performs a bounded one-file revision that reflects those
decisions in the relevant business sections. That substantive revision always requires a
fresh independent review before validation and approval. Changing a question identity or
wording, adding or removing a question, or editing any other BRD content also supersedes the
earlier review.

The VS Code command **CIS: Answer BRD Open Questions** opens one editor page containing
the complete question list, relevant BRD context, current answers, and advisory suggestions.
An answer is recorded only when the named human selects **Accept suggestion** or saves an
edited text area. The page runs an available local model only when **Generate advisory
suggestions** is selected; transmitting the displayed context to a configured remote provider
requires a separate disclosure confirmation.
After the last answer, the page routes to **Update BRD from answered questions**, then to a
different-provider independent review of the updated document. It never routes directly from
the answer page to BRD approval.
