---
title: "Initializing a Newly Created Repository"
type: article
status: Draft
series: "CIS in Practice"
series_order: 1
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on repository-initialization change
summary: "Establish governed intent, documentation, standards, and context before a new codebase accumulates undocumented decisions."
cis:
  stable_id: change-impact-studio:article:initialize-new-repository
---

# Initializing a newly created repository

A new repository has little source evidence and a large decision surface. That makes it
the best time to establish product intent, technical boundaries, and delivery policy.

## Begin with Git and a dry run

```powershell
git init
cis repo init --root docs\cis --dry-run --format agent
```

The dry run creates no files. It shows the baseline documentation, standards, skills,
instructions, templates, and configuration CIS would add.

## Apply the reviewed baseline

```powershell
cis repo init --root docs\cis --yes
cis docs validate --strict
cis skills validate --strict
cis standards validate --strict
```

The starter documents remain Draft until their meaning is completed and reviewed. Empty
templates are not product authority merely because initialization created them.

## Add source and reconcile

As projects appear, rerun initialization. Classification selects technology-specific
contracts, references, implementation skills, and standards. Human edits are preserved
through starter ownership and collision rules.

## Build the first graph

```powershell
cis graph build
cis graph validate --strict
```

An unborn or early repository can use an exact graph build as a change baseline before
it has a meaningful commit history.

## Takeaway

Use a new repository's low implementation cost to make intent and authority explicit.
Initialize safely, complete the Draft knowledge, and reconcile as the codebase develops.

## Canonical CIS sources

- [`cis repo init`](../manual/cis_repo_init.md)
- [CLI and repository initialization](../specs/cli-and-repository-initialisation-spec.md)
- [Classification-driven initialization](../specs/classification-driven-initialisation-spec.md)

