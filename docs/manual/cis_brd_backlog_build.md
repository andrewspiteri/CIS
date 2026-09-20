---
title: "cis brd backlog build"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-15"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-backlog-build
---

# `cis brd backlog build`

```text
cis brd backlog build [--mode <requirements|no-planned-work>] [--actor <human>] [--workspace <path>] [--format <human|json|agent>]
```

Requires Active/current BRD, technical intent, overall solution design, component sheet and UI direction.
Creates or reconciles the catalogued
`plans/high-level-backlog.md`, with one stable high-level outcome per functional BRD
requirement and global quality/success obligations. Reruns preserve dependencies,
feature-specification links, and notes for stable requirement IDs. BRD and technical-intent
baselines use semantic digests, so managed evidence or baseline bookkeeping does not make an
approved backlog stale. The command migrates legacy full-file baselines without reapproval
when the derived backlog content is unchanged; material upstream or backlog changes still
return it to review.

For an existing product with no new delivery work planned, use `--mode no-planned-work --actor <human>`.
This records an empty delivery scope with named provenance and the current BRD/technical baseline.
It does not assert that requirements are implemented, verified or defect-free. Global obligations
remain visible, and the record still requires normal review and approval. Existing backlog items
or feature links cannot be discarded through this action.

Omitting `--mode` preserves a recorded no-planned-work choice. If its source baseline changes,
the choice must be reviewed and explicitly recorded again; a generic rebuild does not silently
renew it. `--mode requirements` explicitly creates candidate outcomes when work needs planning.
Transitions from a no-work record preserve human notes outside managed blocks. Neither mode
approves delivery or starts implementation.

The wizard's Delivery map offers **Create candidate backlog** and **Record no planned work**.
Both keep the current step open and display the result without a second workspace load. The
no-work action asks the user to confirm the named decision, then leaves final approval to
Review and activate. The wizard can prepare either draft mode from its valid/current upstream
drafts; independent commands retain their normal Active-source requirements.

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
