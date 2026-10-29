---
title: "cis test validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-test-validate
---

# `cis test validate`

Repository discovery recognizes inherited literal test settings from the nearest
`Directory.Build.props` and `Directory.Build.targets`, in project evaluation order.
Simple quoted property equality and inequality conditions can be resolved when the
property has a known literal value, including `IsTestProject`. Unknown conditions,
property functions and imports are not executed and still require review.

For an existing custom harness, register its exact command, working directory,
adapter and result paths in the canonical test-suite profile, then validate that
profile. Discovery does not replace the harness or claim its tests executed.

Validate suite identities, layers, adapters, commands, working directories, and evidence paths.

```text
cis test validate [--strict] [--repo <path>] [--format <human|json|agent>]
```

Strict mode requires result, coverage, and mutation evidence beneath `.cis/local/`.
