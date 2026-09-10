---
title: "Why Agent-Reported Changed Files Are Not Evidence"
type: article
status: Active
series: "Verification and Assurance"
series_order: 1
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
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

Direct provider execution strengthens the claim without changing its status. CIS records
the target repository, isolation, revision or private snapshot, normalized event stream,
changed-path containment, and result digest. A successful provider process with missing,
stale, malformed, or out-of-target evidence becomes `InvalidEvidence`, not accepted work.

## Narration still matters

The executor can explain why a path changed, which command produced it, and what risk
remains. That explanation helps review but does not replace the observed diff.

## A report describes the executor's belief

The final message usually comes from the same process that selected edits, interpreted
errors, and decided when to stop. It may be accurate and conscientious, but it is not
independent of the work being assessed.

This is true for humans as well. A pull-request description written from memory can omit
a generated lock file, a rename, or a configuration change. The stronger control is to
compare every executor's account with the repository.

## Several differences can disappear from narration

- a formatter changed files outside the intended component;
- a generator updated an artifact the executor did not inspect;
- a file was deleted or renamed rather than edited;
- an earlier local change was present in the working tree;
- a command failed after modifying output;
- an isolated worktree contains changes that were never imported; or
- the report names a planned file that remained unchanged.

None requires deception. They arise because a natural-language summary compresses a
larger state and because the executor may not observe every tool side effect.

## Establish the comparison boundary

Git evidence is useful only when the starting point is clear. Verification needs the
approved commit or captured working-tree baseline, the target repositories, and the
candidate end state. In a multi-repository change, each owned participant has its own
comparison; an unchanged repository should not disappear merely because another contains
the visible feature.

The observed diff should include status, renames, deletions, and untracked paths within
scope. Generated, ignored, and local artifact paths follow explicit policy rather than
being omitted ad hoc.

## Reconcile report and observation

The agent result remains valuable when treated as a claim:

| Observation | Review question |
|---|---|
| Git and report agree | Do the changes satisfy the contract and evidence obligations? |
| Git contains an unreported path | Was it a tool side effect, baseline issue, or scope expansion? |
| Report names a path Git does not show | Was the edit lost, reverted, or only intended? |
| Agent check differs from retained result | Which exact command and artifact establish the outcome? |
| Changed path escapes the target | Did containment fail, or is the evidence malformed? |

The objective is not to penalize disagreement automatically. It is to prevent disagreement
from being hidden by a confident summary.

## Isolation improves attribution

An isolated worktree seeded from an exact baseline reduces interference from the user's
checkout. It lets CIS attribute candidate changes to one run more reliably. The private
snapshot, run identity, and target containment still need validation; isolation does not
prove semantic correctness or completion.

## Evidence extends beyond paths

Git proves repository difference, not behavior. The verification record also needs the
tests, contract comparisons, security results, documentation validation, manual review,
and operational evidence required by the task. The agent can point to those results, but
the assurer checks their exact identity and validity.

## Review the claim systematically

Ask which baseline the report assumes, whether every target repository was observed,
whether untracked and renamed paths were included, which commands created secondary
artifacts, and whether the reported checks have retained results. Then compare the claimed
non-goals with actual untouched and changed paths. This turns the final narrative into a
useful orientation layer over independent evidence.

## Takeaway

Treat agent summaries as supporting testimony. Use Git, exact commands, and artifacts
for independent evidence of what actually happened.

## Canonical CIS sources

- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [`cis verify compare`](../manual/cis_verify_compare.md)
- [Provider-neutral agent execution](../specs/features/agent-execution-coordination-feature.md)
