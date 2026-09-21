---
title: "The Friends Todo Golden Path"
type: article
status: Active
series: "CIS in Practice"
series_order: 4
owner: "Andrew Spiteri"
last_reviewed: "2026-09-21"
review_cadence: on golden-path change
summary: "Why a small multi-repository product is a demanding end-to-end test of intent, classification, contracts, execution, and assurance."
cis:
  stable_id: change-impact-studio:article:friends-todo-golden-path
---

# The Friends Todo golden path

Friends Todo is intentionally ordinary: a web application, API, and infrastructure for
lists shared with friends. Its value is the number of engineering boundaries hidden by
the simple domain.

## What the replay exercises

The golden path covers explicit ecosystem and product identity, owned and dependency
repository participation, the activated product definition, BRD and technical intent,
graph federation, feature intake and definition, delivery reconciliation, feature
derivation, public/customer/backoffice classification, API governance, authentication
protocol routes, cache policy, provider-owned dynamic routes, task execution, browser and
integration tests, and final verification.

## Why complete replay matters

Focused tests can prove a command in a fixture. The replay exposes disagreements between
source and packaged tools, framework-generated files, real package behavior, multi-repository
paths, runtime providers, and documentation starters.

## Gaps become product evidence

When the replay finds a missing Express route extractor, unsafe protocol classification,
or stale packaged executable, the result is not patched only in the sample. It produces
a CIS specification, implementation, manual, and regression-test change.

## The golden path is not a demo script

A demo can work around product gaps. A golden path records them, fixes the owning product
contract, and reruns from governed baselines.

The replay also keeps authority boundaries visible. Product-owned repositories contribute
to product inference and implementation routing. Dependency repositories provide bounded
context, but CIS does not silently turn them into product requirements or implementation
targets.

## Why an ordinary product is useful

Friends Todo avoids an exotic domain while retaining realistic boundaries: anonymous and
authenticated experiences, access control, public and identity routes, persistence,
multi-repository ownership, deployment, and third-party packages. A maintainer can
understand the product quickly, so failures point toward CIS behavior rather than domain
confusion.

The golden path is intentionally larger than a command smoke test and smaller than an
unbounded enterprise system.

## Replay the complete lifecycle

A representative run should:

1. create or reset the sample repositories to known revisions;
2. initialize the product authority with stable ecosystem and product IDs;
3. import owned participants and bounded dependencies explicitly;
4. validate documentation, standards, skills, and repository health;
5. build each graph and the workspace view;
6. define and activate the product baseline;
7. intake one prepared feature BRD and review its eight-page feature definition;
8. separate Foundation, MVP, and Post-MVP scope, then compare it with existing owned
   implementation without treating model findings as approval;
9. reconcile the reviewed scope into the backlog, complete and approve the feature
   specification, and derive or explicitly review bounded work;
10. execute representative human or agent tasks in bounded targets;
11. reconcile test, security, CI, and artifact evidence;
12. compare actual Git state with approved scope; and
13. record acceptance and reviewed learning.

Skipping the early authority stages turns the replay into an implementation demo. Skipping
verification proves only that files were produced.

## Assert boundaries, not screenshots

The replay should assert stable outcomes: one product authority, correct repository
participation, no dependency writes, exact activation and graph digests, separate frontend
classifications, valid contract inventory, task coverage, contained provider changes,
parseable evidence, and explicit acceptance gates.

UI screenshots can support review, but they should not be the only proof that the CLI and
repository state are correct.

## Treat every workaround as a finding

If a route extractor misses an Express pattern, the sample should not add a fake static
route merely to continue. If the packaged tool lacks a current command, the replay should
identify artifact freshness rather than falling back silently to a source-only invocation.

Classify the failure by owner: sample configuration, product specification, source,
provider, test, packaging, or documentation. Then deliver the fix through that contract
and rerun from the governed baseline.

## Keep the replay reproducible

Pin source revisions and material package versions, record SDK and tool identity, use
isolated local state, and preserve bounded artifacts and diagnostics. Cleanup should remove
only generated replay state, never canonical evidence needed to explain a failure.

The sample itself should remain readable. Excess fixture magic can make a green replay
difficult to trust.

## What success means

A successful golden path proves that this declared lifecycle worked against the selected
product revision and environment. It does not prove every framework, provider, or change
type. Its continuing value comes from rerunning after product evolution and adding focused
regressions for every real gap it exposes.

## Takeaway

Use a small but complete product to test the whole governance chain. The best golden path
does not prove the tool is perfect; it repeatedly reveals where the tool's model of real
delivery is incomplete.

## Canonical CIS sources

- [Implementation roadmap](../specs/implementation-roadmap.md)
- [Product and ecosystem boundary](../specs/product-and-ecosystem-boundary-spec.md)
- [High-level product-definition wizard](../specs/high-level-product-definition-wizard-spec.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [`cis brd feature intake`](../manual/cis_brd_feature_intake.md)
- [`cis brd feature wizard delivery`](../manual/cis_brd_feature_wizard_delivery.md)
