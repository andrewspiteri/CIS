---
title: "cis feedback usage"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-05"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-feedback-usage
---

# `cis feedback usage`

Lists recent entries from `.cis/local/feedback/tool-usage.jsonl`.

```text
cis feedback usage [--repo <path>] [--since <30m|12h|7d>]
  [--limit <1-10000>] [--details] [--format <human|json|agent>]
```

Entries expose command paths, option names, outcome, timing, output counts, and token
estimates. Option values and command output are never stored. The current reporting
invocation is appended only after its result is produced. Exit codes follow
`cis feedback summary`.

New entries use ledger schema 3 and record a sanitized outcome classification. Schema-1
and schema-2 entries remain readable. Concurrent appends are serialized locally; after the ledger
reaches 16 MiB, disposable history is bounded to 30 days and 25,000 valid entries.

Schema 2 also supports optional `buildIdentity`. JSON entries expose
`outputExpansionEstimatedTokens` as the positive difference between actual and
baseline estimates. Older entries without build identity remain readable. Commands
may supply an explicit bounded outcome: parser errors are invalid requests, plan
blockers remain blocked, and execution exceptions are failures. Other commands retain
the documented exit-code fallback.

Savings candidates must identify the same command as the recorded invocation; a
nested command estimate cannot describe an entire workspace snapshot. Estimated
output expansion and possible savings remain estimates, not measured model usage.

## Optional invocation observations

Use `--details --limit 10` to inspect attribution in human or agent output. JSON
includes these fields without `--details`. Ordinary human and agent output retain
their existing compact fields.

An instrumented invocation records its full parsed `operation`, such as
`plan task transition`, separately from the legacy `command` grouping. The
invocation identity exists before command dispatch; nested instrumented invocations
can retain their parent identity. Validated change/task IDs identify the work when
the executing integration supplies them. Raw positional values, option values,
command output and environment values are excluded.

New command groupings and option names come from recognized parser symbols, rather
than positional arguments or unknown option tokens. Only explicit recognized options
are retained, with at most 32 distinct names of at most 64 characters. Excess names
contribute to `droppedObservations` when invocation observations are available. Direct
recorder calls without canonical parser metadata record `unknown` and an empty option
list. Existing historical rows remain readable.

Optional observations include inclusive phase durations and counts, graph consumer
and snapshot identity, reuse/refresh outcome and reason, verification scope and
selected/executed/reused/invalidated batch counts, and stable blocker codes. Phase
durations can overlap and must not be added to command time as extra elapsed work.
Existing low-level performance traces aggregate into `scan`, `identity`, `graph`,
`evidence`, `native`, `render` or `other`; distinct reader names and paths cannot
consume the ledger's phase capacity. Explicit integration phase scopes retain their
bounded names. Detailed opt-in stderr traces remain available with `CIS_PERF_TRACE=1`.
Scope and reason fields are bounded identifiers, not arbitrary diagnostic text.
Each observation kind retains at most 16 entries; duplicate graph, verification and
blocker observations are collapsed. `droppedObservations` reports invalid or excess
observations rather than silently presenting the retained list as complete.

`standardOutputUtf8Bytes` and `standardErrorUtf8Bytes` measure generated UTF-8
output; existing character counts and four-character token estimates are unchanged.
`deliveredContextUtf8Bytes` measures complete context envelopes at delivery boundaries
the integration can observe, including their metadata and diagnostics. Multiple
delivered envelopes add to that count. Internal intermediate projections are not
additional deliveries. These fields do not measure model consumption or billed
tokens. Missing historical or uninstrumented values remain unknown, rather than zero.
Observations are disposable bookkeeping and must not enter native material-input
identities or replace execution reports, evidence validity or work authority.
