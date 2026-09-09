---
title: "Designing Human, JSON, and Agent Output"
type: article
status: Draft
series: "Building Change Impact Studio"
series_order: 6
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
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

An exit code of zero is not enough when a caller requested structured output. Missing,
malformed, partial, stale, or oversized JSON remains a distinct client error rather than
being interpreted from nearby console prose.

## Agent output is compact and line-oriented

Agent format exposes summary counts followed by bounded records such as component,
finding, target, warning, collision, or error. Omitted-item counts prevent truncation
from becoming invisible.

## Separate stdout and stderr

Structured result data stays on standard output. Runtime diagnostics, build noise, and
troubleshooting messages use standard error so pipes and callers receive parseable data.
Optional local performance tracing follows the same boundary.

## Exit codes are part of the contract

Invalid input, confirmation required, collision, and validation failure need distinct
stable codes. Callers should not infer state from message wording.

## One behavior, three projections

The command should compute one domain result and render it for each consumer. Human output
may group and explain. JSON preserves the complete typed structure. Agent output prioritizes
identity, counts, bounded records, and next actions. Separate code paths that recompute
behavior risk producing different truth.

| Consumer | Primary need | Common failure to avoid |
|---|---|---|
| Human | Meaningful summary and recovery action | Hiding exact identities behind prose |
| Script or client | Stable complete schema | Mixing logs into standard output |
| Agent | Compact bounded routing and actionable state | Silent omission or unbounded dumps |

## Make boundedness explicit

Large inventories and graphs need limits. Agent and human formats should report total,
returned, and omitted counts and provide a safe way to narrow or page. JSON may expose the
complete result only when the command contract permits it; a caller should never mistake
truncation for absence.

Diagnostics also need bounds. Full provider or build streams can live in retained local
artifacts while standard error presents useful current progress and references.

## Treat errors as structured outcomes

Invalid input, confirmation required, collision, stale state, unavailable capability,
invalid evidence, and internal failure require different recovery. Stable exit codes let
callers branch without scraping English text, while JSON error fields carry exact IDs,
paths, and suggestions.

A structured-output request that receives malformed or missing JSON is a client failure
even if the process exits zero. The consumer cannot safely infer a result from surrounding
messages.

## Keep progress away from the result

Long-running graph builds and agent runs need progress. Stream normalized events or
diagnostics to standard error and write one final result to standard output. This keeps
pipes, MCP adapters, and the extension reliable without making users wait in silence.

## Evolve schemas deliberately

Adding optional fields can be compatible; renaming meanings, changing enum values, or
removing fields may break clients. Output contracts deserve versions, fixtures, and
cross-client tests. Human wording can evolve more freely because automation should not
parse it.

## Test all formats

Golden or structural tests should cover success, empty, warning, confirmation, collision,
validation failure, truncation, and unexpected exception cases. The installed-tool smoke
test confirms that packaged commands keep the same stdout, stderr, and exit behavior.

## Include identity in every format

A readable result should still name the repository, workspace, product, change, task,
baseline, or run identity needed to act safely. Compact output that says only “3 findings”
forces the next consumer to guess which analysis produced them.

For mutation commands, report planned, applied, retained, quarantined, collision, warning,
and error counts consistently. Human convenience should never hide whether the command
changed canonical state.

## Takeaway

Design outputs for their consumers while preserving one command behavior. Keep complete
JSON, readable human summaries, compact agent routing, clean stdout, diagnostics on
stderr, and stable exit codes.

## Canonical CIS sources

- [CLI and repository initialization](../specs/cli-and-repository-initialisation-spec.md)
- [Technical intent](../specs/technical-intent-spec.md)
- [`cis host modules`](../manual/cis_host_modules.md)
