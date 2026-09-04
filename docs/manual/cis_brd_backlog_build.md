---
title: "cis brd backlog build"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-backlog-build
---

# `cis brd backlog build`

```text
cis brd backlog build [--workspace <path>] [--format <human|json|agent>]
```

Requires Active/current BRD, technical intent, overall solution design, and component sheet.
Creates or reconciles the catalogued
`plans/high-level-backlog.md`, with one stable high-level outcome per functional BRD
requirement and global quality/success obligations. Reruns preserve dependencies,
feature-specification links, and notes for stable requirement IDs. BRD and technical-intent
baselines use semantic digests, so managed evidence or baseline bookkeeping does not make an
approved backlog stale. The command migrates legacy full-file baselines without reapproval
when the derived backlog content is unchanged; material upstream or backlog changes still
return it to review.

Functional and non-functional requirements may use either the canonical table form with
`BRD-FR-*` / `BRD-NFR-*` identities or the agent-authored narrative-bullet form with
`BR-FR-*` / `BR-NFR-*` identities. Narrative bullets use the bold requirement label as the
readable high-level outcome and retain the complete, including line-wrapped, statement as its
acceptance intent. Both identity families normalize to stable `HLT-FR-*` backlog identities;
CIS does not require a BRD rewrite or renewed BRD approval solely to change presentation form.

In a multi-repository workspace, implementation routing targets registered participant
repositories. When a greenfield workspace contains only its authority repository, CIS treats
that repository as the implementation target so generated items never have an empty repository
scope. Adding participant repositories later returns routing to the multi-repository rule on the
next idempotent build.
