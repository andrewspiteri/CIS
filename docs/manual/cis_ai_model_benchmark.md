---
title: "cis ai model benchmark"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ai-model-benchmark
---

# `cis ai model benchmark`

Run deterministic text, structured-JSON, and instruction-following cases for one task class.

```text
cis ai model benchmark --provider <name> --model <name> --task-class <class> [--allow-remote] [--repo <path>] [--format <human|json|agent>]
```

Evidence contains timing, outcomes, missing terms, and output hashes—not generated text. Exit code `0` means all cases passed; `4` means any case or prerequisite failed.
