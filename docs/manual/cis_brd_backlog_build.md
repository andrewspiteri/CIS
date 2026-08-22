---
title: "cis brd backlog build"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-16"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-backlog-build
---

# `cis brd backlog build`

```text
cis brd backlog build [--workspace <path>] [--format <human|json|agent>]
```

Requires Active/current BRD and technical intent. Creates or reconciles the catalogued
`plans/high-level-backlog.md`, with one stable high-level outcome per functional BRD
requirement and global quality/success obligations. Reruns preserve dependencies,
feature-specification links, and notes for stable requirement IDs. BRD and technical-intent
baselines use semantic digests, so managed evidence or baseline bookkeeping does not make an
approved backlog stale. The command migrates legacy full-file baselines without reapproval
when the derived backlog content is unchanged; material upstream or backlog changes still
return it to review.
