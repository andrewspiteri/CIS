---
title: "CIS Tool Usage Feedback Loop"
type: repository-specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on change"
cis:
  stable_id: change-impact-studio:spec:feedback-loop
---

# CIS tool usage feedback loop

## Purpose

CIS records local evidence for every CLI invocation that can be associated with an
initialized repository or workspace. The evidence supports a feedback loop over
command reliability, latency, output size, deterministic/model routing, and possible
agent-token savings without making the ledger canonical repository documentation.

PARR's append-only usage ledger is the baseline. CIS generalizes it to every module,
sanitizes inputs at the host boundary, and requires a calculation basis and confidence
for savings estimates.

## Storage and privacy

The append-only ledger is `.cis/local/feedback/tool-usage.jsonl`. It is derived,
disposable, local-only state and remains excluded by `.cis/.gitignore`.

Each record contains the command path, option names, timestamps, elapsed time, exit
code, stdout/stderr character counts, estimated output tokens, and savings fields.
It must not contain option values, command output, prompts, source content, secrets,
credentials, or model API keys. Failure to write telemetry must never alter the
underlying command result.

## Token estimates

The default estimate uses four characters per token. It is a directional heuristic,
not a provider invoice or exact tokenizer result.

Every invocation has numeric baseline, actual, and possible-savings fields. When no
defensible counterfactual exists, baseline equals actual output and possible savings
is zero with confidence `none`. A command may register a stronger estimate:

- `index find` compares its compact routing output with the estimated contents of the
  matched source files (`medium` confidence);
- `context pack` compares included excerpts with the complete selected sources
  (`high` confidence);
- `agent runs --summary` compares its bounded routing projection with the selected full
  run manifests (`high` confidence).

Possible savings are `max(0, baseline - actual)`. Summaries must retain estimation
coverage so estimated and unestimated commands cannot be confused.

## Feedback commands

- `cis feedback summary` aggregates outcomes, elapsed time, output estimates, possible
  savings, estimation coverage, and per-command totals.
- `cis feedback usage` lists recent sanitized entries.
- `cis feedback opportunities` identifies repeated failures and high-output commands
  lacking compact output or a defensible counterfactual.

Reporting commands are themselves logged after their output completes, so a report
does not include its own current invocation.

Ledger schema 2 classifies successful execution, invalid requests, governed blocks,
Repository Doctor finding-bearing results, cancellation, and execution failure. Legacy
schema-1 entries remain readable and are classified conservatively from command and exit
code. A non-zero governance result must not automatically be described as a tool failure.

Opportunity analysis defaults to a rolling 24-hour window unless `--since` is supplied.
Output opportunities use average, 95th-percentile, and maximum estimated output per
invocation; lifetime aggregate output alone is not a compaction signal. Repeated
non-success is reported only while the latest bounded sample remains unresolved.
Reporting commands are excluded from their own opportunity analysis. Adjacent repeated
reads within two seconds are reported as duplicate-query candidates for shared in-flight
projection caching. Full and `--summary` command evidence is scored separately so a compact
projection cannot conceal an oversized full-detail invocation, or vice versa.

The host serializes concurrent appends with a bounded local lock. Once the disposable
ledger reaches 16 MiB, it retains at most the latest 25,000 valid entries from the last
30 days. Malformed derived lines are omitted during compaction and never become canonical
evidence.

When a governed task transitions to Complete, CIS snapshots the current sanitized
ledger into the task completion-evidence table and `verification.md`. The snapshot
records invocation/failure counts, aggregate possible token savings, and a digest of
invocation identities and outcomes. It does not copy arguments, output, prompts, or
source content. The local JSONL remains the detailed derived record.

## Acceptance criteria

- The production host logs successful, failed, read-only, and mutating commands at one
  host boundary.
- Argument values and output content do not enter the ledger.
- Telemetry failure cannot change command output or exit status.
- Savings claims include basis and confidence; unestimated commands claim zero.
- Opportunity output states its effective evidence window and per-invocation basis.
- Expected governed findings are distinct from command execution failures.
- Concurrent writes and bounded retention cannot change the underlying command result.
- Repository Doctor reports whether the ledger exists.
- Repository initialization seeds an agent skill and instruction for the feedback loop.
- Task completion preserves a canonical aggregate/digest without promoting the local
  telemetry ledger to source-of-truth status.
