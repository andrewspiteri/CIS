---
title: "cis solution-design validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-solution-design-validate
---

# `cis solution-design validate`

```text
cis solution-design validate [--workspace <path>] [--format <human|json|agent>]
```

Validates both halves of the solution-design bundle: identities, managed structure, required
sections, component uniqueness and cross-reference, source currency, lifecycle parity, human
approval metadata, and the approved bundle digest. It does not modify either document.

Implementation-authored diagrams are also checked for bounded content and valid relationships.
SchemaVersion 2 enforces C4 context, container and scoped component boundaries, shared identities,
responsibilities, technology and evidence status. Legacy diagram models remain readable.
