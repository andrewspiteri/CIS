---
title: "cis brd feature wizard status"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-11-06"
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
`--format <human|json|agent>`. File-input operations take identity and concurrency
fields from their JSON request. Other operations require `--slug <feature>` and
`--story <story-id>`.
Story input files must be regular JSON files no larger than 256 KiB.

| Subcommand | Additional required input | Effect |
| --- | --- | --- |
| `status` | None | Read the story, tasks, plan state, hashes and progress. |
| `prepare` | `--expected-input-hash` | Generate proposed tasks from the checked story. Optional `--provider` and `--model` choose generation; remote use requires `--allow-remote`. |
| `prepare` | Alternatively, `--input <json-path>` | Validate and retain an explicit task-contract proposal without calling a model. |
| `assess` | `--input <json-path>` | Save a scoped delivery-size assessment and its engineering rationale. |
| `decompose` | `--input <json-path>` | Retain bounded child outcomes, complete parent requirement coverage and exact prerequisites. The parent becomes a delivery container. |
| `approve` | `--expected-plan-hash`, `--expected-revision`, `--actor`, `--reason` | Record human approval of that exact plan. |
| `start` | `--task`, `--expected-plan-hash`, `--expected-revision`, `--actor` | Record dependency-ready task start without launching an agent. |
| `complete` | The `start` inputs, plus `--evidence` and `--criteria-verified` | Record verified completion against the current plan and revision. |
| `execution-plan` | `--task` | Select implementation and distinct review models without execution. |
| `execute` | `--task`, `--expected-plan-hash`, `--expected-revision`, `--expected-execution-hash`, `--actor` | Execute and review the approved task; remote execution requires `--allow-remote`. |
| `work-status` | `--task` | Read canonical ownership, cumulative allowance, correction history, recovery state and the next permitted action. |
| `bind-work` | `--input <json-path>` | Associate the approved story instance with its exact existing approved canonical change/task. This does not allocate work or grant approval. |
| `feedback-suggest` | `--task`, `--expected-revision` | Suggest supported context for genuine missing decisions/information. Optional advice and ordinary required corrections do not require answers. |
| `feedback-save` | `--input <json-path>` | Save human answers from JSON, at most 256 KiB. Identity and concurrency fields come from the request. |

`feedback-save` input contains `slug`, `storyId`, `taskId`, `expectedPlanHash`,
`expectedRevision`, `actor`, and an `answers` object mapping finding IDs to answers.
Send `expectedBasisHashes`, mapping every answered finding ID to the displayed `basisHash`.
Missing or changed hashes are rejected before writing, so a relevant material change requires
reloading before the response is saved. Existing recorded responses remain in history.

Obtain current hashes and revisions from JSON results rather than inventing values.
Stale inputs are rejected. Task-plan approval and saved feedback do not by themselves
establish successful execution or completion. Durable state is stored beside the
feature request under `story-tasks/<story-id>.json`, separately from generation caches.

### Work ownership and continuation

Inspect the current story without starting work:

```text
cis brd feature wizard story work-status --workspace <authority> --slug <feature> --story <story-id> --task <task-id> --format json
```

`taskProgress[].work` reports whether controls are required, whether the canonical binding
is proven, its `identity`, and the existing work-control result in `control`. The latter's
`work` contains the current revision, task/change consumption, reservations, remaining
allowance, restoration reserve, recovery state and durable correction history. Historical
usage can remain unknown; a required missing ledger or allocation is a hold. A correction-only
view records the default correction limit and does not claim a total work allowance.
Status and execution-plan reads do not allocate, record progress/disposition or call a model.

CIS reuses an existing unambiguous approved association. If it is missing, identify the
actual approved canonical change and task through the existing preparation/plan facilities.
Do not use a synthetic `STORY-*`, `*-IMPLEMENT` or `*-REVIEW` stage name as the owner.
Supply this binding envelope with current story tokens:

```json
{
  "slug": "<feature>", "storyId": "<story-id>", "taskId": "<task-id>",
  "expectedPlanHash": "<current planHash>", "expectedRevision": "<current revision>",
  "changeId": "CIS-0001", "workOwnerTaskId": "WORK-001",
  "actor": "<authorized controller>", "reason": "<why this exact approved relationship applies>"
}
```

`CIS-0001` and `WORK-001` are synthetic examples; replace them with the approved identities.
Run `cis brd feature wizard story bind-work --workspace <authority> --input binding.json
--format json`. CIS validates both current authorities and records the association's actor,
reason and time outside implementation meaning. The original task approval, retained
implementation digest and work history remain intact. A conflicting owner or ambiguous
mapping is held; binding cannot replace an established owner or reset its allowance.

