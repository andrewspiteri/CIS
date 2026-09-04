---
title: "cis agent provider authenticate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-29"
review_cadence: "on command or provider authentication change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-provider-authenticate
---

# `cis agent provider authenticate`

Start an authentication flow implemented by the selected provider adapter.

```text
cis agent provider authenticate <provider> [--method <method>]
  [--timeout-seconds <30..3600>] [--repo <path>]
  [--format <human|json|agent>]
```

Codex supports `browser` (the default) and `device`. Browser authentication runs
`codex login`; device authentication runs `codex login --device-auth`. Both are native
Codex flows. CIS forwards bounded, redacted progress, waits in the foreground, then
diagnoses the provider again.

Claude supports `browser` (the default Claude subscription flow), `console` (Anthropic
Console/API billing), and `sso`. CIS runs the matching provider-native `claude auth login`
flow and diagnoses Claude again when it exits.

CIS does not request an API key, accept a token argument, copy provider credential files,
or persist authentication material. Authentication does not authorize model work and
does not bypass scope, permission, isolation, design, or lifecycle gates.

If `cis agent provider diagnose codex` reports `authentication-unverified`, execution is
still allowed: an ambient Codex Desktop or App Server session may be usable even when
`codex login status` does not confirm it. Use this command for explicit setup, or retain a
successful governed run as execution evidence.

```powershell
cis agent provider authenticate codex --method browser
cis agent provider authenticate codex --method device
cis agent provider authenticate claude --method browser
cis agent provider authenticate claude --method console
cis agent provider authenticate claude --method sso
cis repo doctor --format agent
```

The command exits non-zero for an unsupported provider or method, missing executable,
provider failure, timeout, or cancellation.
