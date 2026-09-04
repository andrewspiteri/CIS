---
title: "cis mcp serve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command or protocol change"
cis:
  stable_id: change-impact-studio:manual:cis-mcp-serve
---

# `cis mcp serve`

Run the repository-scoped local MCP stdio adapter.

```text
cis mcp serve --repo <initialized-repository> [--allow-mutations]
```

`--repo` is required and fixes the process scope. By default, only read tools are listed. `--allow-mutations` exposes graph-build and change-create tools, but each call must additionally pass `confirm=true`.

Protocol output is written to standard output; diagnostics go to standard error. Exit code `0` means the client closed the stdio stream normally; `2` means repository startup validation failed.
