---
title: CIS-0003 verification
type: verification-record
status: Draft
change_id: CIS-0003
---

# Verification

The first checkpoint below records the checks accepted at `33f345f6`. The focused
amendment follow-up is recorded at the end; it reuses compatible earlier checks.

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-001 | Planning and compatibility | `dotnet test tests/Cis.Modules.Delivery.Tests/Cis.Modules.Delivery.Tests.csproj` (affected filters on recheck) | Passed | 376 current cases; reuse unchanged passing cases from the initial project run. One existing symlink fixture needs Windows symlink support and was skipped. |
| WORK-001 | Accounting and native admission | `dotnet test tests/Cis.Modules.Agent.Tests/Cis.Modules.Agent.Tests.csproj --filter 'FullyQualifiedName~Outcome\|FullyQualifiedName~AgentWorkLedgerTests\|FullyQualifiedName~GovernedUnit'` | Passed | 62 cases, with the final eight outcome cases rechecked after contract binding changes. |
| WORK-001 | Document and skill governance | `cis docs validate --strict`; `cis skills validate --strict`; `cis skills audit --no-llm` | Passed | No errors, warnings, deterministic duplicates or conflicts. |
| WORK-001 | Independent source review | Separate Codex review of source, callers and retained contracts | Passed | Required findings resolved, including migration, readiness, context and task binding. |

Ordinary native TRX results remain under `.artifacts/outcome-workflow-correction/` locally. These checks use temporary neutral fixtures; no CISLIB consumer execution or live-state alteration occurred.

One human authorization carries the outcome, scope and total limits. Supporting tasks, a split, assignment, capacity distribution, reload, review and completion use controller derivation. The native scenario launches two representative unit processes and preserves the original report with no launches on ordinary reuse. The metadata scenario creates 100 canonical tasks, records one authorization, and rejects aggregate capacity above its original total.

The earlier CISLIB Logging example at `47fef095f10587d9b0b563ec58f9654bf12f274d` needed impact, plan and allocation decisions. Its human-facing records were nine Markdown files plus one CSV (39,753 bytes), with one machine event file (1,112 bytes). The one-task neutral example now uses one decision, four Markdown files (3,699 bytes) and four separate machine files (4,729 bytes). Controller calls are operations under the grant, not additional human decisions.

The correction record is ReadyForReview. The actual repair was bootstrapped with external build/test commands; it does not claim those commands as settled CIS-native executions or invent consumption/attempt history. The finite allocation retains the authorized historical-unknown acknowledgment. The fixture accounting assertions are measured native evidence.

The handoff is the Release-configuration CIS host built from this commit, on the development branch. Main merge, release publication and subsequent CISLIB product work remain outside this correction.

## Amendment follow-up — 2026-11-15

Completed and otherwise terminal tasks resolve their retained contracts against real
authorization history. Affected unfinished work can be cancelled, deferred or split
into current-scope replacement children without changing its original accounting
owner. Historical validation grants no new execution: current scope and UI applicability
still govern admission and completion. Imported completed work keeps its source-bound
catalogue, while an inactive legacy template creates no new obligation.

Participant completion and resume preserve the original snapshots and approval
provenance. An unrelated authorized scope addition can change shared views, including
large views, while the selected material authority stays current. Selected changes,
altered approval lineage, unsafe paths and snapshot tampering still hold execution.

| Check | Result | Actual scope |
| --- | --- | --- |
| Delivery regressions | Passed | 40 distinct cases across the focused outcome/planning run and final 24-case lifecycle, linked-task gate, completion and catalogue recheck. Compatible passing cases from the first run are reused. |
| Agent regressions | Passed | 67 participant/outcome cases: completion/resume, material-change holds, approval lineage, snapshot integrity/limits, withdrawn UI scope and composed native accounting. |
| Native continuity | Passed | The composed fixture still launches exactly two representative unit processes. Amendment, historical disposition and current replacement preserve the original report, ownership, consumed work and attempt counts; planning does not alter the ledger. |
| Independent review | Passed | Separate source/caller review found no remaining required issue. A context-limited reader understood the three updated manuals; both minor wording ambiguities were corrected. |
| Deterministic governance | Passed | Strict documentation and suite-profile validation; current graph rebuild and diff capture. Unchanged skill checks remain valid from the first checkpoint. |

The first participant run exposed an incorrect fixture expectation: provider resume
creates a second provider attempt within the same logical attempt. The corrected
assertions require both provider calls and the retained single logical attempt.
The imported-UI case exposed old source applicability leaking into current supporting
work; the final recheck passes after correcting that path. Ordinary TRX files, including
the diagnostic outcomes, remain under `.artifacts/outcome-workflow-correction/amendment/`.

This focused pass is complete. The aggregate dossier remains ReadyForReview: full
`verify validate` still requires a verification-ready task disposition and current,
matching security evidence; its retained older security manifest is stale. Those
broader closing gates were inspected, not represented as passed or bypassed. No final
acceptance, main merge or release is claimed. The original authorization, allocation
and honest external-bootstrap accounting limitation remain unchanged.

