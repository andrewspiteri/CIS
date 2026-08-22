---
title: "cis technical-intent validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-16"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-technical-intent-validate
---

# `cis technical-intent validate`

```text
cis technical-intent validate [--workspace <path>] [--format <human|json|agent>]
```

Validates workspace scope and schema, required architecture/data/contracts/security/
operations/quality sections, authoring placeholders, structured `TI-DEC-*` decisions,
the Active BRD hash, participant graph builds, approval evidence, and approved-content
digest. Open or Proposed decisions are blocking; a Deferred decision cannot cross its
declared change-dossier gate. Invalid content exits `5`; workspace errors exit `2`.
