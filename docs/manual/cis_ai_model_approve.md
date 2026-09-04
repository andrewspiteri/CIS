---
title: "cis ai model approve"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-ai-model-approve
---

# `cis ai model approve`

Record human approval of a provider/model for one task class.

```text
cis ai model approve --provider <name> --model <name> --task-class <class> --reviewer <name> --reason <text> --yes [--repo <path>] [--format <human|json|agent>]
```

The command fails unless passed runtime probe, benchmark, and prompt-regression evidence exists. It updates the canonical model registry only after explicit confirmation. Exit code `0` means approval was recorded; `4` means authority, confirmation, or evidence is incomplete.
