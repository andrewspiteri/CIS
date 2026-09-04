---
title: "Agent Provider Profile"
type: agent-provider-profile
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-28"
review_cadence: "on provider, permission, isolation, or retention-policy change"
cis:
  stable_id: change-impact-studio:reference:agent-provider-profile
---

# Agent provider profile

No direct provider is selected by default. A controller must select a discovered provider for every run. Credentials remain provider-native and must never be copied into CIS configuration, envelopes, events, or canonical documents.

| Setting | Value | Rationale |
|---|---|---|
| default-provider | none | Avoid silent vendor selection. |
| default-transport | provider-preferred | Capability negotiation remains explicit. |
| implementation-isolation | git-worktree | Preserve the user's current working tree while seeding the isolated worktree from its exact tracked and non-ignored untracked baseline. |
| direct-dirty-working-tree | denied | Requires an explicit reviewed policy change. |
| maximum-run-seconds | 3600 | Bound foreground execution. |
| maximum-startup-seconds | 30 | Fail closed when a provider process produces no startup evidence. |
| maximum-idle-seconds | 300 | Classify a silent stalled provider separately from total expiry. |
| approve-within-ceiling | denied | Provider requests require explicit per-run authorization. |

## Providers

| Provider | Enabled | Preferred transport | Allowed modes | Maximum permission | Notes |
|---|---|---|---|---|---|
| codex | yes | app-server | plan, implement, review | workspace-write | `exec-json` is an explicit non-interactive fallback. Resolution checks `CIS_CODEX_EXECUTABLE`, process PATH, then the current Windows Codex Desktop installation. Inconclusive login status is advisory because ambient Desktop/App Server authentication can still execute. Explicit setup uses provider-native browser or device authentication through `cis agent provider authenticate codex`; CIS never receives credentials. Network escalation remains denied. |
| claude | yes | stream-json | plan, implement, review | workspace-write | Headless execution uses predeclared permissions. |
| portable | yes | envelope | plan, implement, review | read-only | Preparation only; no direct execution. |

## Authority boundary

Provider availability is not authorization. Direct execution requires accepted impacts, an approved current plan, an eligible task, and a satisfied global design barrier. Agent output is untrusted evidence until explicitly imported; only canonical CIS lifecycle commands and human decisions can approve, accept, transition, verify, or close work.
