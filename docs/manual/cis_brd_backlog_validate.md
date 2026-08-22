---
title: "cis brd backlog validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-16"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-backlog-validate
---

# `cis brd backlog validate`

```text
cis brd backlog validate [--workspace <path>] [--format <human|json|agent>]
```

Validates one-to-one functional BRD coverage, source hashes, affected repositories,
frontend types, dependencies, placeholders, approval digest, and technical-intent
readiness. Invalid structure exits `5`; invalid workspace/authority exits `2`.

