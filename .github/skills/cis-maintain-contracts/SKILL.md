---
name: cis-maintain-contracts
description: Keep API, data, configuration, package, permission, route, and ERD references synchronized with implementation contract changes.
---

# CIS Maintain Contracts

## Purpose

Prevent implementation and contract references from drifting apart.

## When to Use

Use for routes, DTOs, schemas, migrations, options, environment keys, dependencies, authorization, roles, UI routes, or entity relationships.

## Inputs

Gather the diff, relevant governance specifications, references under `docs/references/`, and deterministic source evidence.

## Workflow

1. Classify every changed contract surface.
2. Treat OpenAPI as a generated contract baseline, not as a replacement for the row-level API dictionary.
3. Update each affected reference using stable identities and actual implementation evidence in the same change as the implementation.
4. For an API change, co-update its API row, permission usage and semantics, Problem Details identities, consumers, supported version, and OpenAPI where affected.
5. Do not remove an older operation or version until its lifecycle, compatibility window, and known consumers are dispositioned.
6. Update governance specifications only when maintenance rules change.
7. Run `cis references discover` and preview safe additive canonical updates with `cis references reconcile`. Apply them with `--yes` only after review.
8. Run `cis references validate --strict` and `cis references diff --base <delivery-baseline>` for non-API references.
9. Run strict documentation validation, deterministic OpenAPI export, and forward-transitive API diff against every supported baseline in the same major version.

## Output Expectations

Produce implementation and reference co-changes, evidence, lifecycle status, and validation results.

## Guardrails

Do not invent facts, document test fixtures as production contracts, expose secrets, or defer required co-changes silently.

## Related Files

Use `docs/specs/`, `docs/references/`, and `docs/catalog.yml`.
