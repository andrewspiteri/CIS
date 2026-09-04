---
title: "cis agent provider diagnose"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-28"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-provider-diagnose
---

# `cis agent provider diagnose`

Probe one provider without starting model work.

```text
cis agent provider diagnose <provider> [--repo <path>] [--format <human|json|agent>]
```

The probe reports the resolved executable path, version, provider-native authentication
availability, transports, and capabilities in human, JSON, and agent output. `portable` is
always available for envelope preparation. Availability grants no execution permission.

Codex executable discovery is deterministic and does not invoke a shell. CIS checks, in
order:

1. the explicit `CIS_CODEX_EXECUTABLE` environment variable;
2. an executable file on the current process `PATH`; and
3. on Windows, the newest installed Codex Desktop executable beneath
   `%LOCALAPPDATA%\OpenAI\Codex\bin\<installation>\codex.exe`.

Use `CIS_CODEX_EXECUTABLE` only as an absolute-path override when Codex is installed in a
non-standard location. The probe must still obtain a valid version and provider-native
authentication status. An `authentication-unverified` status is advisory and remains
executable: `codex login status` may not expose a usable ambient Codex Desktop or App
Server session. A successful governed run is retained as the stronger execution evidence.
Use `cis agent provider authenticate codex` when explicit provider setup is wanted.

Claude executable discovery follows the same shell-free precedence: explicit
`CIS_CLAUDE_EXECUTABLE`, the current process `PATH`, then the newest Claude Code native
binary installed by VS Code, VS Code Insiders, Cursor, or Windsurf on Windows. Use
`cis agent provider authenticate claude --method browser` for the default Claude
subscription flow; `console` and `sso` are also available.

The resolved full path and executable digest are recorded in run
provenance; registering the provider does not prove that its executable or authentication
is available.
