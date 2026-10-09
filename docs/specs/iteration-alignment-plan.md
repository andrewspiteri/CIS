---
title: "Iteration completion and standards alignment plan"
type: implementation-plan
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-10-09"
review_cadence: "before implementation and at each milestone"
cis:
  stable_id: change-impact-studio:plan:iteration-alignment
---

# Iteration completion and standards alignment plan

Execution sequencing and .NET trial readiness are defined by the [consolidated implementation plan](engineering-defaults-implementation-plan.md). This document retains the iteration/completion design; its task labels are cross-references, not a duplicate execution ledger.

## Implementation disposition — 9 October 2026

The Windows .NET milestone now implements adopted engineering completion gates and qualifies graph/alignment reconciliation and assigned-reviewer checks. Existing repositories retain their policy until explicit adoption. See the [completed milestone and evidence](../../planning/engineering-defaults-trial-readiness.md) and [trial guide](../../planning/engineering-defaults-dotnet-trial-guide.md). The detailed proposal below retains design rationale and later scope; its Draft metadata is not changed into policy or product approval.

## Agreed workflow direction and planning boundary

The iteration instructions should require a closing alignment sequence whenever a task's implementation and verification finish: rebuild the graph, update CIS dependencies, and review alignment. Run it before recording the task as complete or handing its result to the next task. Do not defer this maintenance until repository onboarding or the final feature sweep.

This records the user's direction on 5 October 2026. The sequence below is a Draft implementation proposal addressing SC-12 through SC-16 in the [improvement inventory](../references/cis-assurance-improvement-candidates-2026-10-05.md). Standards alignment, verified completion and reliable review context belong to this same process: all applicable gates must pass before a task is considered complete. It changes no active instruction, command, standard or completion gate. The implemented command composition and qualified update behavior are now recorded in the completed milestone; this design document remains distinct from adopted policy.

An iteration here is a bounded task delivery cycle, not each tool call or conversational turn. Failed or interrupted tasks retain failure/recovery evidence and cannot obtain a successful completion receipt. Documentation-only and unchanged iterations still assess their applicable scope and may produce an evidenced unchanged result.

## 1. Required closing sequence

| Step | Proposed action | Required result |
| --- | --- | --- |
| 1. Rebuild the graph | Invoke the existing graph build for affected owned repositories and the applicable workspace context against the completed task state. | A fresh generation or verified unchanged result, with source/configuration identity and extraction limits. Failure remains visible. |
| 2. Update CIS dependencies | Reassess source/manifests, component classification and dependency relationships. Reconcile applicable CIS-managed standards, instructions, skills, profiles, conformance mappings and workflow/check bindings using their declared source/version and existing ownership rules. | Applied authorized updates, verified unchanged items and explicit unresolved proposals/conflicts. Do not infer classification solely from a previously empty profile or its graph. |
| 3. Review alignment | Compare actual implementation and current classification with adopted requirements across all applicable standards. Inspect whether skills/instructions reach the affected components and whether required checks are configured and have current evidence. | Rule-level gaps and dispositions, distinguishing missing adoption, missing configuration, failed verification, stale evidence, inapplicability and confirmed alignment. |
| 4. Stabilize and verify | Resolve issues within the authorized task scope. If reconciliation changes source, guidance, profiles or canonical records, rebuild the affected graph and rerun impacted validation/assurance. | Evidence bound to the final state; earlier green checks cannot certify materially changed inputs. |
| 5. Assigned reviewer double-check | Have the existing task reviewer inspect the final source/configuration state, graph freshness, dependency reconciliation and alignment evidence. | Independent findings and their dispositions; the implementer's assertion that the steps ran is not sufficient. |
| 6. Record completion evidence | Attach the alignment result, reviewer result and finding dispositions to the existing task verification record. | All applicable gates pass against the final state, traceability is current and no completion-blocking finding remains unresolved. Human acceptance remains a separate existing authority. |

The completion instruction should say, in substance: **After finishing each task, rebuild the graph, reconcile CIS dependencies and review alignment. Apply authorized corrections, refresh affected evidence, have the assigned task reviewer double-check the result, then record the evidence before marking the task complete.** This is proposed instruction wording, not a command to implement these changes during this planning pass.

