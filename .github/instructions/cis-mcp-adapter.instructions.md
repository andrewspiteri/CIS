---
applyTo: "**"
---

# CIS MCP adapter authority

- Fix every MCP process to its explicit initialized `--repo` scope.
- Read tools are default; mutation tools require both `--allow-mutations` and per-call `confirm=true`.
- Delegate to CIS services and preserve their validation and authority boundaries.
- MCP results are evidence, not approval, acceptance, or completion.
- Use local stdio, minimize inherited environment, and never emit credentials or sensitive content in diagnostics.
