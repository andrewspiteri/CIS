---
title: "External Tracker Synchronization"
type: specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-14"
review_cadence: "on tracker synchronization change"
cis:
  stable_id: change-impact-studio:spec:external-tracker-synchronization
---

# External tracker synchronization

## 1. Purpose and authority

CIS task documents under a change dossier are canonical. GitHub Issues, Jira work
items, and extension trackers are coordination mirrors. A remote edit, transition,
closure, assignee, label, milestone, or comment never approves a CIS plan or design,
accepts risk or deferral, proves completion, or closes a CIS change.

Only explicit CIS lifecycle commands and their required human authority may change
those decisions. Synchronization may project fields out and detect incoming drift;
it may not silently translate remote state into canonical authority.

## 2. Stable identity and projection

Every remote body carries the repository ID, change ID, task ID, and canonical
repository-relative task path. The triple is immutable. Remote numeric keys and URLs
are locators, not CIS identities.

The deterministic projection contains the task title, objective, required changes and
outputs, constraints, context, dependencies and gates, acceptance criteria, targeted
validation, and deferrals/residual risk. Completion evidence and synchronization
tables are not recursively projected. A SHA-256 digest covers the projected title,
body, and normalized labels.

Each canonical task preserves a durable `External issue links` row with provider,
remote ID and URL, canonical and remote digests, synchronization time, and link state.
This permits recovery after `.cis/local/` is deleted. Disposable three-way state and
conflicts live under:

```text
.cis/local/trackers/
  state.json
  conflicts.json
```

## 3. Provider profile and registration

`docs/references/external-tracker-profile.md` selects provider instances. `Provider`
is the repository-local instance key; `Kind` is the key registered by a loaded
`ICisTrackerProvider` assembly. Duplicate kinds are rejected. GitHub and Jira starter
rows are disabled until their exact target, field/status mappings, credential source,
and dry run have been reviewed.

Credentials never appear in Markdown, local derived state, diagnostics, logs, issue
bodies, or command output. Providers obtain them from the reviewed environment or
credential store and use least privilege. Missing credentials, offline state,
authorization failures, and rate limits return an unavailable/failure result while
preserving the last healthy state.

## 4. Authority and direction matrix

| Field or decision | Canonical authority | Permitted synchronization |
|---|---|---|
| Repository/change/task identity | CIS | CIS to remote only |
| Title and projected body | CIS | CIS to remote; remote drift becomes conflict |
| Dependencies, criteria, gates, evidence, deferrals | CIS | CIS to remote; never pulled automatically |
| Plan/design approval, risk acceptance, final acceptance | Human through CIS | Never inferred or mapped from remote |
| CIS lifecycle/task completion | CIS governed command | Remote status may mirror after CIS transition |
| Remote ID and URL | Provider | Recorded in canonical link table |
| Assignee, labels, milestone | Profile-defined | Detected and reconciled; no implicit CIS authority |
| Remote comments | Provider | Not canonical completion evidence without explicit reviewed import |

The initial direction is `cis-to-remote`. A later bidirectional adapter may add
explicitly remote-owned coordination fields, but it cannot weaken the authority rows.

## 5. Three-way reconciliation

Comparison uses the last synchronized canonical and remote digests:

| Current condition | Action |
|---|---|
| No durable mapping | Propose `create`. |
| Neither digest changed | `unchanged`; perform no write. |
| Canonical changed only | Propose `update` of the remote mirror. |
| Remote changed only | Persist `remote-changed`; never rewrite canonical Markdown. |
| Both changed | Persist `both-changed`; push must not overwrite it. |
| Remote item missing | Persist `remote-deleted`; never recreate or delete canonical work automatically. |
| Provider unavailable | Report unavailable; preserve mapping and last healthy baseline. |

Conflict identity is stable for provider, change, task, and conflict kind. `resolve`
requires a human reviewer and rationale. `cis` schedules the canonical projection for
a reviewed push; `remote` explicitly acknowledges a remote divergence without granting
it CIS authority; `unlink` retains the audit row and stops synchronization. Decisions
are appended to the canonical task.

## 6. Retry, deletion, and concurrency

Reads and updates may be retried according to provider rate-limit guidance. Create
retry requires an idempotency mechanism or recovery by embedded CIS identity; an
adapter must not create duplicates after an ambiguous response. Backoff preserves
provider retry timestamps and does not busy-loop. Deletes are never propagated in
either direction automatically. Optimistic concurrency tokens should be used when a
provider exposes them; a rejected conditional update becomes a conflict.

## 7. Commands

- `cis tracker plan`: read-only projection/diff; no canonical, local-state, or remote write.
- `cis tracker push`: create/update non-conflicting mirrors and preserve durable links.
- `cis tracker pull`: read remote state and persist conflicts; no canonical task rewrite.
- `cis tracker status`: report profiles, mappings, and conflict state without a remote write.
- `cis tracker resolve`: record an explicit reviewed conflict disposition.

All commands accept the initialized repository. Plan, push, and pull are bounded to an
exact change and optionally one provider. Output must omit secrets and remote body
contents.

## 8. GitHub and Jira mapping contract

GitHub maps task title/body to issue title/body and may map CIS lifecycle to open/closed
only after the canonical transition. Jira maps title to summary and the projected body
to the server-supported description format; status changes require reviewed transition
IDs. Repository profiles own target repository/project, issue type, status mappings,
labels, custom fields, and coordination-field direction.

The built-in `Cis.Providers.Tracker.GitHub` assembly maps the neutral contract to the
GitHub REST Issues API. It requires an `owner/repository` target and an
`environment:<variable>` credential source. The built-in
`Cis.Providers.Tracker.Jira` assembly maps the contract to Jira Cloud REST API v3,
uses Atlassian document format for descriptions, and requires
`basic-environment:<email-variable>:<token-variable>`. Both are explicitly loaded,
disabled by default in initialized profiles, and covered by offline HTTP contract tests.
Provider-specific capabilities cannot change canonical authority or conflict semantics.

## 9. Acceptance

- Creation and update are idempotent against stable identity and digests.
- Local-state deletion does not lose remote identity.
- Concurrent and remote-only changes remain visible until reviewed.
- Remote deletion never cascades.
- Credentials and remote content are excluded from local feedback records.
- Human resolution records reviewer, timestamp, rationale, and disposition.
- Re-importing a feature specification preserves external links and decisions.