The [graph build command](../manual/cis_graph_build.md) already supports repository/workspace scope and a verified unchanged path. Invoke the check each iteration; use its content-aware reuse rather than force a full extraction with `--refresh` every time. Graph freshness does not itself prove correct classification, complete extraction or standards compliance.

If canonical reconciliation changes graph inputs, the initial graph is no longer the final graph. Refresh once those updates stabilize. Do not recurse indefinitely: use a bounded retry policy, retain each failure and report an unresolved alignment state if the process cannot converge.

## 2. Meaning of CIS dependency updates

The proposed scope is the repository's CIS governance dependencies and dependency knowledge: imported/default standards, instructions and skill packs, profiles, rule/conformance bindings, workflow definitions, and recorded project/workspace relationships. Include source identity and version where available so the result can explain what changed and why.

Synchronize against the repository's adopted CIS sources/version policy. Discover newer available definitions as candidates where relevant, but do not silently track an unpinned latest policy or change an adopted requirement. This task-closing sequence is not blanket authorization to upgrade application packages, runtimes, database schemas or CIS binaries.

Use existing init/import/reconciliation and ownership mechanisms where they fit. The [import contract](../manual/cis_repo_import.md) currently uses reviewed previews and merge identities for canonical changes; do not prescribe a blind `--yes` import as the iteration implementation. Routine updates covered by recorded user/repository authority should proceed within that authority, without requiring the same approval again. Conflicts, new policy choices or expanded implementation scope retain the existing decision process.

Refresh derived state automatically. For canonical content, preserve human-owned edits, stronger local policy, custom commands and supported alternative tools. Show the proposed delta and provenance when a merge or adoption decision is unresolved. Do not delete or overwrite custom guidance merely because a newer starter exists. Dependencies owned by another product remain bounded context, not targets for this iteration to modify.

## 3. Alignment covers all applicable standards

Reassess language, component role and capabilities from current source evidence. Cover code quality, architecture, testing, security, observability, API/data contracts, delivery, documentation and agent-facing CLI/skills where applicable. Use the common standards applicability mechanism rather than implement a separate selection algorithm per subject.

For each applicable obligation, distinguish:

1. A recommendation exists.
2. The repository has adopted a rule or a justified alternative.
3. Relevant instructions/skills and configuration are present and current.
4. The required check or review ran against the correct scope.
5. Its findings have valid dispositions and its evidence still matches the final state.

The existence of a Markdown file, matching phrase or valid profile is insufficient to establish all five. New implementation in a formerly empty repository must expose missing requirements even if no framework or component was previously recorded.

Review gaps for the current task and consequential newly introduced capabilities before completion. Surface unrelated legacy debt with its owner and follow-up disposition; do not silently claim repository-wide compliance or force every small task to repair unrelated historical code. A new policy choice can require a decision, but publishing a draft standard elsewhere must not silently turn it into an adopted blocking requirement.

## 4. Instructions, execution and evidence

### All applicable gates must pass

Determine the required gate inventory from the task's requirements, acceptance criteria, actual impact and adopted standards, then compare it with configured checks. Checking only the commands already registered would miss the original problem. A required gate with no implementation or evidence is missing, even if every configured command succeeds.

| Gate state | Meaning | Completion effect |
| --- | --- | --- |
| Passed | The required check/review succeeded with valid evidence for the final applicable scope. | Satisfies this gate only. |
| Failed | The check ran and reported an unmet requirement or execution error. | Blocks completion. |
| Missing | A required gate, configuration, result or evidence link is absent. | Blocks completion. |
| Stale | Evidence no longer matches relevant source, configuration, requirements or review state. | Blocks completion until refreshed or validity is re-established by the defined evidence policy. |
| Skipped | An applicable check was not executed, including an unavailable tool or dependency. | Blocks completion; it cannot silently become inapplicable. |
| Inapplicable | An explicit, evidenced applicability decision establishes that the gate is not required for this task. | Excluded from required gates with recorded rationale and authority where required; never reported as passed. |

