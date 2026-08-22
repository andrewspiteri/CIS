---
title: "Change Impact Studio Implementation Roadmap"
type: specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on stage completion or scope change"
cis:
  stable_id: change-impact-studio:spec:implementation-roadmap
---

# Change Impact Studio implementation roadmap

## Purpose

This roadmap is the canonical completion contract for the ten-stage CIS implementation.
It prevents conversational stage labels from drifting and ties each stage to executable
outcomes. A stage is complete only when its commands, repository initialization,
documentation, agent guidance, focused tests, and applicable end-to-end checks pass.

## Stages

| Stage | Capability | Completion state |
|---|---|---|
| 1 | API discovery, governance, compatibility, graph integration, and public cache boundary | Complete |
| 2 | Provider-neutral external tracker projection, three-way reconciliation, and conflict authority | Complete |
| 3 | Separately loaded GitHub Issues and Jira Cloud transport providers | Complete |
| 4 | AI routing/provider/model policy, usage/evaluation/cache visibility, and deterministic generation | Complete |
| 5 | Deterministic workflow definitions, resumable runs, logs, and summaries | Complete |
| 6 | Provider-neutral agent task envelopes and governed result ingestion | Complete |
| 7 | Verification baselines, planned-versus-actual comparison, evidence, and human acceptance | Complete |
| 8 | Runtime diagnostics and governed learning proposals/history | Complete |
| 9 | Thin Visual Studio Code client over stable CLI JSON and canonical Markdown | Complete |
| 10 | Cross-module hardening, packaging, end-to-end release validation, and final gap closure | Complete |

## Authority

Markdown remains canonical. Derived state lives under `.cis/local/`. External trackers,
models, agents, diagnostics, and the editor client may propose or project information;
none may infer approval, completion, risk acceptance, or final acceptance.
