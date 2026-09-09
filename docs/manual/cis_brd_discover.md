---
title: "cis brd discover"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-discover
---

# `cis brd discover`

Finds possible business-requirements documents, domain-equivalent product-design
documents such as GDDs, and development feature specifications across an authority
workspace without treating discovery as proof of correctness, absorption, or currency.

## Synopsis

```text
cis brd discover [--workspace <path>] [--format <human|json|agent>]
```

Discovery checks up to 10,000 Markdown files per registered repository and reports up
to 100 candidates using filename, heading, and document-type signals. Product-design
front matter and Game Design Document/GDD signals are treated as BRD evidence. Feature
templates are excluded; copied feature specifications should declare
`type: feature-specification`. Every candidate has a kind, stable source ID, and content
hash. The command also reports graph build identity,
head, dirty state, freshness, role, and diagnostics for every repository.

Automatic discovery excludes agent configuration and skill directories, along with
`SKILL.md`, `AGENTS.md`, and `CLAUDE.md`: instructions for authoring requirements do
not become product requirements merely because they mention a BRD.

The workspace must identify one authority repository and every repository must have a
built graph. Participant graphs must be fresh. A stale authority graph is reported as
a warning because canonical BRD edits can make it stale without invalidating participant
evidence.

## Results

| Status | Meaning |
| --- | --- |
| `missing` | No source BRD, product-design document, feature specification, or canonical BRD was discovered. |
| `review-required` | One or more non-canonical candidates require human assessment. |
| `canonical-found` | Only the managed canonical BRD was found. |
| `incomplete` | Required graph evidence is unavailable or stale. |

## Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | Discovery completed, including a missing result. |
| `2` | Workspace or authority configuration is invalid. |
| `4` | Required graph evidence is unavailable. |

## Example

```powershell
cis brd discover --workspace C:\work\product-docs --format agent
```

## Related commands

- [`cis brd init`](cis_brd_init.md)
- [`cis brd reconcile`](cis_brd_reconcile.md)
- [`cis workspace init`](cis_workspace_init.md)
- [`cis graph build`](cis_graph_build.md)
