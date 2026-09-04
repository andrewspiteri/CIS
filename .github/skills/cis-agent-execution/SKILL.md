---
name: cis-agent-execution
description: Execute one approved CIS task through a discovered Codex or Claude provider with explicit modes, permissions, isolation, durable provenance, cancellation, resumption, and result reconciliation.
---

# CIS agent execution

1. Run `cis repo init --root <documentation-root>` when required. If initialization or command discovery fails, run `cis repo doctor` before retrying.
2. For pre-change BRD work, use `cis agent author brd` only with explicitly selected references. Then use `cis agent review brd` with a different provider, review mode, and read-only isolation; include extracted authoring evidence only after explicit disclosure authority. For delivery work, read `docs/references/agent-provider-profile.md`, the approved change plan, the selected ready task, and repository delivery policy.
3. Run `cis agent providers --format agent` and `cis agent provider diagnose <provider> --format agent`; provider availability never grants authority.
4. Run `cis agent prepare <change-id> <task-id> --provider <provider> --format agent` and inspect the bound digest and permission ceiling.
5. Run `cis agent run` with an explicit provider, mode, permission, target, transport, actor, and reason. Workspace-write defaults to an isolated Git worktree whose baseline includes current tracked and non-ignored untracked changes.
6. Use `--approve-requests` only when the user explicitly authorizes provider requests within the declared ceiling. Network access and paths outside the bounded workspace remain denied.
7. Inspect durable state with `cis agent runs` and `cis agent show`; cancel with an actor and reason. Use `recover` only for a proven orphaned process before an explicit `resume`. A resumed attempt never erases the first attempt.
8. Inspect the structured result before `cis agent import-result`. Imported output is evidence only; it cannot approve plans, accept designs, complete tasks, or verify delivery.

Never copy provider credentials into CIS, infer permission from tool availability, run a non-ready task, bypass the design barrier for downstream work, or treat an agent's completion claim as canonical CIS state. Coordination, wireframe, and visual-design preparation establish the barrier and are therefore eligible before approval.
