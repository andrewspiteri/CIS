---
title: "cis frontend validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command or provider change"
cis:
  stable_id: change-impact-studio:manual:cis-frontend-validate
---

# `cis frontend validate`

Validate provider identity, source evidence, route uniqueness, and canonical screen-route correlation.

```text
cis frontend validate [--strict] [--no-refresh] [--repo <path>] [--format <human|json|agent>]
```

Validation refreshes discovery unless `--no-refresh` is supplied. Strict mode fails on canonical route drift warnings. Exit code `0` means valid; `4` means state/repository unavailable; `5` means validation failed.
