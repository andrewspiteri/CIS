---
title: "cis verify finalize"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-23"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-verify-finalize
---

# `cis verify finalize`

Complete the standard delivery lifecycle and record explicit human outcome acceptance.

```text
cis verify finalize <change-id> --reviewer <identity> --reason <rationale> [--repo <path>] [--format human|json|agent]
```

The command first validates the current non-empty workspace-aware snapshot, deterministic evidence, approved plan and design gates, and task dispositions. It then:

1. completes the final delivery sweep;
2. completes the coordination scope guard;
3. closes the change dossier;
4. recaptures changed tracked and untracked files across every registered workspace repository; and
5. records reviewer, rationale, timestamp, and snapshot digest in `verification.md`.

Failure before the lifecycle mutations leaves the dossier unchanged. Human identity and rationale remain mandatory; the command does not infer approval from test results.
