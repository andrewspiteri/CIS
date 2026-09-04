---
title: "cis references source accept"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-30"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-references-source-accept
---

# `cis references source accept`

Accept a reviewed source revision as the registry baseline without rewriting the BRD.

```text
cis references source accept <BRD-SRC-ID> --actor <identity> --reason <rationale> [--yes] [--repo <path>] [--format <human|json|agent>]
```

Without `--yes`, CIS returns a confirmation-required preview. Acceptance changes only the registry digest and review provenance. Reconcile and review the BRD separately when cited meaning changed.
