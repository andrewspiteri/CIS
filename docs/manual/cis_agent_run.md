---
title: "cis agent run"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-28"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-run
---

# `cis agent run`

Execute one eligible task through a selected provider in the foreground.

```text
cis agent run <change-id> <task-id> --provider <id> --mode <plan|implement|review> --permission <read-only|workspace-write> --actor <identity> [--target <repository-id>] [--transport <name>] [--timeout-seconds <30..86400>] [--approve-requests] [--repo <path>] [--format <human|json|agent>]
```

The command requires accepted impacts, an approved current plan, and a Ready or InProgress task. An approved design pack is required for downstream frontend work; coordination, wireframe, and visual-design preparation remain eligible because they establish the global design gate. It streams bounded normalized events to standard error and writes the final command result to standard output. Ctrl+C records cancellation and terminates the owned provider process tree.

`--timeout-seconds` is the total foreground-run limit. Each attempt also records a startup limit of at most 30 seconds and an idle-output limit of at most 300 seconds, both bounded by the total limit. Startup, idle, and total expiry produce distinct failure classifications and preserve the first attempt; use `cis agent recover` only for a proven orphan and `cis agent resume` to append a later attempt.

Workspace-write uses an isolated detached Git worktree by default. When the target is dirty, CIS builds the worktree from a private, unreferenced Git snapshot of the exact tracked and non-ignored untracked baseline; it does not move a branch, alter the real index, or count baseline files as provider output. `--approve-requests` authorizes only supported Codex command or file requests contained by that worktree; network and dynamic permission requests remain denied. The result remains derived evidence and does not transition the task.

For Codex `exec-json`, CIS sets `approval_policy="never"` when `--approve-requests` is absent so a non-interactive provider cannot stall on an unseen prompt; sandboxed operations either succeed inside the declared ceiling or fail visibly. When the user explicitly supplies `--approve-requests`, CIS uses Codex automatic review inside the workspace-write sandbox. Oversized but safely parseable tool-result events are retained in bounded form and do not erase the eventual structured completion.

An installed provider with inconclusive authentication is reported as
`authentication-unverified`, not blocked, because ambient Desktop/App Server credentials
may still execute successfully. Repository Doctor reports this distinction and suggests
`cis agent provider authenticate <provider>` when the adapter exposes provider-native
setup. CIS does not accept or persist credentials.
