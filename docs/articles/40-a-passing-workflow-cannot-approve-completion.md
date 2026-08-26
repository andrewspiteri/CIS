---
title: "Why a Passing Workflow Cannot Approve Completion"
type: article
status: Draft
series: "Verification and Assurance"
series_order: 7
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
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

## The plan owns the obligation

The approved task identifies which workflow and additional artifacts are required. A
successful workflow satisfies that evidence row; it does not close missing scope findings
or accept residual risk.

## Failed and changed definitions matter

Resume only reuses successful steps against the same workflow digest. Changing the
definition changes the evidence contract and requires a new run.

## Takeaway

Treat workflow success as high-quality evidence with a precise boundary. Completion
requires the whole approved verification contract and human acceptance.

## Canonical CIS sources

- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Standard delivery workflow](../workflows/standard-delivery.md)
- [`cis workflow status`](../manual/cis_workflow_status.md)

