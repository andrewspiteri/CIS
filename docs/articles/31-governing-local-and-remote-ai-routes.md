---
title: "Governing Local and Remote AI Routes"
type: article
status: Draft
series: "Human and Agent Execution"
series_order: 6
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on AI-provider or privacy change
summary: "Capability routes, local-first selection, explicit remote authorization, caching, and sanitized usage evidence."
cis:
  stable_id: change-impact-studio:article:governing-ai-routes
---

# Governing local and remote AI routes

“Use AI” is not a complete execution policy. Different capabilities need different
models, privacy boundaries, latency, and evidence.

## Route by named capability

CIS maps capabilities—such as compact summarization or advisory analysis—to a provider,
model, remote permission, and cache policy. Tasks request a capability rather than
hard-coding whichever model happens to be available.

## Automatic selection remains local

When a route asks for the smallest suitable local model, CIS can select an available
local provider automatically. Automatic selection never escalates repository content
to a remote service.

## Remote use needs two permissions

A remote operation requires a canonical route that allows it and explicit authorization
for the exact content-bearing invocation. Configuration alone does not imply consent to
send the selected repository material.

## Sensitive content is invalid input

Likely secrets, tokens, private keys, certificates, credentials, and sources marked
sensitive are rejected rather than sent with optimistic redaction.

## Cache without creating authority

Model caches are disposable local state bound to route and input identity. A cached
response can improve performance but cannot become a durable decision or approval.

## Record usage without content

Sanitized usage records can retain provider, model, timing, token estimates, cache state,
and outcome while excluding prompts, responses, source, and secrets.

## Takeaway

Govern models through named capabilities, local-first selection, explicit remote
authorization, sensitive-input rejection, disposable caching, and content-free usage
evidence. Provider availability should never decide privacy policy.

## Canonical CIS sources

- [AI routing profile](../references/ai-routing-profile.md)
- [`cis ai routes`](../manual/cis_ai_routes.md)
- [`cis ai usage`](../manual/cis_ai_usage.md)
- [Technical intent](../specs/technical-intent-spec.md)