Follow the returned `nextAction` using [`cis agent work`](cis_agent_work.md). Allocate once
at the proven change/task boundary under existing authority, including the required
acknowledgment of unmeasured historical effort. Applicable controls on an exact registered
owned participant permit allocation at the authority. A story with retained correction
history can also explicitly enter finite work accounting through this allocation route.
Establishing the allocation adopts work accounting across the authority: all supported
direct-task and story routes, including sibling tasks with no correction history, then
need their canonical owner and applicable finite allocation. One task's allocation grants
no capacity to another. This does not adopt engineering quality policy or grant plan,
design or completion approval. The allocation requires the existing canonical task,
authorized actor/reason and decision references; a profile alone does not opt in.
Progress, reflection and justified disposition
use that same owner and the returned revision. Retry through `execution-plan` and `execute`
with refreshed story tokens; compatible implementation whose work was already accounted can
be reused without charging it again. Unaccounted historical runs require the displayed
owner-scoped `reconcile-story-history` before reuse. Generic agent resume follows the recorded
route and cannot turn story-stage identities into direct canonical tasks.

The default two automatic correction rounds persist across invocations, repositories and
provider changes. A started interrupted round remains recorded. After the limit, inspect
the returned correction relationship and required finding references. An authorized relevant
`simplify` disposition can permit one specific bounded correction within remaining capacity;
it does not replenish two rounds, logical attempts, restoration reserve or total allowance.
After checking the retained contract and exact candidate material, CIS starts that granted
step against its retained findings before a fresh review can replace their wording. A genuinely
revised and approved task contract supersedes obsolete pending correction context; prior
rounds, spent work and history remain recorded.

