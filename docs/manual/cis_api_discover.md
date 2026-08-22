---
title: "cis api discover"
type: manual
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-api-discover
---

# `cis api discover`

Correlates source endpoints, the canonical API dictionary, and configured current
OpenAPI documents into normalized derived state. Markdown remains authoritative.

```text
cis api discover [--repo <path>] [--openapi <path> ...] [--format <human|json|agent>]
```

| Option | Required | Effect |
|---|---|---|
| `--repo` | No | Initialized repository; defaults to the current directory. |
| `--openapi` | No | Current OpenAPI JSON paths overriding the profile for this run. |
| `--format` | No | Human, JSON, or compact agent output. |

The command writes `operations.json`, `manifest.json`, and `diagnostics.json` beneath
`.cis/local/api/`. It is content-aware and idempotent and never rewrites canonical rows.
Exit `2` means invalid repository/request; exit `5` means a discovery error. Run
`cis repo doctor` after repository-resolution failures.

