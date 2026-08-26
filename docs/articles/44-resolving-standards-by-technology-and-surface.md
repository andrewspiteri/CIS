---
title: "Resolving Standards by Technology and Change Surface"
type: article
status: Draft
series: "Standards and Engineering Policy"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on standards-applicability change
summary: "Apply the standards relevant to repository classification and affected targets instead of presenting one universal checklist."
cis:
  stable_id: change-impact-studio:article:standards-by-technology-surface
---

# Resolving standards by technology and change surface

A frontend interaction standard is irrelevant to a Terraform-only repository. A
persistence boundary standard should appear when a change affects database behavior,
not in every documentation edit.

## Declare targets and stacks

Standards identify targets such as backend, frontend, API, persistence, accessibility,
operations, or repository governance and stacks such as C#, TypeScript, or generic.

## Initialize from evidence

CIS classification selects a conservative standards baseline. Every repository receives
agent-documentation governance; application, API, persistence, eventing, frontend,
accessibility, observability, and edge standards are added when evidence supports them.

## Resolve again for each change

Repository classification says what can apply. The affected task surface says what does
apply to the current work. Plans should cite the resolved rules before implementation.

## Do not silently remove authority

If later classification no longer selects a human-reviewed standard, initialization
retains it. Removal or retirement requires governance, not absence from a new scan.

## Takeaway

Route standards through repository evidence and affected surfaces. Make applicability
specific enough to guide work without hiding reviewed rules when classification changes.

## Canonical CIS sources

- [Standards governance](../specs/standards-governance-spec.md)
- [Classification-driven initialization](../specs/classification-driven-initialisation-spec.md)
- [`cis standards applicable`](../manual/cis_standards_applicable.md)

