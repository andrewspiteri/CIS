---
title: "cis test mutation assess"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-26"
review_cadence: "on mutation assessment contract change"
cis:
  stable_id: change-impact-studio:manual:cis-test-mutation-assess
---

# `cis test mutation assess`

Assess a native Stryker JSON report without executing tests or changing the report.

```text
cis test mutation assess --report <path> --break <percent> [--repo <path>] [--format <human|json|agent>]
```

`--report` selects one bounded file within the repository. `--break` supplies the
required score from 0 through 100; preserve any stronger adopted policy. The command
returns zero for a qualifying assessment and four for findings or invalid evidence.

The conservative score is `Killed / (Killed + Survived + Timeout + NoCoverage) * 100`.
The summary separately exposes the native score, denominator and each disposition.
Timeouts receive no conservative kill credit. Timeouts, uncovered mutants, runtime
errors, pending or unknown outcomes prevent qualification. Compile errors and ignored
mutants are reported separately; they do not count as kills. Compare thresholds
using the unrounded score. Native kills do not need a custom assertion-message filter.

JSON output is a `TestSuiteExecution` with a mutation summary and a hash-bound native
report artifact. It assesses the reported mutants only: it does not certify source
freshness, complete production scope, process health or release approval. Check those
separately using the [mutation workflow](../workflows/mutation-assurance.md).

The repository runner preserves the original report and writes this JSON alongside
it as `<report>.assessment.json`. Reconciliation checks the sidecar's report hash and
recomputes the assessment; a stored `passed` flag or score is never authoritative.
Reports from a failed workflow remain failed execution evidence even when the
readable native report has useful mutant counts.
