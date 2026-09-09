---
title: "cis agent discover technical-intent"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-07"
review_cadence: "on command or provider-policy change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-discover-technical-intent
---

# `cis agent discover technical-intent`

Prepare a review-only technical scaffold, evidence-derived questionnaire and cached
implementation disclosure preview without contacting a provider. The canonical BRD and
human decisions remain unchanged. This command accepts the same owned repository selection
as [technical-intent authoring](cis_agent_author_technical_intent.md).

```text
cis agent discover technical-intent --reference <owned-repo> [--reference <owned-repo>]...
  --actor <human> --repo <authority> [--format <human|json|agent>]
```

Before freezing implementation evidence, discovery refreshes observed dictionaries in owned
repositories, consolidates them into the authority with repository attribution, and refreshes
their graphs. Reviewed rows and decisions are preserved. Direct technical-intent authoring
performs the same preparation.

The JSON envelope lists all prospective artifacts and exact implementation snapshots at
`.cis/local/agents/implementation/technical-intent-selection.json`. Review the selection
before provider disclosure. Discovery and authoring share a technical-intent execution lock.
