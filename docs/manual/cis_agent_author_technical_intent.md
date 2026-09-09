---
title: "cis agent author technical-intent"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-07"
review_cadence: "on command or provider-policy change"
cis:
  stable_id: change-impact-studio:manual:cis-agent-author-technical-intent
---

# `cis agent author technical-intent`

Infer a review-only technical intent for an existing product from selected owned
implementation and test repositories, the current BRD, and the technical questionnaire.
The definition wizard exposes **Technical direction → Infer from existing repositories**.

```text
cis agent discover technical-intent --reference <owned-repo> [--reference <owned-repo>]...
  --actor <human> --repo <authority> --format json

cis agent author technical-intent --reference <owned-repo> [--reference <owned-repo>]...
  --provider <id> --actor <human> --repo <authority>
  [--transport <name>] [--timeout-seconds <30..86400>] [--approve-requests]
  [--format <human|json|agent>]
```

Discovery is local-only. It prepares the Draft scaffold, a questionnaire with derived
facts and unresolved questions, and `.cis/local/agents/implementation/technical-intent-selection.json`.
It first refreshes discovered Draft dictionaries in owned repositories and the authority,
with repository attribution, then refreshes their graphs before capturing evidence digests.
It does not contact a provider or edit/approve the BRD. Authoring prepares the same inputs
and passes them to the explicitly selected provider in an isolated scratch repository.

Draft discovery can proceed while the business baseline is under review or the technical
questionnaire is incomplete. It requires a canonical BRD, an owned classified implementation,
and fresh participant graphs. It never changes the ordinary initialization, answer, approval
or downstream gates. Active technical intent is protected from this drafting command.

The provider reads implementation bodies and related tests before writing a connected
technical narrative: actual modules, responsibility and dependency boundaries, cross-repository
flows, data and consistency, contracts, trust/security, jobs, failure handling, operations,
and verification gaps. Observed implementation, proposed direction and unavailable evidence
remain distinct. Current code is not an approved future architecture.

Snapshots use the BRD discovery cache, redaction, bounded selection and immutable digests.
They exclude manifests, deployment configuration, migrations, dependencies, generated files
and sensitive paths. Deployment and operational claims therefore remain bounded by the
available source bodies; file presence does not prove deployed behavior or successful tests.
Every required area needs hidden coverage evidence or a concrete gap.

Copy-back permits exactly one file and rejects changed canonical/context/snapshot inputs,
altered frontmatter or protected evidence, missing coverage, visible source citations, removed
architecture structure, and agent-selected or deferred decisions. Controller-owned provenance
is hidden in reversible comments. Decisions remain visible. Subsequent initialization preserves
the implementation-authored module architecture, interactions and decision records.

Failed drafts can use `cis agent resume <run-id>` while their original envelope, canonical
input, context and snapshots remain unchanged. Resumption applies the same copy-back checks.
Review and resolve human choices through CIS before approval; rebuild the graph after drafting.
