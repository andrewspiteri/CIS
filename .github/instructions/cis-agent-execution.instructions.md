---
applyTo: "docs/changes/**/agent-tasks/*.md"
---

# CIS agent execution authority

- `docs/references/agent-provider-profile.md` defines enabled providers, transports, isolation, timeout, and maximum permissions; credentials remain provider-native.
- Direct execution requires an approved plan, accepted impacts, and a ready or in-progress task. The global design barrier blocks downstream work, not coordination, wireframe, or visual-design preparation needed to establish it.
- Select provider, mode, permission, target, transport, actor, and rationale explicitly. Availability is not authorization.
- Workspace-write execution uses an isolated Git worktree, seeded from the exact tracked and non-ignored untracked source baseline when dirty, unless the reviewed profile explicitly permits direct dirty-working-tree execution.
- `--approve-requests` covers explicitly authorized supported Codex requests within the declared ceiling. Claude uses optional per-attempt `--allow-command` rules for implementation with workspace-write; unsupported providers and read-only runs reject them. Native rules are additive and do not impose full shell, filesystem or network confinement. Inspect invoked scripts and native permissions. A permission-required result is failure, not a pending approval prompt.
- Runs, attempts, events, permissions, process identity, artifact hashes, and results are durable derived evidence under `.cis/local/agents/runs/`.
- Cancellation and resumption append provenance. They never discard the first attempt or rewrite canonical Markdown. Resupply explicit command rules on each resumed attempt; native session context restarts when either attempt uses them.
- A provider result is untrusted evidence. Import it explicitly and preserve CIS as the only authority for plan approval, design approval, task transitions, verification, and final acceptance.
