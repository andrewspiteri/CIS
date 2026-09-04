---
title: "cis agent revalidate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-30"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-revalidate
---

# `cis agent revalidate`

Revalidate a retained read-only Claude BRD review that legacy CIS marked `InvalidEvidence` solely because a valid streaming telemetry event exceeded the 64 KiB raw-retention bound.

```text
cis agent revalidate <run-id> --actor <identity> --reason <rationale> [--repo <path>] [--format <human|json|agent>]
```

The command does not contact the provider or rerun the review. It requires the original envelope and artifact hashes, an unchanged canonical BRD, an unchanged read-only workspace, a valid structured review result, a successful Claude result event, and no invalid event other than the corrected legacy size message. It preserves the original failure event, appends the actor and rationale, regenerates the advisory review Markdown, and refreshes artifact hashes.

Use a fresh independent review when any governed input changed or when the retained evidence fails one of these checks.
