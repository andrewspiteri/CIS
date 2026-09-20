---
title: "Onboarding an Existing .NET Application"
type: article
status: Active
series: "CIS in Practice"
series_order: 2
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on .NET classification or initialization change
summary: "Use deterministic classification and reviewed starter content to govern an established C# codebase without rewriting its history."
cis:
  stable_id: change-impact-studio:article:onboard-existing-dotnet
---

# Onboarding an existing .NET application

An established application contains abundant implementation evidence and incomplete
explanations of why that implementation exists.

## Establish the product authority in place

For an existing application that owns its product documentation locally, the preferred
flow self-imports the repository. This initializes it in place and registers the same
repository as the single product workspace authority without copying implementation files.

```powershell
cis repo import `
  --workspace C:\work\orders `
  --source C:\work\orders `
  --root docs\cis `
  --participation owned --relationship none `
  --ecosystem commerce --product ordering `
  --dry-run --format agent

cis repo import `
  --workspace C:\work\orders `
  --source C:\work\orders `
  --root docs\cis `
  --participation owned --relationship none `
  --ecosystem commerce --product ordering `
  --yes
```

CIS plans the complete initialization and registry transaction before mutation. Underlying
repository classification inspects solution and project metadata, ASP.NET Core surfaces,
packages, persistence, tests, configuration, and workflows without executing the
application.

## Review selected knowledge

An API application may receive product and technical intent, system context, API and
cache policy, API, permission, data, problem and configuration inventories, module
ownership, testing and security standards, and .NET implementation skills.

Detected source facts remain evidence. Owners, lifecycle, compatibility, and business
meaning require review.

## Handle collisions deliberately

Existing documentation is retained. A path or stable-ID conflict stops application. If
the current file is the intended authority, review it and use `--accept-current --yes`
to record human ownership rather than replacing it.

## Validate and build context

```powershell
cis docs validate --strict
cis standards applicable --target backend --stack csharp
cis graph build
cis graph validate --strict
cis api discover
cis api validate
```

The product-definition business, technical, and contract pages can then prepare bounded
existing-system evidence from this owned repository. Code and tests describe observed
behaviour; they do not establish stakeholder intent or deployed configuration. Inferred
drafts still require independent review, question resolution, and human activation.

## Inspect before importing

Confirm the repository boundary, Git status, intended documentation root, product and
ecosystem identity, and whether the application truly owns its product. A shared library
or service owned elsewhere may belong as a dependency in another authority workspace
rather than becoming a standalone product by convenience.

Run self-import as a dry run and inspect the full classification. CIS may observe C#
projects, ASP.NET Core routes, Entity Framework packages, test assemblies, GitHub
workflows, configuration patterns, and existing Markdown. Those are implementation facts,
not approval of the architecture they imply.

## Reconcile existing documentation

An established repository may already contain an ADR folder, API notes, runbooks, or a
README with product intent. CIS should retain those files and surface path or stable-ID
collisions. Decide whether existing content is the intended authority, source evidence,
or obsolete material before accepting ownership or creating a new canonical record.

Avoid copying the same meaning into a second CIS-shaped document merely to satisfy a
template. Stable links and explicit source assessment are better than duplicate authority.

## Separate observed behavior from intended behavior

The current API may expose an undocumented route; a test may encode a historical bug; a
database dependency may be transitional; a workflow may represent an environment that no
longer deploys. Product-definition preparation can summarize these observations with
citations and open questions. Humans decide which behavior is required, tolerated,
deprecated, or incorrect.

This is the central onboarding discipline: preserve the evidence without turning the
existing implementation into unquestioned product policy.

## Validate representative surfaces

After import, build the graph and inspect a few known components, routes, tests, and
workflows. Run API discovery and validation where applicable. Check that repository paths,
ownership, and stable identities resolve correctly and that generated output or package
trees are excluded.

Strict validation proves structural health, not semantic completeness. Ask maintainers
which critical dependencies, operational workarounds, and customer promises remain outside
the repository.

## Adopt in stages

Start with one bounded product definition and one representative change. Use the first
delivery to test whether context routing, task ownership, validation, and evidence match
the application. Reconcile gaps into governed references, graph declarations, standards,
or tests.

An adoption does not require rewriting the repository's history or every document at once.
It requires making current authority and known uncertainty explicit enough that the next
change can be governed honestly.

## Takeaway

Onboarding is evidence reconciliation, not retrospective invention. Let deterministic
classification seed the map, then have maintainers confirm the meaning and boundaries
the code alone cannot establish.

## Canonical CIS sources

- [`cis repo init`](../manual/cis_repo_init.md)
- [`cis repo import`](../manual/cis_repo_import.md)
- [Classification-driven initialization](../specs/classification-driven-initialisation-spec.md)
- [API design and governance](../specs/api-design-and-governance-spec.md)
- [High-level product-definition wizard](../specs/high-level-product-definition-wizard-spec.md)
