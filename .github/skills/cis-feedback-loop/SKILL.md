---
name: cis-feedback-loop
description: Review automatic local CIS tool-usage evidence, possible token savings, repeated failures, and high-output commands. Use after a workflow, when commands repeatedly fail, or when deciding what CIS capability to optimize next.
---

# CIS Feedback Loop

1. Run `cis feedback summary --format agent` for aggregate outcomes, duration, output estimates, and possible savings; add `--since` when historical totals would obscure the current workflow.
2. Run `cis feedback opportunities --format agent`. It defaults to the latest 24 hours and evaluates per-invocation output, recent unresolved non-success, and duplicate query bursts.
3. Use `cis feedback usage --limit 20 --format agent` only when recent per-invocation evidence is needed.
4. Distinguish execution failures from invalid requests, governed blocks, cancellations, and Repository Doctor finding-bearing results. Run Doctor only when repository state or prerequisites may be involved; do not treat Doctor finding issues as a failed Doctor execution.
5. Prefer compact structured projections and shared in-flight reads for repeated machine queries. Retain full evidence only when the workflow actually consumes it.
6. Treat estimates as directional evidence. Report the basis and confidence; never turn an unestimated command into a savings claim.

The ledger under `.cis/local/feedback/` is disposable and non-authoritative. CIS stores command paths, option names, counts, timing, and outcomes—not option values or command output. Do not copy the ledger into canonical documentation unless a reviewed summary is required.