The granted hypothesis, specific next action and disposition provenance accompany the actual
correction provider request and subsequent independent review. Recovery preserves them;
an extension supplies its new applicable direction while retaining prior direction and work.
Missing pre-fix direction can be recovered only from its exact retained authorized disposition.
If unavailable, follow the reported bounded `simplify` reconciliation action in
[`cis agent work`](cis_agent_work.md#record-a-disposition-and-resume); do not invent an approach.

A started grant whose remaining bound cannot support the minimum operation stays held.
Retrying or reallocating the containing task does not extend that correction's separate cap.
After every associated owned root and child is safely finalized without recovery uncertainty,
an authorized controller can use the [same-round extension](cis_agent_work.md#record-a-disposition-and-resume)
through `cis agent work disposition <change-id> <task-id> --input <disposition.json>
--actor <controller> --reason <rationale>`. Supply kind `simplify`, a fresh revision, the same
`correctionRelationshipId` and current `correctionFindingReferences`, a changed justified
hypothesis, evidence and `nextAction`. When fewer than 30 usable seconds remain,
`correctionMaximumSeconds` authorizes one additional step of 30–3,600 seconds. CIS retains
the same round ID, number, material context and completed stages; spent work, logical
attempts, restoration reserves, hard task/change allowances and provider limits do not reset.
Recovery uncertainty and genuine authority/permission/evidence holds remain independent.
Routine useful reflection and delegated continuation do not require another human sign-off.

Inside an already admitted valid window, a nested stage can use fewer than 30 remaining
seconds. CIS rounds down to whole seconds and preserves the parent/correction deadline;
preparation remains charged. No usable whole second holds before launch. A new root request
still requires at least 30 seconds within the adopted ceiling and restoration reserves.

`controller-assessment-required` means the authorized orchestrating agent must assess the
approach before another expensive continuation. Inspect retained implementation, failed or
valid review evidence, measured cost, reservations, remaining capacity and current revision.
Use `cis agent work progress <change-id> <task-id> --input <assessment.json> --actor <controller>`
at the displayed canonical owner. Record Outcome, nonempty Evidence, Approach, Decision and
the next bounded action; CIS supplies measured Cost. Keep `closeAttempt: false` unless the
logical attempt has ended, then resume this governed story with refreshed tokens. The
[work manual](cis_agent_work.md#record-progress) provides the input envelope.

Automatic `story-execution-summary` records preserve candidate/review outcomes but never
supply controller judgment, even when the candidate is reused. Legacy `story-controller-progress`
records do not qualify. Explicit authorized progress uses the existing owner-wide route;
incidental story records cannot satisfy direct-task reflection. Routine assessment needs
no human acknowledgment, extra reviewer or task `Blocked` transition. Status remains
read-only; independent correction, repeated-blocker, capacity, recovery, permission and
approval holds and logical-attempt counts remain binding.

### Review findings and answers

`execution.structuredFindings` retains severity and explicit `required`/`optional` disposition,
with CIS-stamped repository, run and material association. Display strings are summaries.
Optional minor advice permits application and completion when the other gates pass, and
does not require a correction, answer or suggestion call. A required minor defect remains
corrective; a free-text answer cannot fix or waive it. Only an explicitly identified existing
human decision or question requires an answer; other holds name the permitted next action.
Retained historical blocked questions keep their established answer flow. Inconsistent review verdicts
are held for clarification within the same work context.

An early retry failure before a new review preserves legacy string-only findings and their
still-current saved answers, identity, actor, time and original review evidence. A material
task/source/evidence change still requires renewed assessment. Absent `structuredFindings`
remains distinct from an explicitly empty newer review: a valid current empty collection
replaces obsolete legacy findings. Saved answers never waive required defects or approve completion.

Historical findings without disposition keep their established meaning: blocking, major and
minor are corrective; observations are advisory. Unknown or malformed values do not become
optional. The editor projects these distinctions, cumulative work and next permitted action;
it does not create an additional approval boundary.

Delivery size must have a current assessment of at most medium before implementation
can start. High stories remain containers; unknown or stale scope needs targeted
assessment. Planning and authorized investigation can establish that assessment.
Assess breadth, interfaces, state/concurrency, coordination and uncertainty; a label or
requirement count alone is insufficient. Model-selection `complexity` describes reasoning
strength independently: a bounded security-sensitive task may need a high reasoning tier.
Each executable task also records its own delivery complexity; a reusable type's default
is not an assessment of that instance.

Status returns `deliverySize`, `deliverySizeState`, `deliverySizeReason`,
`planningBasisHash`, `planningRevision`, parent/child links and task eligibility.
The editor displays these same values and reasons. Loading an assessment, child outcome
or task proposal is an explicit save; opening or refreshing the page calls no model.

Use the current status values in an assessment request:

```json
{
  "slug": "account-settings",
  "storyId": "<current story ID>",
  "expectedBasisHash": "<planningBasisHash>",
  "expectedRevision": "<planningRevision>",
  "actor": "Delivery controller",
  "assessment": {
    "size": "medium",
    "rationale": "One bounded preference workflow with an existing persistence boundary.",
    "basisHash": "<planningBasisHash>",
    "behavioralBreadth": "Read and update one preference.",
    "interfaces": "Reuse the accepted account API and settings view.",
    "stateAndConcurrency": "Retain the existing optimistic concurrency rule.",
    "coordination": "One owning repository; use the shared design prerequisite.",
    "uncertainty": "No unresolved delivery uncertainty; accepted decisions remain controlling."
  }
}
```

Run `cis brd feature wizard story assess --workspace <authority> --input assessment.json
--format json`. Assessment records engineering judgment, not a new stakeholder approval.
Decomposition uses the same identity/basis/revision/actor fields and a `children` array.
Each child has a stable `key`, `title`, `definition`, parent `requirementNumbers`,
exact child-key `dependsOn` references and an `assessment` with the fields above.
Child prerequisites may also name an exact existing story ID. Task prerequisites use
a local task ID or `story-id/task-id` for an explicitly shared prerequisite.
When decomposing a parent with shared task prerequisites, map its exact qualified
references into each applicable child's `sharedPrerequisiteTaskIds`. CIS rejects omitted
parent edges; it does not guess by task type or attach every sibling. Uncommitted future
candidates keep that status in their children; delivery remains held until the existing
source and scope workflow commits the outcome. Record the authorized commitment in the
source requirements, then use [BRD reimport](cis_brd_feature_wizard_reimport.md) and
resolve its reported reviews. Moving a draft card between release lists alone does not
commit an unapproved candidate.
CIS retains child IDs, inherited direction and constraints, and rejects incomplete
coverage or invalid dependencies. A container's whole outcome depends on its children,
so a child cannot depend on an ancestor's whole outcome. An exact ancestor preparation
task remains a valid prerequisite. Container completion also requires its own preparation
tasks to finish. Child delivery cannot duplicate parent execution.

For example, a synthetic four-criterion capability can use these two child outcomes.
Keep the assessment envelope's identity/tokens/actor, replace `assessment` with `children`,
and supply a complete assessment in each child (using the shape above):

```json
{
  "children": [
    { "key": "preference-editing", "title": "Edit preferences", "definition": "Read and update preferences with existing compatibility rules.",
      "requirementNumbers": [1, 2], "dependsOn": [], "assessment": "<complete assessment object>" },
    { "key": "preference-export", "title": "Export preferences", "definition": "Export the accepted preference representation.",
      "requirementNumbers": [3, 4], "dependsOn": ["preference-editing"], "assessment": "<complete assessment object>" }
  ]
}
```

The snippet shows the child shape, not a complete runnable request: replace both
assessment placeholders with objects and retain the current envelope before running
`cis brd feature wizard story decompose --workspace <authority> --input children.json --format json`.

A deterministic task proposal uses `slug`, `storyId`, `expectedInputHash` (status
`inputHash`), `expectedRevision` (status `revision`, not `planningRevision`), `actor`,
and `tasks`. Copy each existing task ID/instance key when
revising an unchanged task. Each task carries category, delivery complexity, type/version,
targets/repositories, outputs, criteria, negative criteria, decisions, approval gate,
validation and exact dependencies. A `verificationObligations` entry contains `id`,
`criteria` (exact task criterion strings), `description`, `evidenceRoute` and
`evidenceExpectations`; `sharedGateIds` and `basisReferences` retain applicable shared
obligations and source references. Use an actionable native check, artifact or review
expectation. Several criteria may share a check, and one criterion may need several checks.
Completion must supply actual current evidence; planned checks alone cannot pass.
Applicable adopted gates remain required independently of these task-specific links.
When a task uses an existing canonical plan's approval authority, retain the exact
`changeId` and `workOwnerTaskId`; a type or gate label alone does not prove approval.
The work-owner link retains accounting identity through decomposition; it does not
grant a new approval or turn a child's category into its parent's category.

For example, the `tasks` array in a complete preparation envelope can contain this
synthetic backend task. The accepted story must contain the referenced criterion;
use its current supported type/version and actual repository IDs:

```json
{
  "id": "T1", "instanceKey": "preference-editing/backend",
  "title": "Update preferences", "description": "Implement the accepted update behavior and focused native tests.",
  "repositoryIds": ["api"], "requirementNumbers": [1], "dependsOn": [],
  "category": "backend", "complexity": "medium", "taskTypeKey": "core.backend.behavior", "taskTypeVersion": "1.0",
  "requiredOutputs": ["Preference update implementation and native tests"],
  "acceptanceCriteria": ["An authorized update persists the accepted preference value."],
  "negativeCriteria": ["Preserve the public representation."], "approvalGate": "none",
  "validation": "Run the native preference update tests, including stale-write rejection.",
  "verificationObligations": [{ "id": "preference-update", "criteria": ["An authorized update persists the accepted preference value."],
    "description": "Demonstrate update behavior against the real implementation.", "evidenceRoute": "native",
    "evidenceExpectations": ["Current passing native preference update manifest"], "sharedGateIds": ["unit"] }]
}
```

Run `cis brd feature wizard story prepare --workspace <authority> --input tasks.json --format json`
with the complete preparation envelope. This saves a proposal and runs no model or implementation.

For a reviewed contract change, explicit `prepare --input` can propose a revision of an
approved plan using its current `revision`. Status marks `isRevisionProposal` and holds
execution until the existing `approve` action reviews that exact proposal. Model generation
cannot replace approved work. Reapproval retains the earlier plan, approval and events in
`previousPlans`; compatible task progress can carry forward, while changed work requires
current verification. Instance IDs and work-owner references remain stable.
The material contract of an exact prerequisite also affects dependent completion, including
transitive dependents. Historical completion cannot release downstream work after that basis
changes. Completing the revised prerequisite does not renew the dependent's old completion:
use the existing explicit preparation and approval route, then supply current completion
evidence for the affected task. Unrelated work retains its current approval and progress.
To withdraw an unchanged proposal, submit the approved tasks again with the current
revision. The existing approval and progress resume without another sign-off. Regenerating
an unaccepted proposal preserves unchanged instances and allocates new identities for
changed work, remapping exact dependencies together.

Legacy missing semantics resolve only from an unambiguous maintained contract. Otherwise
the affected task reports missing classification or obligations and retains its history
for targeted reassessment. It never silently becomes low-complexity implementation.
If a task is otherwise unchanged, adding missing legacy semantics or first recording a
prerequisite's material basis preserves in-progress/applied work and marks
`needsVerificationRenewal`. Inspect that work and refresh the current contract's
verification and review; the old completion claim alone does not satisfy new obligations.
Record completion after explicitly re-verifying every criterion and supplying the current
required evidence. Adopted completion still checks the current contract's receipts and gates.
Human work can use this completion route without running an agent; scoped agent verification
and review remain available when useful.
Unchanged pre-3B records retain their recorded boundary and execution-digest version.
Newly reviewed contracts bind the expanded delivery boundaries, including defaults,
compatibility and interfaces, without rewriting older approvals on a status read.

Status is observational. Assessments expose their current, changed or unknown supporting scope
without resetting retained human decisions. Delivery comparisons use content and relevant
dependencies, including newly relevant and deleted inputs; touching a file or rebuilding an
equivalent graph does not establish impact. Finding identity is stable across run IDs/order;
`applicabilityState`, `applicabilityReason` and `retainedAnswer` distinguish a current response
from history needing renewal. Responses never turn a failed review into a pass. Native execution
identity, evidence qualification and completion receipt checks remain independent gates.

Approved backlog outcomes can enter this wizard through
`cis brd feature wizard start-backlog --item <HLT-ID> --actor <human>
--expected-input-hash <hash> --workspace <path> --format json`.
Use the current navigation result to obtain the checked input hash. This creates or
resumes the linked draft; it does not grant feature or task-plan approval.
