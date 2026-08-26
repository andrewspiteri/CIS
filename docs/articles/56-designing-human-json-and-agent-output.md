---
title: "Designing Human, JSON, and Agent Output"
type: article
status: Draft
series: "Building Change Impact Studio"
series_order: 6
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on CLI-output change
summary: "One command can support readable terminal use, stable automation, and compact agent routing without mixing diagnostics into stdout."
cis:
  stable_id: change-impact-studio:article:human-json-agent-output
---

# Designing human, JSON, and agent output

A governance CLI serves several consumers. Humans need readable summaries, scripts need
stable structure, and agents need bounded results with actionable identity and evidence.

## Human output explains the outcome

Human format prioritizes status, important changes, warnings, errors, and next actions.
It can use tables and narrative without being the automation contract.

## JSON preserves the complete structure

JSON uses stable field names and explicit arrays for tools that need the whole result.
Adding presentation text does not require downstream parsing changes.

## Agent output is compact and line-oriented

Agent format exposes summary counts followed by bounded records such as component,
finding, target, warning, collision, or error. Omitted-item counts prevent truncation
from becoming invisible.

## Separate stdout and stderr

Structured result data stays on standard output. Runtime diagnostics, build noise, and
troubleshooting messages use standard error so pipes and callers receive parseable data.

## Exit codes are part of the contract

Invalid input, confirmation required, collision, and validation failure need distinct
stable codes. Callers should not infer state from message wording.

## Takeaway

Design outputs for their consumers while preserving one command behavior. Keep complete
JSON, readable human summaries, compact agent routing, clean stdout, diagnostics on
stderr, and stable exit codes.

## Canonical CIS sources

- [CLI and repository initialization](../specs/cli-and-repository-initialisation-spec.md)
- [Technical intent](../specs/technical-intent-spec.md)
- [`cis host modules`](../manual/cis_host_modules.md)

