---
title: "Why a Passing Workflow Cannot Approve Completion"
type: article
status: Draft
series: "Verification and Assurance"
series_order: 7
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on workflow or acceptance change
summary: "A zero exit code proves one execution contract succeeded; it does not establish complete scope or acceptable risk."
cis:
  stable_id: change-impact-studio:article:passing-workflow-not-completion
---

# Why a passing workflow cannot approve completion

A workflow can prove that its declared steps ran and returned the expected exit states.
It cannot prove that the workflow contained every required check.

## Execution and authority differ

Builds, tests, linters, and package smoke checks produce strong deterministic evidence.
Their definitions may still omit a contract, repository, migration, or manual risk review.
A zero exit code without the declared structured result or artifact is invalid evidence,
not a weaker form of success.

## The plan owns the obligation

The approved task identifies which workflow and additional artifacts are required. A
successful workflow satisfies that evidence row; it does not close missing scope findings
or accept residual risk.

## Failed and changed definitions matter

Resume only reuses successful steps against the same workflow digest. Changing the
definition changes the evidence contract and requires a new run.

## A workflow proves its own contract

If restore, build, unit tests, and packaging all pass, the workflow has strong evidence
for those exact steps, arguments, inputs, and result semantics. It says nothing directly
about a missing browser suite, an undocumented consumer, a required human design review,
or an accepted impact that never became a task.

This is not a weakness in automation. It is the natural boundary of every test: a system
can prove the contract it was given, not that no necessary contract was omitted.

## Green can hide invalid evidence

A child process can return zero while failing to produce the declared test result. A
wrapper can swallow a failure. A parser can reject the artifact. A cached result can
belong to another workflow digest. CIS distinguishes process success from evidence
validity so these cases do not appear as a pass.

The workflow should retain exact step state, bounded output, artifacts, and any truncation.
A dashboard color is a projection of that evidence, not the evidence itself.

## The approved plan selects the workflow

The canonical task identifies which workflow, suites, manual reviews, contracts, and
artifacts are necessary for this change. A low-risk documentation edit and a public
authorization change should not inherit the same evidence merely because both use the
default pipeline.

Plan validation confirms that accepted impact has evidence obligations. The workflow
then supplies some of those obligations; verification integrates the complete set.

## Definition changes require new evidence

Adding a missing browser step after a run does not make the old green run proof that the
browser behavior passed. The changed workflow digest creates a new contract and requires
execution of the affected steps. Resume can reuse only unchanged successful work under
the defined dependency rules.

This history matters during incidents. Reviewers can see which contract actually ran
rather than interpreting every historical green check through today's pipeline.

## Acceptance owns residual risk

Even a complete valid evidence set may leave risk: a supported platform was unavailable,
a manual test has limited coverage, or a migration rollback was demonstrated only in a
representative environment. A human authority decides whether that risk is acceptable and
records rationale.

Letting a successful workflow close the task would give the workflow definition power to
approve its own omissions.

## Consider a fully green but incomplete run

An API workflow restores, builds, runs unit tests, and packages successfully. The approved
change also requires comparison with two supported OpenAPI baselines, a permission
inventory update, a browser error-state check, and human disposition of a security
finding. The workflow is genuinely green and the change is genuinely not ready for
acceptance.

Adding every possible check to one universal pipeline is not the answer. The plan selects
the evidence appropriate to the change, and verification assembles results from focused
workflows, tools, artifacts, and manual reviews. Each source keeps its own validity and
authority boundary.

## Make dashboards phrase claims precisely

Prefer “workflow succeeded” or “required automated checks passed” to “change complete.”
Interface language shapes operating behavior. A button that closes work on green status
can collapse the authority boundary even when the underlying records remain separate.

## Takeaway

Treat workflow success as high-quality evidence with a precise boundary. Completion
requires the whole approved verification contract and human acceptance.

## Canonical CIS sources

- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Standard delivery workflow](../workflows/standard-delivery.md)
- [`cis workflow status`](../manual/cis_workflow_status.md)
