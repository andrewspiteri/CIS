---
name: cis-feedback-loop
description: Review automatic local CIS tool-usage evidence, possible token savings, repeated failures, and high-output commands. Use after a workflow, when commands repeatedly fail, or when deciding what CIS capability to optimize next.
---

# CIS Feedback Loop

1. Run `cis feedback summary --format agent` for aggregate outcomes, duration, output estimates, and possible savings.
2. Run `cis feedback opportunities --format agent` to locate repeated failures and commands that need compact output or a defensible estimator.
3. Use `cis feedback usage --limit 20 --format agent` only when recent per-invocation evidence is needed.
4. If a repository-aware command repeatedly fails, run `cis repo doctor` before retrying it.
5. Treat estimates as directional evidence. Report the basis and confidence; never turn an unestimated command into a savings claim.

The ledger under `.cis/local/feedback/` is disposable and non-authoritative. CIS stores command paths, option names, counts, timing, and outcomes—not option values or command output. Do not copy the ledger into canonical documentation unless a reviewed summary is required.
