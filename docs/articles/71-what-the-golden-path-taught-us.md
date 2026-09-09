---
title: "What the Golden Path Taught Us"
type: article
status: Draft
series: "Product Evolution and Learning"
series_order: 1
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on golden-path review
summary: "Real product replays expose classification, contract, packaging, and authority gaps that isolated tests cannot reveal."
cis:
  stable_id: change-impact-studio:article:golden-path-lessons
---

# What the golden path taught us

The Friends Todo replay did more than verify commands. It challenged whether CIS's model
of software delivery matched a real multi-repository application.

## Classification needs real frameworks

Static fixtures did not expose every route style, generated file, or package behavior.
Real Next.js, Express, and authentication middleware showed where discovery rules were
too narrow or framework defaults could create unmanaged governance files.

## API governance needs provider-aware boundaries

Authentication revealed that pre-authentication protocol routes are not ordinary public
application endpoints and that SDK-owned dynamic route families need narrow reviewed
declarations rather than weakened validation.

## Source success is not package success

A feature present in source and regression tests can be absent from the executable used
by a case study. Artifact freshness and installed-package smoke checks are part of product
correctness.

## Initialization must reconcile, not stamp

Repeated onboarding showed the importance of stable IDs, human ownership, additive
selection, and strict collision handling as repository capabilities evolved.

It also made the product boundary explicit: one workspace represents one product in an
ecosystem, owned repositories can inform that product, and dependency repositories remain
bounded context. The product-definition journey turns that boundary into an activated,
digest-bound baseline before downstream planning relies on it.

## Learning needs full delivery

Each lesson becomes valuable only when the owning specification, source, manual, tests,
package, and golden path agree.

## Product boundaries must be executable rules

The replay showed that “these repositories belong to one product” is too vague. CIS needs
one explicit authority, owned participants that may inform product definition and receive
implementation work, and dependencies that remain bounded read context. Product and
ecosystem IDs must survive folder names and local checkout layouts.

Those distinctions affect graph queries, BRD discovery, technical intent, planning,
write-capable agent targets, and acceptance. A boundary documented only in prose will
eventually be violated by automation.

## Definition needs an activation point

Collecting business, technical, architecture, contract, experience, and delivery material
is not enough if downstream work can combine arbitrary revisions. The eight-page journey
creates reviewable artifacts and a consolidated activation digest. Feature authoring can
then prove which product baseline it consumed.

Existing implementation remains useful evidence without becoming stakeholder authority.
The distinction was easier to see in a real application containing legacy behavior and
framework defaults than in empty fixtures.

## Provider integration needs evidence semantics

Launching an agent is the easy part. Safe coordination requires eligibility, current task
digests, explicit provider and transport, permission ceilings, isolated worktrees,
timeouts, cancellation, resume, normalized events, result validation, and explicit import.

The replay also exposed a crucial state: a provider process can exit successfully while
the expected result is missing or malformed. `InvalidEvidence` preserves that difference
instead of rewarding a green process status.

## Thin clients reveal contract quality

The VS Code extension forced CLI output, progress, authority selection, and error semantics
to become clear enough for another client. When the extension needed to recreate domain
rules, the underlying CLI contract was incomplete. Keeping the editor thin improved both
terminal and UI behavior.

## Assurance extends beyond unit tests

Real delivery needed test-result reconciliation, security findings and exceptions, CI
investigation, artifact retention, package installation, and clean-profile VSIX checks.
Fixtures proved module behavior; the golden path proved whether the distributed pieces
formed a usable product.

## Learning should name the owning layer

Every failure should be classified as sample configuration, product specification,
source implementation, provider, test, package, client, or documentation. “Fix the demo”
is not a useful learning outcome. A bounded finding leads to a focused regression and a
reviewed improvement in the layer that owns the contract.

## The replay remains evidence with limits

Friends Todo cannot represent every language, framework, provider, or enterprise topology.
Its value is a stable, understandable system that exercises the complete governance chain.
Each new product gap should add focused coverage without turning the sample into a maze of
special cases.

## Takeaway

Golden paths reveal integration assumptions. Treat every failure as evidence about the
product model, trace it to the owning contract, and replay after the complete fix.

## Canonical CIS sources

- [Implementation roadmap](../specs/implementation-roadmap.md)
- [Classification-driven initialization](../specs/classification-driven-initialisation-spec.md)
- [Product and ecosystem boundary](../specs/product-and-ecosystem-boundary-spec.md)
- [High-level product-definition wizard](../specs/high-level-product-definition-wizard-spec.md)
- [Versioning and release](../standards/versioning-and-release.md)
