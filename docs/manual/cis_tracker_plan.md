---
title: "cis tracker plan"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-tracker-plan
---

# `cis tracker plan`

Previews external issue creates, updates, unchanged mappings, unavailable providers,
and conflicts for an exact change. It performs no canonical, local-state, or remote
write.

```text
cis tracker plan <change-id> [--provider <key>] [--repo <path>]
  [--format <human|json|agent>]
```

The provider key comes from `docs/references/external-tracker-profile.md`. Exit `5`
means drift conflicts need human review; exit `4` means the configured provider or
remote read is unavailable. Run this command before every `push`.

