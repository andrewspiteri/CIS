---
title: "Why High-Complexity Tasks Must Be Decomposed"
type: article
status: Active
series: "Change Impact and Planning"
series_order: 5
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on task-planning change
summary: "Complexity should constrain execution by creating bounded child tasks, not merely label a large instruction."
cis:
  stable_id: change-impact-studio:article:decompose-high-complexity-tasks
---

# Why high-complexity tasks must be decomposed

A task labelled “high complexity” remains dangerous if it is still handed to one
executor as a single instruction.

The label acknowledges risk without changing the execution boundary.

## Complexity combines several dimensions

Work becomes complex through more than estimated effort. Signals include multiple
repositories, public contracts, security, data migration, cross-surface behavior,
provider handoffs, operational rollout, and unresolved dependencies.

These signals increase the number of decisions and failure modes an executor must hold
at once.

## Parents express outcomes; children express execution

CIS does not allow a high-complexity work item to remain directly executable. It becomes
a decomposed parent with at least two low- or medium-complexity children.

The parent retains:

- the outcome;
- accepted impact and requirement coverage;
- shared constraints; and
- completion relationship.

Children receive bounded objectives, targets, dependencies, validation, and evidence.

## Decomposition should follow risk boundaries

Useful boundaries include contracts before consumers, schema before backfill, wireframes
before visual design, design before implementation, and implementation before independent
verification.

Splitting one broad task into arbitrary file groups does not reduce conceptual risk.
Each child should be independently understandable and verifiable.

## Coverage must survive decomposition

Requirements and accepted impacts cannot disappear when work is split. Parent and child
records preserve traceability so validation can show which executable task covers each
obligation.

## Complexity is a coupling signal

Complexity rises when a task must coordinate several kinds of authority at once. A useful
assessment looks beyond story points:

| Signal | Why it increases risk |
|---|---|
| Multiple repositories | Baselines, owners, and release paths can diverge |
| Public contract change | Compatibility and consumer coordination become material |
| Security or permission boundary | Incorrect behavior can expose data or capability |
| Data migration | Forward, rollback, and mixed-version states must agree |
| Several user surfaces | Behavior and evidence differ by audience |
| Operational rollout | Runtime failure and recovery become part of the outcome |
| Unresolved decision | The executor would otherwise choose product direction |

The exact scoring can vary. The control principle is stable: once the amount of coupled
judgment exceeds one safe execution boundary, the work must change shape.

## Decompose an authentication feature

“Add social sign-in” might become a high-complexity parent covering the outcome and all
accepted impact. Its children could be:

1. approve identity ownership, provider, and account-linking decisions;
2. define redirect, callback, error, and session contracts;
3. implement backend protocol and permission behavior;
4. implement the public sign-in entry and customer account states;
5. configure secrets, telemetry, and provider-outage behavior;
6. run security, contract, browser, and integration verification; and
7. coordinate rollout and final acceptance.

Each child has a smaller authority set, clear dependencies, and evidence that can fail
for the right reason. The parent completes only when the children collectively cover the
outcome and accepted findings.

## Good children are independently reviewable

A bounded child should have one primary objective, named repository targets, explicit
inputs, non-goals, acceptance criteria, and validation. It should be possible to explain
why the child is complete without relying on another child's private reasoning.

This does not mean every child can ship alone. Contract work may intentionally precede
consumers, and a schema change may require a coordinated deployment. Independence here
means that authority, execution, and evidence are understandable at the child boundary.

## Avoid decomposition theatre

Weak decomposition creates tasks such as “edit backend files,” “edit frontend files,” and
“run tests.” These are activity buckets, not engineering boundaries. They leave contract,
security, failure behavior, and ownership choices implicit.

Another anti-pattern creates dozens of tiny tasks that share the same context and cannot
be verified separately. Coordination cost rises while risk remains coupled. The objective
is not the maximum number of tasks; it is the smallest set of safe, coherent execution
contracts.

## Coordinate without restoring one giant task

The parent preserves cross-child requirements and accepted impact. Dependencies express
order. Shared decisions and contract artifacts become explicit inputs. Verification can
then inspect both child evidence and the integrated outcome.

If one child discovers new impact, only the affected scope needs to return to review. The
remaining children do not acquire permission to absorb it merely because they share a
parent.

## Takeaway

Complexity classification should change what may execute. Use a high-complexity parent
to preserve the outcome, then create bounded children around contracts, risk, ownership,
and validation boundaries.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Task-type contract](../specs/task-type-contract-spec.md)
- [Core task-type catalogue](../specs/core-task-type-catalog.md)