CISLIB was inspected read-only at `b9a66340`: its checkout is clean, its existing
completed work remains unchanged and its current plan queues no successor. No CISLIB
consumer test or live-state update ran in this pass.

## Request-to-review follow-up — 2026-11-15

WORK-002 uses shared proposal AC-1–AC-6, recorded before implementation. The current
request explicitly adds the named applied instructions to scope; native authorization
records that path addition on the same grant while preserving the original workflow
scope and history. Controller redistribution keeps the same 14,400 seconds, 12 attempts,
600 protected restoration seconds and storage ceiling: each owner has 7,200 seconds
and six attempts, with the original 600-second reserve retained on WORK-001. Consumption,
attempts and reservations are preserved. External bootstrap checks retain the earlier
historical-unknown limitation; no unmeasured work is represented as settled native cost.

The promised-workflow rule is now canonical in `cis-validate-completion`. Its runtime
excerpt reaches direct workers, engineering reviewers and story stages through the
shared prompt builder. Participant context also carries the actual skill and applied
instruction snapshots. It asks for acceptance-derived checks of the user's operation,
preserved state and next operation, including relevant writers/readers/validators and
supported denial, resolution and continuation. One existing independent review handles
required corrections; optional advice and correction ceilings create no extra stages.

### Why the earlier reviewed handoff missed the defects

The amendment handoff was declared ReadyForReview, without formal dossier acceptance.
Its ordinary scope-amendment scenario needed completed history, disposition of unfinished
work and a current replacement. The earlier regression checked rejection and unrelated
eligibility but stopped before resolving affected work. `OutcomeAmendmentLifecycleTests`
and the composed native accounting case now follow that continuation and preserve history.

The participant scenario needed successful completion and interrupted-run resume after
an unrelated amendment. The native result-reuse fixture exercised another execution
route and did not establish the participant snapshot checks. `ParticipantOutcomeAuthorityTests`
now exercise both actual participant paths, relevant writers/readers and retained approval
evidence. The canonical review rule explicitly asks which alternate consumers the chosen
fixture leaves unverified.

The current request also exposes the seed/context gaps left by that handoff. Its checked
generic seed could stand in for observable acceptance because authorization and field
presence were treated as readiness. The new seed regressions exercise draft, hold,
controller-supplied criteria and normal continuation through the actual services. The
wording review covered central guidance and manuals while obsolete auto-applied clauses
still reached agents. The participant context regression now binds the actual five
guidance sources; shared story and engineering prompt callers are checked as well.
Issues caught during this pass's internal review, including deferral and split-container
edges, are ordinary internal corrections rather than additional escaped handoffs.

| Follow-up check | Result | Actual paths and scope |
| --- | --- | --- |
| AC-1–AC-3: concrete readiness and preservation | Passed | `OutcomeSeed_` service/controller cases cover draft before/after authorization, direct/native/story denial, concrete update and supported lifecycle, deferral, split completion, and old started/approved/completed contracts. Original authorization, reports and accounting are retained. |
| AC-4–AC-5: supplied context and review | Passed | `RequestWorkflow_` binds and delivers the actual five guidance sources in both participant modes. Existing story and engineering-review cases inspect their real provider requests. The linked-story modular path also checks readiness without an optional Plan reader. This establishes context delivery and service readiness, not model compliance or a new product qualification. |
| Affected Agent checks | Passed | 74 distinct cases across the focused outcome/request-context/story/engineering run and targeted rechecks; compatible unaffected passing results are reused. The final seed variants pass through ReadyForReview → InProgress → Complete, following the existing lifecycle. |
| Affected Delivery checks | Passed | 36 outcome, linked-task, legacy completion and approved-refresh cases. The retained amendment/native and participant regressions remain valid; unrelated earlier qualification results are reused. |
| Guidance and independent review | Passed | 535 documents and 53 skills pass strict validation; deterministic no-LLM audit reports no duplicate, overlap or conflict. One independent source review has no required finding; a separate reader check resolved two authority-wording ambiguities. |

The new fixture checks first assumed that inspection returned validation, omitted the
workspace registry needed for real completion, and attempted an unsupported direct
ReadyForReview → Complete transition. The fixtures now use the actual validation API,
normal service composition and supported transition sequence; product completion gates
were preserved. Ordinary first outcomes and rechecks remain in the existing local
`request-workflow/` artifact directory. No broad mutation, performance or model campaign ran.
Routing-card refresh was unavailable because no local text provider was selectable;
actual source inspection supplied review context, with no remote fallback.

WORK-002 is ReadyForReview at the requested handoff. WORK-001's prior ReadyForReview
checkpoint and stale/mismatched security evidence remain the separate CIS-0003 closeout.
An aggregate final verification also sees WORK-002's new review checkpoint; neither
checkpoint is represented as formal completion or acceptance. No allowance was added,
and no CISLIB state or consumer result changed.
