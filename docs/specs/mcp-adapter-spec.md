---
title: "MCP Adapter"
type: technical-specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on MCP protocol, tool schema, scope, or mutation-policy change"
cis:
  stable_id: change-impact-studio:spec:mcp-adapter
---

# MCP adapter

## Boundary

`cis mcp serve --repo <path>` exposes existing CIS application services over local line-delimited JSON-RPC stdio. The process resolves one initialized repository at startup and rejects per-tool path overrides. It does not introduce a second domain or workflow implementation.

The adapter implements the MCP `2025-11-25` initialize, ping, tools/list, and tools/call surface. A newer `server/discover` probe receives the standard unsupported-version error and supported legacy versions so capable clients can downgrade safely. No HTTP listener is created.

## Tools

Default read tools expose repository resolution, graph find/related/trace, normalized reference inventory, and change listing. When `--allow-mutations` is present, graph build and change creation are also advertised.

Mutation safety has two independent gates:

1. the server session must explicitly expose mutations; and
2. the exact call must include `confirm=true`.

Tool annotations declare read-only, destructive, idempotent, and open-world hints. Application failures return MCP tool results with `isError=true`; protocol and unknown-tool failures use JSON-RPC errors.

## Security and evidence

Stdio is local but the child process may inherit environment variables. Client configuration should pass only what CIS requires. The adapter must not log credentials, submitted prompts, or sensitive source text. MCP output has the same authority as its underlying CIS service: evidence is not approval, acceptance, or completion.

Protocol schemas and compatibility are tested with initialize, notifications, list, call, mutation hiding, confirmation rejection, and discovery fallback fixtures.
