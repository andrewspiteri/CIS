---
title: "cis agent prepare"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-28"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-prepare
---

# `cis agent prepare`

Prepare a digest-bound envelope for one task.

```text
cis agent prepare <change-id> <task-id> [--provider portable] [--summary] [--repo <path>] [--format <human|json|agent>]
```

`--provider` accepts `portable` or a discovered provider ID. Blocked tasks and downstream work behind an unapproved design gate fail closed; coordination, wireframe, and visual-design preparation remain eligible to establish that gate. The derived envelope is written beneath `.cis/local/agents/envelopes/`, binds the canonical task digest, and grants no execution or lifecycle authority.

When the Context module is present, preparation checks or refreshes the current graph and adds a bounded `taskContext` field rooted in the canonical task document. It uses the existing context-pack traversal to select connected requirements, constraints, components, contracts, source and recorded verification evidence. A linked test is a candidate check, not a passing result. Missing roots, missing modules, unresolved participant context, omitted content and stale or unavailable evidence remain explicit. The default advisory delivery budget is 16,000 characters and 64,000 UTF-8 bytes, including its metadata; the larger selection detail remains beneath `.cis/local/context/`.

Task preparation selects a narrow evidence traversal of up to four hops, 80 nodes and 25 expansions. It follows explicit task/criterion, artifact/check and recorded-result references, including their provenance; it does not expand broad component catalogues or call graphs. Ordinary `context pack` traversal keeps its existing default behavior. Reached recorded-result observations appear before source excerpts and distinguish the saved case outcome, receipt state, input-pin state and the need for current native verification. If no result is reached or the delivery budget omits it, the pack reports that uncertainty. A recorded pass does not establish full criterion coverage or satisfaction.

Canonical task front matter may select up to four additional advisory roots with one inline JSON field, for example `context_roots: ["example:change:cis-0001:task:work-001"]`. Each identifier is at most 256 ASCII letters, digits or `:._-#` characters and must resolve uniquely in the same repository graph. Paths and cross-repository selection are excluded. The existing context-pack multi-root path shares the same whole delivery budget; task mode divides its 80-node and 25-expansion limits across the roots. These references select context without making the active task implement another task's criteria or granting requirements, targets or execution authority. Invalid metadata or unresolved roots produce an explicit unavailable context. Parsing uses the already loaded canonical task text and preserves its exact bytes and digest.

The advisory pack never replaces `instructionMarkdown`, changes the canonical task digest or enters the authority artifact list. Providers receive it in a separate section labelled as derived evidence. Preparation does not grant execution permission.

The Agent boundary recomputes advisory delivery size and token estimates rather than trusting an optional provider's counters or advertised budget. Invalid or oversized provider output becomes an explicit unavailable context with a stable diagnostic code; it is never copied into the task instructions.

The advisory size fields cover the largest supported frame: compact DTO JSON, the actual nested pretty JSON value, or the separate provider prompt section with its heading and metadata. Canonical authority retains its separate existing bounds. For usage observations, JSON preparation counts only the actual advisory JSON value, including its metadata and formatting. Summary output reports zero delivered context because it omits the body; its bytes still count as generated output. A provider run observes the actual full prompt sent to that provider, including required authority and policy text. These byte observations are not measured model token usage.

`--summary` emits a compact JSON projection in every output format, bounded to 12,000 characters and 48,000 UTF-8 bytes. It reports task identity, state, application result, context freshness, detail references, blockers and omission counts. The saved envelope and ordinary `--format json` output retain the full existing records.
