---
title: "What Changed in CIS and Why"
type: article
status: Draft
series: "Product Evolution and Learning"
series_order: 8
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on product release
summary: "A repeatable product-evolution format connecting evidence, intent, decisions, implementation, verification, and future work."
cis:
  stable_id: change-impact-studio:article:what-changed-and-why
---

# What changed in CIS and why

Product updates are most useful when they connect implementation to the evidence and
decisions that produced it.

## The problem observed

Describe the golden-path failure, user friction, governance gap, or maintenance cost.
Link bounded evidence rather than relying on a general impression.

## The intended outcome

State what should become observably better and what remains out of scope.

## The decision

Summarize the options considered, selected direction, authority, and rationale.

## The product change

Explain affected commands, schemas, documentation, providers, clients, or workflows at
the user boundary rather than listing internal files.

For the current CIS evolution, that boundary includes explicit ecosystem and product
onboarding, the eight-page product-definition journey and activation baseline, governed
reference sources, direct but isolated agent execution, CI and assurance evidence, and a
VS Code client that remains subordinate to CLI and canonical repository authority.

## The verification

Report focused checks, wider suites, packaged behavior, golden-path replay, unavailable
evidence, and residual risk.

## The learning

Identify what future context, planning, validation, or release behavior improved and
which questions remain open.

## A reusable outline

```text
Evidence
  → intent
  → decision
  → change
  → verification
  → learning
```

## Use evidence as the opening, not the appendix

Begin with a concrete observation: an existing repository could not establish product
authority atomically; dependency source leaked into product inference; a provider run
could exit zero without usable evidence; or the extension needed to recreate a lifecycle
rule. Name the affected version, workflow, and bounded evidence.

This gives the reader a reason for the change before presenting features.

## State the authority that shaped the outcome

Connect the evidence to product intent, applicable standards, reviewed impact, and
resolved decisions. Explain rejected alternatives where they illuminate the trade-off.
For example, centralizing all ecosystem repositories in one workspace may simplify
queries and would collapse product ownership.

The narrative should distinguish discovered facts, proposals, human choices, and verified
results.

## Describe the current product boundary

The recent CIS evolution can be explained as one connected argument:

- explicit ecosystem and product identity prevents repository layout from defining ownership;
- owned and dependency participation bounds product inference and write-capable work;
- the eight-page definition journey creates a single activated product baseline;
- governed references make contract evidence stable and reviewable;
- direct agent execution adds isolation, permissions, attempts, and explicit evidence import;
- test, security, CI, and artifact capabilities strengthen result validity; and
- the VS Code client presents the same CLI and repository authority through a richer workflow.

Listing commands alone would miss why these changes belong together.

## Report verification with limits

Name focused module tests, cross-module lifecycle suites, strict documentation checks,
installed-tool and VSIX smoke tests, golden-path replay, and representative providers.
State failures, unavailable environments, excluded platforms, and residual risk. A release
argument is stronger when its evidence boundary is honest.

## Close with consequences and next questions

Explain what users can now do, what migration or setup changed, which old assumptions no
longer hold, and where human judgment remains required. Then identify future work that is
truly open rather than implying that the architecture is finished forever.

## Keep the format reusable

For every substantial release or milestone, preserve the same chain:

1. observed problem and evidence;
2. intended outcome and non-goals;
3. options, authority, and decision;
4. product and compatibility changes;
5. implementation and migration;
6. verification, limits, and residual risk; and
7. learning applied to future delivery.

## Apply an editorial evidence check

Before publishing, ask whether every behavioral claim links to a current specification,
manual, decision, reference, or verified implementation; whether Draft and planned work
are labelled honestly; whether examples use current syntax; and whether limits are as
visible as capabilities. Then have a reader unfamiliar with the change explain the
problem, decision, migration, and evidence back to the author.

The test of a product-evolution article is not how many features it lists. It is whether
the reader can understand the engineering argument and act safely on the result.

## Takeaway

Explain product evolution as a traceable engineering argument. Readers should be able
to see not only what changed, but why that direction was authorized and how the result
was verified.

## Canonical CIS sources

- [Product intent](../specs/product-intent-spec.md)
- [Implementation roadmap](../specs/implementation-roadmap.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Versioning and release](../standards/versioning-and-release.md)
