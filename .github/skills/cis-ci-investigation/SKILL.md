---
name: cis-ci-investigation
description: Inspect remote CI checks, runs, jobs, redacted logs, artifacts, and failure classifications before proposing a focused fix or explicitly confirmed rerun.
---

# CIS CI investigation

1. Resolve the exact repository and PR or run identity; do not investigate an inferred target without checking it.
2. Use `cis ci status --pr <number>` or bounded `cis ci runs` before fetching logs.
3. Inspect jobs and artifact metadata. Download logs only for the failed jobs needed for diagnosis.
4. Use `cis ci diagnose --run <id>` and preserve the first-failure classification and evidence path.
5. Use `cis ci reproduce --run <id>` to select a focused repository-owned local workflow.
6. Treat remote failures as evidence, not as permission to change scope, tests, gates, or policy.
7. `cis ci rerun-failed --run <id> --yes` is a remote mutation and requires explicit authorization.

Never print credentials, upload local evidence, erase the first failing attempt, or claim a rerun passed before retrieving its new result.
