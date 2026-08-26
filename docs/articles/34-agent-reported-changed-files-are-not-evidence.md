---
title: "Why Agent-Reported Changed Files Are Not Evidence"
type: article
status: Draft
series: "Verification and Assurance"
series_order: 1
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on verification change
summary: "An executor's changed-file list is useful narration, but Git provides the independent repository evidence."
cis:
  stable_id: change-impact-studio:article:agent-changed-files-not-evidence
---

# Why agent-reported changed files are not evidence

Most coding agents end with a confident summary of files changed and tests run. The
summary is useful. It is not independent evidence.

## The executor has a partial view

An agent may omit generated files, concurrent edits, renamed paths, tool-created output,
or changes outside its remembered task. It can also report an intended change that was
never written successfully.

## Git observes the workspace

CIS compares actual Git state with the approved baseline and planned targets. It asks
which repositories and paths changed, not which ones the executor recalls changing.

The result can then be compared with the agent report. Agreement increases confidence;
disagreement becomes a visible verification finding.

## Narration still matters

The executor can explain why a path changed, which command produced it, and what risk
remains. That explanation helps review but does not replace the observed diff.

## Takeaway

Treat agent summaries as supporting testimony. Use Git, exact commands, and artifacts
for independent evidence of what actually happened.

## Canonical CIS sources

- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [`cis verify compare`](../manual/cis_verify_compare.md)

