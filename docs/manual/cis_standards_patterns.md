---
title: "cis standards patterns"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-16"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-standards-patterns
---

# `cis standards patterns`

Inventories and validates the known-standard-pattern catalogue.

## Synopsis

```text
cis standards patterns [--repo <path>] [--language <name>...] [--applicable] [--strict] [--format <human|json|agent>]
```

`--language` filters compiler languages. `--applicable` returns only patterns whose language, role, capability, and Active lifecycle match the classified repository graph. `--strict` turns warnings into failures.

The command combines built-in patterns, loaded `ICisStandardPatternProvider` extensions, and repository Markdown under `<documentation-root>/references/standard-patterns/`. Repository patterns must use the seeded template, declare `type: standard-inference-pattern`, and have a matching canonical catalog entry.

Output reports pattern identity and version, provider, authority, applicability, readiness, source, and missing graph capabilities. A missing capability produces `requires-graph-capability`; CIS does not substitute lexical evidence. Duplicate stable IDs are blocking conflicts and are never resolved by load order.

The C# schema-version-2 adapter advertises attributes, interfaces, inheritance,
external calls, generic arguments, invocation argument shapes, generated markers,
and bounded dataflow. Capability availability means the deterministic extractor ran;
it does not guarantee that a repository contains an occurrence of every fact.

Exit codes are `0` for a valid catalogue, `2` for invalid repository or pattern definitions, and `5` when strict warnings fail validation.
