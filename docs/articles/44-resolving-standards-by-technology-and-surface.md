---
title: "Resolving Standards by Technology and Change Surface"
type: article
status: Active
series: "Standards and Engineering Policy"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
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
accessibility, observability, secure-feature, and edge standards are added when evidence
supports them.

## Resolve again for each change

Repository classification says what can apply. The affected task surface says what does
apply to the current work. Plans should cite the resolved rules before implementation.

## Do not silently remove authority

If later classification no longer selects a human-reviewed standard, initialization
retains it. Removal or retirement requires governance, not absence from a new scan.

## Resolve in two stages

Repository classification establishes the available standards baseline. Change planning
then narrows that baseline through accepted impact, task type, target repository, stack,
and surface. The two stages answer different questions:

1. Which standards belong to this repository's governed environment?
2. Which rules constrain this particular work?

A standard absent from repository governance cannot be conjured by a task keyword. An
Active applicable standard should not disappear because the executor forgot to mention it.

## Follow an example

A TypeScript customer application and C# API belong to one product. A change adds a
public product-discovery endpoint and a public screen. Classification makes frontend,
accessibility, API, application, secure-feature, testing, and observability standards
available across their appropriate repositories.

Accepted impact then resolves public experience rules for the frontend task, API and
cache-isolation rules for the backend task, accessibility evidence for design and browser
work, and shared assurance rules for verification. A Terraform standard remains irrelevant
unless infrastructure impact is accepted.

## Use positive evidence

Applicability should come from repository manifests, components, governed inventories,
feature surfaces, accepted impact, and explicit task targets. A sentence saying “no
database change” must not activate persistence standards through a word match. Likewise,
an imported dependency's technology does not become the product's implementation stack.

When evidence is ambiguous, report the candidate and ask for review. Guessing a narrower
set can hide risk; applying every standard can bury the few rules that matter.

## Preserve rule-level results

Resolving a document is only the start. Individual rules may have narrower conditions,
different evaluators, or valid scoped exceptions. The task should cite stable rule IDs so
the executor and assurer can see exactly which obligations apply.

This also supports evolution. A standard can add a new rule without invalidating evidence
for unrelated rules, while current work receives the updated applicable set when its
baseline changes.

## Classification drift does not revoke review

If a framework marker disappears or an extractor changes, previously reviewed standards
remain canonical until an authorized lifecycle change removes them. The new scan is
evidence for reconciliation, not permission to erase policy.

The opposite is also true: new technology evidence can propose additional standards, but
generated Draft files do not become Active without review.

## Make the result explainable

An applicability report should show the target, stack, classification evidence, matched
surface, rule IDs, lifecycle, exceptions, and omitted candidates. The executor needs a
bounded obligation set; the reviewer needs to understand how it was derived.

## Review the resolved set before execution

Ask whether every target repository is represented, whether dependency technology leaked
into product direction, whether negative requirements activated false work, whether
Active human-owned standards were retained, and whether each selected rule has an
enforcement route or visible gap. Store the result with the task so the executor and
assurer use the same obligation set.

## Takeaway

Route standards through repository evidence and affected surfaces. Make applicability
specific enough to guide work without hiding reviewed rules when classification changes.

## Canonical CIS sources

- [Standards governance](../specs/standards-governance-spec.md)
- [Classification-driven initialization](../specs/classification-driven-initialisation-spec.md)
- [`cis standards applicable`](../manual/cis_standards_applicable.md)
