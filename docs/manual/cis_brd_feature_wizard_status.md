---
title: "cis brd feature wizard status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-05"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-feature-wizard-status
---

# `cis brd feature wizard status`

Projects the eight feature-definition pages from the saved intake, reviewed answers and current product baseline.

```text
cis brd feature wizard status --slug <feature-slug> --workspace <authority>
  [--format <human|json|agent>]
```

The result contains `plan`, `revision`, `baselineCurrent`, `reviewed` and `pages`. Each page reports status, outstanding items, saved answers, suggested BRD excerpts and links to product baseline documents. Suggestions are not saved answers. Source decisions remain unresolved until explicitly answered. Missing, changed or unsafe retained source files block the read. A baseline or repository-boundary change retains answers and requires renewed review.

Open **CIS: Open Feature Definition Wizard** in VS Code for the guided interface.
See [feature intake](cis_brd_feature_intake.md) for the initial source and repository setup.

Use [reimport](cis_brd_feature_wizard_reimport.md) when the source BRD was updated
externally. Status returns the selected retained source in `plan.sourcePath` and
previous BRD/review links in `sourceHistory`. A source update retains answers but
requires renewed review against the new requirements.

Technical direction has four topics; architecture, contracts and experience
each have three; delivery has three user-story lists and five planning questions. Each uses stable field IDs and relevant BRD excerpts,
with existing product context as a fallback. Nested source sections stay with their parent,
and excerpts retain headings and table formatting. C4 coverage is an explicit proposal.
Suggestions do not become reviewed answers on read. Additional narrative notes are optional.
Technical context comes from the selected product document, including imported technical
intent, rather than an unused scaffold. TODO and TBD placeholders are not usable direction.
When the product baseline is Active and current, settled platform, security and operations
topics appear as **Inherited approved direction**. Only feature-specific changes and
unresolved choices require answers. A fully inherited technical page is complete without
recording duplicate human answers; use **View next step** to continue. Inherited context
also accompanies planning for features with retained source-BRD provenance. Saved overrides
remain visible, and changed source bindings still require renewed review.
An older reviewed narrative remains available and valid against its existing binding; the
new questions are optional refinements for that legacy review, without fabricated answers.

The extension shows numbered question navigation, formatted answer previews and an
**Edit answer** control. **Save and continue** advances only if the CLI reports the page
complete. Partial answers remain on the current step, and **Save without leaving** saves
in place. The final step still requires all preceding reviews and a current product baseline.

**Delivery and acceptance** starts with Foundation (required regardless of release scope),
MVP (first release), and Post-MVP (later delivery). CIS prepares editable, capability-level
user stories and acceptance outlines from the retained feature BRD. Requirement sections
take precedence over duplicate scope summaries; matching integration sections contribute
to the same story. Cross-cutting platform and non-functional requirements are suggested as
Foundation. Review these proposed categories before planning repository work.

BRD outlines are requirements, not proof that implementation is missing. Use the
[delivery reconciliation](cis_brd_feature_wizard_delivery.md) action to compare them
with code in all owned repositories. Review ownership direction, existing capability,
remaining work and conflicts before using the assessed lists as drafts.

Excluded capabilities never become later-delivery stories just because they are outside
the MVP. Explicit future ideas appear as uncommitted candidates, and an empty Post-MVP
list says that no later stories are established. Missing Foundation or MVP evidence leaves
that list empty for the reviewer. Source sections and requirement IDs are retained in hidden
Markdown comments. The full BRD remains the source for acceptance requirements beyond
the displayed outlines. Large suggestions report when further stories need review.

These suggestions are deterministic and do not call a model or write files during status.
Expand a story to read its outline. Each MVP card has **Move to Post-MVP**, and each
Post-MVP card has **Promote to MVP**. The buttons move the complete story, acceptance
text and hidden source comments into the other draft list. Foundation cards have no
category button. Both lists update without a CLI call or workspace refresh; save the
delivery page to record the change. Moving the last story leaves an explicit empty-list
statement. If the displayed card no longer matches edited text, the edits are retained
and the card is refreshed before retrying the move.

Use **Edit user stories** to revise the text. Saving preserves the reviewed text; subsequent reads never overwrite it
with regenerated suggestions. Previously saved planning answers remain available, and
new lists require review before a structured delivery page is complete. Earlier single
narrative reviews retain the legacy behaviour described above.

## Story task commands

The related `cis brd feature wizard story` commands expose the workflow shown in the
[editor story view](cis_vscode_extension.md). All accept `--workspace <path>` and
`--format <human|json|agent>`. Except `feedback-save`, each requires `--slug <feature>`
and `--story <story-id>`.

| Subcommand | Additional required input | Effect |
| --- | --- | --- |
| `status` | None | Read the story, tasks, plan state, hashes and progress. |
| `prepare` | `--expected-input-hash` | Generate proposed tasks from the checked story. Optional `--provider` and `--model` choose generation; remote use requires `--allow-remote`. |
| `approve` | `--expected-plan-hash`, `--expected-revision`, `--actor`, `--reason` | Record human approval of that exact plan. |
| `start` | `--task`, `--expected-plan-hash`, `--expected-revision`, `--actor` | Record dependency-ready task start without launching an agent. |
| `complete` | The `start` inputs, plus `--evidence` and `--criteria-verified` | Record verified completion against the current plan and revision. |
| `execution-plan` | `--task` | Select implementation and distinct review models without execution. |
| `execute` | `--task`, `--expected-plan-hash`, `--expected-revision`, `--expected-execution-hash`, `--actor` | Execute and review the approved task; remote execution requires `--allow-remote`. |
| `feedback-suggest` | `--task`, `--expected-revision` | Suggest answers to retained review findings. Provider/model selection and remote authorization work as for preparation. |
| `feedback-save` | `--input <json-path>` | Save human answers from JSON, at most 256 KiB. Identity and concurrency fields come from the request. |

`feedback-save` input contains `slug`, `storyId`, `taskId`, `expectedPlanHash`,
`expectedRevision`, `actor`, and an `answers` object mapping finding IDs to answers.

Obtain current hashes and revisions from JSON results rather than inventing values.
Stale inputs are rejected. Task-plan approval and saved feedback do not by themselves
establish successful execution or completion. Durable state is stored beside the
feature request under `story-tasks/<story-id>.json`, separately from generation caches.

Approved backlog outcomes can enter this wizard through
`cis brd feature wizard start-backlog --item <HLT-ID> --actor <human>
--expected-input-hash <hash> --workspace <path> --format json`.
Use the current navigation result to obtain the checked input hash. This creates or
resumes the linked draft; it does not grant feature or task-plan approval.
