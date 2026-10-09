---
title: "cis agent run"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-09"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-run
---

# `cis agent run`

Codex connection errors marked for automatic retry remain visible in the event journal
without prematurely ending the run. CIS waits for provider recovery or a terminal result;
the configured cancellation and timeout limits continue to apply.
An explicitly reported context-consolidation operation uses the overall run deadline
because it can be active without streamed output. Ordinary idle monitoring resumes when
that operation completes; unrelated notifications cannot mark it complete.

Codex App Server may stream delegated child threads alongside the selected task.
CIS retains their events, but only the selected thread and acknowledged turn can
supply the completion message, finish the run or change its context-compaction state.
Child failures remain in the event journal. A failed or malformed turn-start response
ends with a protocol failure, as do duplicate responses and unsupported request or response IDs.
Notifications arriving before that response are journaled immediately and buffered
within fixed limits, then matched to its turn identity. Buffer overflow produces
invalid evidence. Unidentified completion messages cannot establish success; an
unscoped fatal error still fails the run. Child work remains subject to the ordinary
idle deadline.

Streaming output is journaled with bounded payloads: retained raw events are limited to
65,536 characters, and lines over 4,194,304 characters produce an invalid-event diagnostic.
A stdout-reader journal failure ends the run and terminates the provider process. Routine run/attempt manifest checkpoints are
limited to once per second; process/session identity, permission and lifecycle changes
are checkpointed immediately. The journal sequence count is cached under the append lock
and re-read after another controller changes the file length or after a process restart,
so long code-reading sessions do not rescan their accumulated log for every output fragment.

Execute one eligible task through a selected provider in the foreground.

```text
cis agent run <change-id> <task-id> --provider <id> --mode <plan|implement|review> --permission <read-only|workspace-write> --actor <identity> [--target <repository-id>] [--transport <name>] [--timeout-seconds <30..86400>] [--approve-requests] [--allow-command <command>] [--repo <path>] [--format <human|json|agent>]
```

The command requires accepted impacts, an approved current plan, and a Ready or InProgress task. An approved design pack is required for downstream frontend work; coordination, wireframe, and visual-design preparation remain eligible because they establish the global design gate. It streams bounded normalized events to standard error and writes the final command result to standard output. Ctrl+C records cancellation and terminates the owned provider process tree.

Participant runs receive bounded, hash-checked snapshots of the selected authority documents. When present, the task dossier's `test-cases.csv` accompanies `test-cases.md`, so the agent can inspect the export itself. These snapshots retain authority-relative identities and do not overwrite participant files.

CIS records the snapshots and hashes before the provider starts, then checks the originals before accepting successful execution and before resuming. A changed bound CSV causes result rejection or blocks resume. For a generated catalogue, follow the [feature-plan import and derivation guidance](cis_plan_import_spec.md) to regenerate its projections and establish an approved current plan before starting a fresh run. Resuming an existing run requires its original bound context to remain current.

`--timeout-seconds` is the total foreground-run limit. Each attempt also records a startup limit of at most 30 seconds and an idle-output limit of at most 300 seconds, both bounded by the total limit. Startup, idle, and total expiry produce distinct failure classifications and preserve the first attempt; use `cis agent recover` only for a proven orphan and `cis agent resume` to append a later attempt.

Workspace-write uses an isolated detached Git worktree by default. When the target is dirty, CIS builds the worktree from a private, unreferenced Git snapshot of the exact tracked and non-ignored untracked baseline; it does not move a branch, alter the real index, or count baseline files as provider output. `--approve-requests` authorizes only supported Codex command or file requests contained by that worktree; network and dynamic permission requests remain denied. The result remains derived evidence and does not transition the task.

The Claude adapter runs non-interactively and does not relay interactive permission requests.
Its workspace-write `acceptEdits` mode does not by itself approve shell commands. A denied
command may therefore end the run without displaying an approval prompt in CIS or another
Claude session. A `permission-required` result is a failed attempt, not a waiting approval;
inspect the retained evidence and explicitly authorize the required commands before
retrying. A completed source review does not establish that the blocked commands ran.

The child process receives a filtered environment. Standard Windows user-data, machine-data and program-directory locations are inherited when present so native build tools can find their configuration. CIS preserves the host's values without assuming a drive or installation layout. This does not broaden filesystem or network permissions or inherit arbitrary application credentials; a build can still fail when its configured cache or dependencies are inaccessible.

The Windows directory variables are `APPDATA`, `LOCALAPPDATA`, `ProgramData`, `ALLUSERSPROFILE`, `ProgramFiles`, `ProgramFiles(x86)` and `ProgramW6432`. Existing home-directory, temporary-directory and executable-search settings are also retained.

For Codex `exec-json`, CIS sets `approval_policy="never"` when `--approve-requests` is absent so a non-interactive provider cannot stall on an unseen prompt; sandboxed operations either succeed inside the declared ceiling or fail visibly. When the user explicitly supplies `--approve-requests`, CIS uses Codex automatic review inside the workspace-write sandbox. Oversized but safely parseable tool-result events are retained in bounded form and do not erase the eventual structured completion.

An installed provider with inconclusive authentication is reported as
`authentication-unverified`, not blocked, because ambient Desktop/App Server credentials
may still execute successfully. Repository Doctor reports this distinction and suggests
`cis agent provider authenticate <provider>` when the adapter exposes provider-native
setup. CIS does not accept or persist credentials.

## Explicit command permissions

For an implementation task with `workspace-write`, optionally repeat `--allow-command` for each reviewed
command needed by the task. Claude currently supports this capability; other adapters reject
it unless they declare support. `--approve-requests` does not supply this command list.

```powershell
cis agent run CIS-0001 WORK-090 --provider claude --mode implement --permission workspace-write --actor "Project maintainer" --allow-command "dotnet build -c Release" --allow-command "dotnet bin/Release/net10.0/Example.Cli.dll --help"
```

Use commands appropriate to the selected task and project. The list applies only to the current
attempt and is retained in its manifest and permission journal with the actor and timestamp.
A resumed attempt requires the list again. CIS restarts native session context when either
attempt uses explicit commands, preventing permissions retained by that session from being
reused silently. The digest-bound task and retained continuation evidence still apply.

Each entry must contain a program and arguments. CIS accepts ASCII letters, digits, single
spaces and `- _ . / : =`; it rejects wildcards, quotes within the command, shell operators,
expansions, duplicate entries, more than 32 entries and entries over 2,048 characters. The outer
PowerShell quotes above pass one CLI argument and are not part of the command. Paths needing
internal quotes are not supported by this option. Do not put secrets in recorded commands.

Claude receives literal Bash and PowerShell permission rules without wildcards. Native matching
can normalize wrappers and evaluate compound commands separately; an allowed command can
still run after an independently permitted directory change. CIS asks for separate commands
in the assigned directory, but that instruction is not deterministic confinement. These rules add native permissions;
they do not create an operating-system sandbox, remove existing native permissions, constrain
build scripts, or guarantee that a subprocess stays inside the worktree. Review the invoked
project and its scripts as well as the command. Existing native deny rules still apply. This
option does not grant product approval or make a reported run success proof that checks ran.
Inspect actual command output and required verification evidence.

See [Claude's permission rules](https://code.claude.com/docs/en/permissions) for native matching
semantics and [programmatic execution](https://code.claude.com/docs/en/headless) for unattended
permission behavior.
