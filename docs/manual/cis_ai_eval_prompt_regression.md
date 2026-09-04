---
title: "cis ai eval prompt-regression"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ai-eval-prompt-regression
---

# `cis ai eval prompt-regression`

Execute a repository-owned JSON regression dataset against a real model.

```text
cis ai eval prompt-regression --provider <name> --model <name> --task-class <class> --dataset <repository-relative.md-or-json> [--allow-remote] [--repo <path>] [--format <human|json|agent>]
```

Markdown is canonical and JSON remains supported for imported fixtures. The path must remain inside the repository and its declared task class must match. Deterministic required-term checks score each case. Exit code `0` means all cases passed; `4` means invalid input, unavailable execution, or regression failure.
