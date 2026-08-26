---
title: "Release Notes with Engineering Context"
type: article
status: Draft
series: "Product Evolution and Learning"
series_order: 6
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on release-process change
summary: "Explain outcomes, contract changes, migrations, evidence, and known limits instead of publishing a raw commit list."
cis:
  stable_id: change-impact-studio:article:release-notes-engineering-context
---

# Release notes with engineering context

A list of merged commits describes repository activity. Users need to know what changed
in the product, whether contracts moved, and what action they must take.

## Start with outcomes

Describe the user or maintainer capability delivered and the problem it addresses.

## Identify compatibility

Call out CLI, schema, canonical Markdown, workspace, graph, provider, and extension
contract changes. Link migrations and deprecations.

## Preserve authority and evidence

Reference the governing change, decisions, affected specifications, verification result,
package smoke test, and checksums.

## State known limits

Deferred work, unsupported platforms, manual steps, and residual risks belong in release
communication rather than disappearing behind a successful tag.

## Separate release from deployment

A packaged and verified artifact is not proof that every consumer installed it or every
environment deployed it successfully.

## Takeaway

Write release notes as a product and engineering handoff: outcome, compatibility,
migration, evidence, limits, and next action.

## Canonical CIS sources

- [Versioning and release](../standards/versioning-and-release.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Repository delivery policy](../specs/repository-delivery-policy-spec.md)

