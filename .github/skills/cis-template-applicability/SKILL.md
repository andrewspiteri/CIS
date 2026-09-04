---
name: cis-template-applicability
description: Determine whether a repository-owned deterministic template applies before hand-writing repeated structure. Use before adding scaffolds, repetitive documentation, standard configuration, or generated UI/design assets.
---

# CIS template applicability

1. Run `cis generate status --format agent`.
2. Run `cis generate applicable --task <description> --changed-file <path> --format agent`.
3. Inspect, validate, and render a matching template with every required value.
4. Record `cis generate used`, `cis generate not applicable: <specific reason>`, or `cis generate unavailable: <specific reason>`.
5. Never use applicability to overwrite human-managed output without authority.