Unknown or incomplete applicability cannot establish readiness. A deferred task is not a completed task. Accepted risk or a finding disposition cannot relabel failed, missing, stale or skipped evidence as passed. Any authorized scope/policy change must be explicit, preserve prior results, recompute the required gate inventory and receive current review; the agent cannot remove a gate to obtain a green result.

Reconcile requirements and acceptance criteria to task scope, implementation artifacts, test catalogue identities or other verification methods, execution evidence, reviewer findings and final dispositions. Use existing plan/test/verification records. A current plan with no matching implementation evidence, a missing required test mapping or an unresolved completion-blocking review finding prevents completion. Nonblocking observations remain visible with disposition rather than being silently discarded.

The assigned reviewer checks both that every required gate is present and that each applicable gate passed. Its recommendation is itself required evidence, not a substitute for the underlying gates. Persist this reconciliation as part of the same closing receipt; SC-13/14 do not need a separate parallel completion process.

For SC-17, consume the [business-acceptance results](testing-harness-defaults-plan.md#representative-workload-acceptance-belongs-to-business-tests) from the testing harness. Where requirements specify representative workload, resource/performance budgets, recovery or installed behavior, the reviewer checks that those conditions and the complete business outcome were actually verified. Passing smaller component or business scenarios cannot satisfy an unexecuted representative-acceptance gate.

### Instruction and evidence integration

The iteration instructions are the primary behavioral entry point. Align generated repository guidance and relevant delivery/completion skills with the same sequence so agents do not encounter competing checklists. Existing [delivery instructions](../../.github/instructions/cis-delivery-execution.instructions.md), [change instructions](../../.github/instructions/cis-change-delivery.instructions.md) and [completion skill](../../.github/skills/cis-validate-completion/SKILL.md) are candidate integration points, not a claim that a dedicated iteration instruction currently exists.

The implementation should also verify the result at the governed task-completion transition. Instructions tell an agent what to do; a receipt prevents an omitted or failed sequence from being presented as executed. Reuse existing task/verification evidence rather than create a competing completion ledger. The [final delivery sweep](final-delivery-sweep-task-type.md) checks task receipts and their freshness, and still performs its feature-wide reconciliation.

A receipt should identify task/iteration, affected repositories, final source/configuration identity, CIS/definition versions, graph generation/status, classification changes, dependency updates, applicable-rule assessment, commands/checks and results, unresolved findings and disposition authority. Verify artifact integrity where appropriate; an agent-written declaration of success alone is insufficient.

Bind freshness to the files and configurations actually assessed, including uncommitted changes where supported. Exclude the receipt itself and disposable output from its input identity to avoid self-invalidating evidence. Concurrent changes after assessment make affected evidence stale; do not overwrite another task's work or certify a mixed snapshot. A failed graph build or incomplete alignment run cannot count as a clean receipt.

Keep the process incremental: assess all applicable obligations, but rerun expensive checks only when their inputs, requirements or evidence validity demand it. Refresh disposable routing/index state only as needed; model-generated summaries remain advisory and must not determine adoption or completion. Provider authorization and data-handling policy remain unchanged.

### Context sufficiency and reuse

Graph freshness establishes that derived state matches its inputs; it does not establish that extraction captured every relevant dependency or that a generated summary is accurate. Treat source, canonical contracts and observed verification evidence as authoritative. Use graph/index summaries to locate that evidence, then inspect it before drawing material implementation or review conclusions.

The task context should identify changed behavior and relevant entry points, callers, called dependencies, contracts, tests and operational consequences. Include failure propagation, timeouts/cancellation, persistence and externally visible results where affected. Record the inspected scope, source identities and known omissions, unsupported analysis, unresolved relationships or truncated material. Do not equate a small diff or a graph query returning no caller with proof that no caller is affected.

When derived context is incomplete or contradicts source, expand inspection through targeted source search and direct reads, correct/invalidate misleading routing summaries as appropriate, and reassess affected conclusions. A rebuild alone may reproduce the same inaccurate summary. Missing context that matters to a required acceptance criterion leaves that review incomplete and blocks completion until evidence is obtained; unrelated omissions can remain explicit limitations. These checks concern sufficient context for the task, not an assertion that the entire repository has been exhaustively understood.

At the start of the next task, compare the reused context/receipt identity with current relevant inputs. If another task, branch change, external edit or canonical update has intervened, refresh affected context before relying on it for implementation. A verified unchanged snapshot can be reused without a forced full rebuild. This is a freshness precheck within the iteration lifecycle, not a second review process; closing validation cannot undo decisions already made from stale inputs.

### Assigned task reviewer

Reuse the reviewer already assigned to the task and the provider-neutral [independent logic-review workflow](code-quality-architecture-plan.md#73-independent-secondary-agent-logic-review). This double-check does not require another agent or a particular tool/provider. If required review is unavailable, report that state through the existing review policy; do not substitute the implementer's own statement.

Give the reviewer the final diff/source identity, graph status and diagnostics, context scope and omissions, relevant caller/contract/operational source, reconciliation delta including preserved customizations, applicable-rule assessment, executed-check evidence and unresolved findings. The reviewer can request additional relevant evidence and should verify that:

- Graph evidence covers the final task state, with missing extraction capabilities disclosed.
- Source inspection supports material context claims and covers relevant callers and operational consequences; a fresh graph or persuasive summary is not sufficient by itself.
- Classification and CIS dependencies reflect new components/capabilities, and updates preserve local ownership and adopted alternatives.
- Required standards, instructions, skills and checks are actually present or have explicit unresolved dispositions; file existence or a successful command alone is not alignment.
- Verification evidence matches the final state and no newly required check, failed update or material finding was silently skipped.
- Any exception, deferral or acceptance has the required authority; the reviewer does not invent it.

Review should inspect underlying source/configuration and artifacts, and repeat targeted read-only status or validation checks when necessary. It need not blindly rerun every expensive suite. A discrepancy becomes a task finding: correct it within authorized scope, refresh affected evidence and return the changed scope for review. Changes made after the review invalidate affected review evidence, including reconciliation updates that alter requirements or checks.

Record reviewer identity/run, reviewed state, checked scope, findings and dispositions alongside the alignment receipt. This confirms the reviewer performed the double-check; it does not make model judgment infallible or replace deterministic validation and final human authority.

## 5. Bounded implementation sequence

| Task | Depends on | Proposed deliverable and acceptance |
| --- | --- | --- |
| IA-01: closing contract | — | Specify the iteration sequence, CIS dependency scope, ownership/authority rules, gate inventory, evidence identities and passed/failed/missing/stale/skipped/inapplicable states. Define current requirement-to-task-to-implementation-to-verification-to-review traceability. |
| IA-02: shared reconciliation | IA-01; shared classification work in the testing plan | Compose graph refresh, classification/dependency reconciliation and all-standard applicability assessment through existing services. Cover an empty repository gaining source and an existing component gaining a new capability. |
| IA-03: instructions and skills | IA-01, IA-02 | Add the sequence to iteration guidance and align starter/delivery/completion skills. Validate generated and human-edited guidance without overwrites or duplicate workflows. |
| IA-04: completion evidence and reviewer check | IA-02, IA-03 | Require every applicable gate to pass and bind alignment, traceability, context sufficiency and assigned-reviewer results to completion transitions; final sweep consumes valid receipts. Reject omitted, failed, stale, partial and tampered evidence. Qualify missing gates despite green configured checks, broken test mappings, unresolved blocking findings, overwritten customization and skipped checks. |
| IA-05: qualification and rollout | IA-04 | Exercise unchanged/doc-only tasks, classification growth, custom standards, alternative tools, newly added checks, conflicting updates, failed builds, concurrent edits and bounded retry exhaustion. Add fresh-but-incomplete graph, misleading summary, omitted-caller, source fallback and changed-input-at-next-task fixtures. Confirm authorized routine updates and unresolved decisions are handled distinctly. |

Coordinate shared discovery and classification work with the [testing plan](testing-harness-defaults-plan.md), [quality plan](code-quality-architecture-plan.md) and [CLI/skills plan](agent-interface-defaults-plan.md). Their standard-specific requirements feed one iteration alignment and completion process. This proposal does not duplicate those implementations or mark SC-12 through SC-16 resolved.

Validation for this planning document consists of source inspection, documentation validation and link checks. No repository graph refresh, canonical dependency update, application test or runtime completion enforcement was performed as part of drafting it.
