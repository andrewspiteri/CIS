---
title: "Release Notes with Engineering Context"
type: article
status: Draft
series: "Product Evolution and Learning"
series_order: 6
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
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
package smoke test, and checksums. Where execution or CI produced the evidence, identify
the provider run and retained artifact without treating local logs as canonical product
documentation.

## State known limits

Deferred work, unsupported platforms, manual steps, and residual risks belong in release
communication rather than disappearing behind a successful tag.

## Separate release from deployment

A packaged and verified artifact is not proof that every consumer installed it or every
environment deployed it successfully.

## Write for several readers

Users need new capabilities and required actions. Maintainers need changed contracts,
architecture, migrations, and evidence. Operators need rollout, configuration, telemetry,
and rollback. Integrators need versions, deprecations, and compatibility. A useful release
note gives each reader a path without copying the internal change dossier.

## Connect outcome to authority

Begin with the problem and observable outcome. Link the governing change, resolved
decisions, affected specifications, and relevant ADRs. This shows why the product moved in
this direction without retelling every planning discussion.

Avoid presenting generated summaries as product authority. The release note explains
reviewed sources and behavior.

## Describe contracts precisely

Call out new, changed, deprecated, or removed CLI commands, JSON schemas, Markdown
contracts, workspace registries, graph formats, provider capabilities, extension behavior,
and package requirements. State whether the change is compatible, additive with a
migration window, or breaking under the supported policy.

“Internal improvements” is not enough when scripts or clients can observe the difference.

## Provide migration and validation steps

Where users must act, include prerequisites, ordered commands or links to the canonical
manual, expected outcomes, rollback, and version boundaries. Keep secrets and environment-
specific values out of the notes.

The verification section names focused and wider tests, package and VSIX smoke checks,
checksums, golden-path replay, unavailable evidence, and residual risk. A green build badge
is not a complete handoff.

## State known limits visibly

Unsupported providers, platforms, migration gaps, manual steps, retention limits, and
deferred work belong near the affected capability. Users should not need to infer them
from issue trackers.

## Distinguish package, release, and deployment

The release record proves which source produced which verified artifacts. Publication
proves those artifacts were made available. Deployment and adoption remain separate
events under their own systems and owners. Do not claim that a fixed release resolved
every running environment.

## Use a repeatable structure

1. Outcome and audience.
2. Added, changed, deprecated, and fixed behavior.
3. Compatibility and migration.
4. Architecture or policy decisions.
5. Verification and artifact identity.
6. Known limits, residual risk, and follow-up.

## Example of a useful change entry

> Existing standalone repositories can now self-import as the product authority in one
> reviewed transaction. The command requires explicit ecosystem and product IDs plus owned
> participation and `none` relationship. It initializes in place, does not copy source,
> and fails atomically on collision. Existing users of plain repository reconciliation may
> continue using `repo init`; begin with the self-import manual when establishing product
> authority.

This entry states outcome, audience, required input, safety, compatibility, and next action.
It is more useful than “added repository import improvements” and much shorter than the
underlying implementation history.

## Review release language as a contract

Verify command names, versions, dates, links, lifecycle, and claims against the packaged
product. Avoid future tense for incomplete work and do not label a capability generally
available when its provider, platform, or evidence boundary is narrower.

## Takeaway

Write release notes as a product and engineering handoff: outcome, compatibility,
migration, evidence, limits, and next action.

## Canonical CIS sources

- [Versioning and release](../standards/versioning-and-release.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Repository delivery policy](../specs/repository-delivery-policy-spec.md)
