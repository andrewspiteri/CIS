---
applyTo: "docs/changes/**/agent-tasks/*.md"
---

# CIS external tracker guidance

- Canonical task Markdown is authoritative; external issues are mirrors.
- Read the external tracker profile and synchronization specification before commands.
- Run `cis tracker plan` before `push`; never overwrite conflicts or recreate deletion.
- `pull` records drift and cannot change canonical approval, lifecycle, or evidence.
- Human reviewer identity and rationale are mandatory for `resolve`.
- Credentials must not appear in Markdown, `.cis/local/`, logs, or command output.

