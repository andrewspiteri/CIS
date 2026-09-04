---
title: "AI Model Qualification"
type: technical-specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on provider, model, prompt, dataset, routing, privacy, or approval-policy change"
cis:
  stable_id: change-impact-studio:spec:ai-model-qualification
---

# AI model qualification

## Purpose

CIS separates model availability from governed suitability. A model may be used for a task class only after a real capability probe, deterministic benchmark, runtime prompt regression, and explicit human approval.

## Canonical and derived state

- `references/ai-routing-profile.md` declares capability routes and remote/cache policy.
- `references/ai-model-registry.md` records human task-class approvals.
- `references/ai-evaluation-datasets/*.json` contains reviewable prompt-regression cases.
- `.cis/local/ai/qualification/` contains disposable runtime evidence.

Qualification evidence records provider, model, task class, timestamp, profile and dataset digests, latency, deterministic case outcomes, missing expected terms, and output hashes. It never stores generated content.

## Required lifecycle

1. A provider-name collision fails closed.
2. A runtime probe must return the declared probe token.
3. The built-in text, JSON, and instruction-following benchmark must pass.
4. Every repository-owned regression case must pass deterministic required-term scoring.
5. A named human reviewer supplies a rationale and explicit confirmation.
6. Route explanation reports canonical selection, remote authorization, resolved model, and task-class approval.

Provider health, simulated output, static dataset validation, cached generation, and model self-assessment cannot satisfy approval. Remote execution additionally requires explicit authorization for the exact submitted prompts.

## Change and invalidation

Provider, model, prompt, dataset, route, privacy, or policy changes require requalification. Established deterministic expectations may be strengthened, but must not be weakened solely to obtain a pass.
