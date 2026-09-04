---
title: "cis artifacts clean"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-artifacts-clean
---

# `cis artifacts clean`

Delete eligible entries in a delete-policy family after retaining a hash tombstone.

```text
cis artifacts clean --family <delete-family> --yes [--repo <path>] [--format <human|json|agent>]
```

The tombstone records paths and hashes but cannot recover deleted bytes. Archive-policy and preserve-policy families are rejected.
