---
applyTo: "docs/changes/**/agent-tasks/*.md"
---

# CIS agent execution authority

- `docs/references/agent-provider-profile.md` defines enabled providers, transports, isolation, timeout, and maximum permissions; credentials remain provider-native.
- Direct execution requires an approved plan, accepted impacts, and a ready or in-progress task. The global design barrier blocks downstream work, not coordination, wireframe, or visual-design preparation needed to establish it.
- Select provider, mode, permission, target, transport, actor, and rationale explicitly. Availability is not authorization.
- Workspace-write execution uses an isolated Git worktree, seeded from the exact tracked and non-ignored untracked source baseline when dirty, unless the reviewed profile explicitly permits direct dirty-working-tree execution.
- Approve provider requests only when the user authorizes per-run approval and the request is inside the declared filesystem and command ceiling. Network permission remains denied unless a future reviewed contract adds it.
- Runs, attempts, events, permissions, process identity, artifact hashes, and results are durable derived evidence under `.cis/local/agents/runs/`.
- Cancellation and resumption append provenance. They never discard the first attempt or rewrite canonical Markdown.
- A provider result is untrusted evidence. Import it explicitly and preserve CIS as the only authority for plan approval, design approval, task transitions, verification, and final acceptance.
