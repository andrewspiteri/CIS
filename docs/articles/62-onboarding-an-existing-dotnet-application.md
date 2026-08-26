---
title: "Onboarding an Existing .NET Application"
type: article
status: Draft
series: "CIS in Practice"
series_order: 2
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on .NET classification or initialization change
summary: "Use deterministic classification and reviewed starter content to govern an established C# codebase without rewriting its history."
cis:
  stable_id: change-impact-studio:article:onboard-existing-dotnet
---

# Onboarding an existing .NET application

An established application contains abundant implementation evidence and incomplete
explanations of why that implementation exists.

## Preview classification

```powershell
cis repo init --root docs\cis --dry-run --details --format agent
```

CIS inspects solution and project metadata, ASP.NET Core surfaces, packages, persistence,
tests, configuration, and workflows without executing the application.

## Review selected knowledge

An API application may receive product and technical intent, system context, API and
cache policy, problem and configuration inventories, module ownership, testing and
security standards, and .NET implementation skills.

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

## Takeaway

Onboarding is evidence reconciliation, not retrospective invention. Let deterministic
classification seed the map, then have maintainers confirm the meaning and boundaries
the code alone cannot establish.

## Canonical CIS sources

- [`cis repo init`](../manual/cis_repo_init.md)
- [Classification-driven initialization](../specs/classification-driven-initialisation-spec.md)
- [API design and governance](../specs/api-design-and-governance-spec.md)

