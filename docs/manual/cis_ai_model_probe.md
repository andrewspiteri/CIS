---
title: "cis ai model probe"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ai-model-probe
---

# `cis ai model probe`

Execute a real provider text-generation probe and retain hashed local evidence.

```text
cis ai model probe --provider <name> --model <name> [--allow-remote] [--repo <path>] [--format <human|json|agent>]
```

A provider health response alone does not pass this command. Remote execution requires explicit authorization. Exit code `0` means the deterministic probe passed; `4` means unavailable, conflicted, unauthorized, or failed.
