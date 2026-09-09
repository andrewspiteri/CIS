---
title: "Governing Local and Remote AI Routes"
type: article
status: Draft
series: "Human and Agent Execution"
series_order: 6
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
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

The model registry and qualification workflow keep availability, probe results,
capability benchmarks, approval identity, and freshness visible. A configured model is
not automatically qualified for every route, and a previously approved result cannot be
silently applied to a different provider or model revision.

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

## Separate capability, provider, and authorization

A capability describes the work needed, such as summarizing a bounded failure or
reviewing possible skill overlap. A provider and model describe one mechanism that may be
qualified to perform it. Authorization describes whether the selected content may be
sent through that route now.

Conflating those layers produces brittle policy. “Use model X” says nothing about whether
X is available, qualified for the task, local or remote, or permitted to receive the
source set.

## Explain every selected route

A route explanation should identify the requested capability, eligible candidates,
qualification status, local or remote classification, cache policy, sensitivity checks,
and why alternatives were rejected. This is especially important when automatic local
selection chooses a smaller model.

Selection evidence should be reproducible against the current registry. A model that was
qualified months earlier may be stale after a version, runtime, benchmark, or policy
change.

## Remote permission is invocation-specific

Suppose a remote provider is allowed for documentation summarization. That canonical
policy is necessary but insufficient. The user must still authorize the concrete source
set for the invocation. Adding a diagnostic excerpt or configuration file changes the
disclosure and requires another decision.

This prevents a one-time configuration choice from becoming permanent consent to transmit
future repository content. It also gives maintainers a chance to remove unnecessary or
sensitive sources before execution.

## Prefer rejection to optimistic redaction

Redaction can reduce incidental exposure in bounded logs. It is not a safe gateway for
known secrets, keys, credentials, certificates, or explicitly sensitive sources. Such
content is invalid input and should stop the route.

The caller can select a different evidence source, use a local provider, or resolve the
sensitivity under repository policy. Silent substitution would make it impossible to
know what the model actually received.

## Cache by exact identity

A reusable result must bind capability, provider, model, route configuration, source
digests, and relevant parameters. A cache hit for “summarize this file” is unsafe after
the file or approved route changes.

Cached responses remain derived and disposable. If a summary establishes durable product
meaning, a human must review and promote the conclusion into the appropriate canonical
document. Committing the cache would confuse performance state with authority.

## Observe usage without rebuilding the prompt

Operational evidence can record timings, provider and model identity, estimated tokens,
cache use, outcome, and failure class. It should exclude prompt, response, source, option
values, and secrets. The aim is to improve route reliability and cost—not to create a
shadow content store.

## Fail closed without making local work impossible

If no qualified route is available, CIS should report why and preserve deterministic
evidence already gathered. The user can select another provider, qualify a model, reduce
the capability, or proceed manually. The system must not escalate to remote use merely
to avoid an inconvenient failure.

## Takeaway

Govern models through named capabilities, local-first selection, explicit remote
authorization, sensitive-input rejection, disposable caching, and content-free usage
evidence. Provider availability should never decide privacy policy.

## Canonical CIS sources

- [AI routing profile](../references/ai-routing-profile.md)
- [`cis ai routes`](../manual/cis_ai_routes.md)
- [`cis ai usage`](../manual/cis_ai_usage.md)
- [Technical intent](../specs/technical-intent-spec.md)
- [AI model qualification](../specs/ai-model-qualification-spec.md)
- [`cis ai route explain`](../manual/cis_ai_route_explain.md)
