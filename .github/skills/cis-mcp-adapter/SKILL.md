---
name: cis-mcp-adapter
description: Expose bounded CIS graph, reference, and change context to a local MCP client. Use when configuring an editor or agent to query an initialized repository through stdio, or when reviewing whether a canonical mutation may be exposed.
---

# CIS MCP adapter

1. Start with a fixed scope: `cis mcp serve --repo <initialized-repository>`.
2. Keep the default read-only tools for discovery and analysis.
3. Add `--allow-mutations` only for a bounded mutation session.
4. Every mutation call still requires `confirm=true`.
5. Treat results like their underlying CIS service result; they grant no approval.
6. Minimize inherited environment variables in the MCP client configuration.

The adapter accepts no per-tool repository override and delegates to CIS services rather than duplicating workflow logic.
