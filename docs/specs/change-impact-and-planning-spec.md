---
title: "Change Impact and Bounded Planning"
type: specification
status: Draft
version: "0.1"
scope: "Product:ChangeImpactStudio"
owner: "Andrew Spiteri"
last_reviewed: "2026-08-23"
review_cadence: "on change-workflow change"
cis:
  stable_id: change-impact-studio:spec:change-impact-and-planning
---

# Change Impact and Bounded Planning

## 1. Purpose

This specification defines how CIS turns a proposed outcome into reviewed impact and
bounded work under an explicit authorized outcome.

The controlling principle is:

> One authorization of an outcome, scope and overall limits covers the necessary
> tasks. CIS preserves that human decision through bounded derivation and asks only
> for a specific material difference outside it.

## 2. Canonical dossier

Each change lives beneath the configured documentation root at `changes/CIS-0001/`.
Draft creation writes `proposal.md`, compact `creation-inputs.json` and append-only `events.jsonl`. It requires an
initialized repository and exact built baseline, but no preliminary approval.
Preparing a draft plan also grants no execution or product-baseline authority.

Generate other records only when their subjects apply:

| Record | Subject |
| --- | --- |
| `authorization.json` | Actual human outcome authorization, overall limits and delegation. |
| `plan.json`, `plan.md`, `agent-tasks/WORK-*.md` | Compact task state, derived plan view and task-specific contracts. |
| `impact.md` | Actual impact analysis. |
| `decisions.md` | A real unresolved or resolved decision. |
| `wireframes.md`, `design.md` | Applicable UI behavior and design review. |
| `test-cases.md`, `test-cases.csv` | An applicable manual-catalogue obligation and hash-bound import projection. |
| `verification.md` | Actual checks and their evidence. |

Catalogue actual Markdown records only. Accepted no-UI scope needs no visual
templates; missing ad-hoc `feature_spec_frontend:false` metadata cannot override it.
Missing or conflicting scope requires specific clarification. Genuine UI review
remains required. Existing full dossiers and completed tasks remain readable without
deleting records, removing fields or migrating their historical authority.

`proposal.md` records the outcome, exact Git commit or graph build baseline, initial
impact roots, constraints, and outcome-level acceptance criteria. It binds the
repository creation inventory in `creation-inputs.json` by path and SHA-256;
legacy inline inventories remain readable. A graph build is a
valid exact baseline for an unborn Git repository.

Keep the authoritative outcome, shared constraints and limits concise. Task prose
adds only its specific work, acceptance and checks and references that shared context.
Machine state remains separate from human review text; derived views do not become
competing descriptions of the same scope.

### Outcome authorization

Reuse a current applicable authorization. When it is missing, prepare the complete
outcome, explicit scope, constraints, finite overall limits and delegation, then use
`cis change authorize <id> --input <request.json> --reviewer <human> --reason <reason>`
to record the actual human decision. Original reviewer, reason, decision reference
and amendment history remain distinct from derived task/impact/plan attribution.

Delegated controllers may add necessary supporting tests, documentation and in-scope
fixes, split unfinished tasks, assign work and update ordering through
`cis plan task add|split|update`. These operations preserve identity, history, costs
and valid results. A generated plan hash or formatting change alone cannot revoke
authority. Legacy approval of one exact task or plan stays within that original scope.

When resource distribution is delegated, `cis agent work allocate` assigns task
capacity inside the same authorized total. Task count, new agents, changed profiles
and refreshes cannot reset consumption or multiply time, attempts or storage. Native
admission, ownership, accounting and completion checks remain mandatory.

Only a material difference outside the authorization, such as extra functionality,
an incompatible contract or a larger overall allowance, needs its targeted new
decision. Hold the affected work while unrelated work remains eligible where its
dependencies and shared resources permit. Legitimate product, UI and publication
decisions retain their own boundaries. Routine task completion requires no separate
human approval after the normal checks pass.

Managed dossier files and their catalog routes are excluded from the product/source
graph identity so CIS cannot invalidate a change merely by creating its own records.
`cis change rebaseline` is an explicit, actor-and-rationale-audited escape hatch for a
genuine baseline change before findings or work items exist. Once review evidence
exists, rebaseline is blocked; a new change or explicit scope revision preserves the
historical authority boundary.

## 3. Impact analysis

`cis impact analyse` performs bounded, cycle-safe traversal from one or more exact
graph roots. It emits stable findings classified as documentation, contract,
dependency, implementation, delivery, verification, repository, or another graph
kind. Each finding records target identity, evidence, rationale, confidence, and
review state.

Finding IDs are derived from stable graph-node identity. Re-analysis preserves human
disposition and review rationale for the same target. Analysis refuses to mix the
change with a different graph-build baseline.

Allowed states are:

| State | Authority | Meaning |
| --- | --- | --- |
| `proposed` | deterministic analyser | Requires review; not approved scope |
| `accepted` | human command or eligible outcome/feature authority carry-forward | Must be covered by bounded work |
| `rejected` | human command | Reviewed and excluded with a reason |
| `deferred` | human command | Acknowledged but unresolved outside the approved plan |

AI may propose additional findings but cannot invent human authority. Derived
dispositions inside explicit authorized scope preserve their actual decision basis.

When an exact, current feature specification already has explicit human approval,
`cis plan derive` may carry that authority to deterministic findings that are neither
low-confidence, deferred, nor produced by truncated analysis. This is provenance reuse,
not autonomous approval: the original reviewer, rationale, source path, and approved
content digest remain the authority basis. Any ambiguity or expanded risk stops for
individual human disposition.

## 4. Completeness

Completeness is an explicit result, not a claim that every semantic impact exists in
the graph. The retained accepted-impact planning path requires:

- at least one discovered and accepted finding;
- no proposed findings;
- no deferred findings hidden outside approved scope; and
- no truncated traversal.

Graph diagnostics remain visible. A user may broaden roots or bounds and rerun the
idempotent analysis before disposition.

## 5. Decision lifecycle

Change-local decisions create `decisions.md` when the first actual decision exists.
An absent never-needed record is empty; a missing previously created record is an
error and cannot erase its gate. Each record contains a stable ID,
category, question, at least two options, blocking/advisory authority, required-before
gate, lifecycle state, resolution, human rationale, evidence, and optional promoted
ADR path.

Allowed states are `open`, `resolved`, and `deferred`. Resolution must select an exact
recorded option and requires human rationale. A resolved choice cannot be silently
replaced; a different outcome requires a superseding decision. Deferral also requires
human rationale.

Open or deferred blocking decisions prevent plan approval. Open or deferred advisory
decisions remain visible as validation warnings but do not block safe delivery.
Deterministic or AI analysis may propose questions and options but cannot resolve,
defer, or promote them.

Only a resolved decision may be promoted. Promotion creates an accepted, catalogued
ADR under `architecture/decisions/`, carries alternatives, evidence, rationale, and
originating change provenance, and links the change-local record to the ADR.

## 6. Bounded planning

`cis plan build` prepares drafts and derives tasks from explicit outcome authority.
The retained accepted-impact route consumes accepted findings. Every generated work
item contains:

- one bounded objective;
- a `low`, `medium`, or `high` complexity classification;
- explicit authorized scope references or accepted impact IDs;
- feature-requirement IDs when the work originates in an imported feature specification;
- dependency IDs;
- task-specific acceptance criteria;
- proportionate deterministic validation; and
- explicit status.

Ordering follows documentation and contracts, dependencies, implementation,
delivery, then verification. Dependencies express required sequencing without turning
CIS into a general backlog or sprint manager.

`cis plan import-spec <change-id> --file <path>` consumes a repository-relative,
canonical Markdown feature specification. Native documents declare
`type: feature-specification`; PARR-compatible rich documents may declare
`type: specification` with a feature title. The specification supplies either a
functional-requirements table with stable IDs and acceptance criteria or a numbered
Goals section whose entries receive deterministic `GOAL-NNN` IDs. The plan records
the source path and SHA-256 digest rather than copying the specification.

`cis plan derive <change-id> --file <path>` is the default path for a current,
explicitly approved CIS feature. It verifies the registered feature authority, adopts
only eligible deterministic impacts, imports the same task pack, validates it, and
records the exact feature approval as the plan approval basis. When the proposal still
contains only its initialization placeholder, derivation carries the feature requirement
IDs and exact approved acceptance criteria into that section; existing human-managed
criteria are never overwritten. The operation is atomic:
failure restores prior impact dispositions, generated files, and catalog state. It
stops on stale or ambiguous approval, truncated analysis, deferred or low-confidence
findings, unresolved provider conflicts, blocking decisions, mismatched scope, or an
invalid generated plan. The explicit impact/import/approve commands remain available
for those exceptions and for specifications without reusable CIS authority.

Before task instantiation, planning resolves applicable extension definitions by
capability. Mutually exclusive providers stop planning until a human records the
repository selection through `cis plan capability select`; the conflict and its
deterministic fingerprint remain in the change event ledger. Selection never
silently rewrites existing tasks. `cis plan task migrate-type` preserves the stable
task ID/path, evidence, deferrals, external links, and migration history, adds the
selected type's obligations, and returns the task and plan to Draft. Re-import reuses
that migrated identity even when the replacement provider has a different display order.

The template also provides a structured `Surface` column. Supported surface values
are frontend, backend, full-stack, mobile, native, API, contract, data, security,
delivery, and documentation. Older specifications without the column remain valid and
use conservative requirement-text and UX-section classification.

UI-bearing rows also carry `Frontend type`. Its closed values are:

- `public`: anonymous or publicly accessible acquisition, information, registration,
  and other visitor-facing experiences;
- `customer`: authenticated end-customer product experiences; and
- `backoffice`: internal operator, support, administrative, or operational experiences.

`not-applicable` is used for non-frontend rows. For compatibility, an older frontend
row without this column is classified from explicit public/backoffice language and
otherwise defaults to `customer`; newly authored specifications must be explicit.
When one logical behavior affects multiple types, the specification uses one stable
requirement row per type; comma-separated frontend types are deliberately invalid so
each chain retains precise acceptance and evidence ownership.

Task applicability is derived from positive structured functional-requirement evidence.
Optional data, schema-migration, backfill, API, integration, lifecycle, rollout, and
extension workstreams are not activated by exclusions, negative statements, or headings
alone. Token-aware matching prevents partial words and credential labels such as
`API key` from becoming unrelated UI or API-contract evidence.

Task decomposition is deterministic and surface-aware. Frontend requirements produce
a separate, matched wireframe, visual/interaction design, and frontend implementation
chain for every affected frontend type, with verification obligations in the owning
tasks unless a separately owned verification deliverable is required. A public,
customer, and backoffice change therefore has three UI chains rather than one ambiguous
frontend task. Classification-specific task documents contain only their assigned
requirements while retaining the full canonical specification as context.
Backend and contract requirements produce data/migration, API/permission, and backend
work as applicable. Documentation stays within that work unless it has a separately
owned outcome. Cross-module handoffs, search/retrieval,
and lifecycle/carry-forward each become bounded workstreams when detected. Every plan
includes regression, independent review and completion checks in its owning tasks;
separate workflow tasks require an explicit owned deliverable. Security, data, integration,
cross-surface, and migration signals increase complexity.

Repository routing uses imported target roles when available: frontend chains route to
frontend targets, backend and persistence chains to backend/database targets, contracts
to their frontend and backend participants, and infrastructure to infrastructure targets.
Cross-cutting work remains workspace-wide. When classification evidence is unavailable,
planning conservatively retains the feature's declared target list.

An unauthenticated public application endpoint activates the
[`PUBLIC-ENDPOINT-CACHE`](public-endpoint-caching-policy-spec.md) rule. Planning adds
the policy marker and concrete cache/database-isolation obligations to Security, API
contract, Backend and Observability tasks, including native verification and
independent review within those tasks or in explicitly planned separate work. A
valid plan therefore cannot omit caching, allow direct database/repository access
from the endpoint boundary, or leave cache keys, freshness, invalidation, stampede,
failure, security, and operational evidence implicit.
OAuth callbacks, token exchanges, session-creation commands, and similar identity
protocol routes are not public application endpoints solely because they run before
authentication. They remain `no-store`, explicitly classified, and subject to the same
transport-to-application dependency boundary and protocol-specific security review.

High-complexity work is never left as a directly executable task. It becomes a
`decomposed` high-complexity parent with at least two low- or medium-complexity child
tasks. Parent and child records preserve requirement and accepted-impact coverage.

The `plan.md` view is a dependency and status ledger. Every generated row links to a
catalogued `agent-tasks/WORK-NNN.md` contract. Outcome-derived tasks reference shared
scope and limits instead of repeating them. Retained feature task contracts contain:

- objective and bounded required changes extracted from relevant specification sections;
- explicit non-goals and prohibited scope;
- canonical spec path/hash, governed references, and accepted graph-impact evidence;
- task and approval-gate dependencies;
- requirement-level and workstream acceptance checklists;
- targeted deterministic, browser, coverage, and assurance validation as applicable;
- a completion-evidence table for exact commands and artifacts; and
- explicit deferral, owner, follow-up, approval, and residual-risk recording.

`wireframes.md` is the canonical textual screen contract. It defines stable screens,
their public/customer/backoffice classification, states, visible content, actions,
conditions, side effects, and concrete navigation
paths without choosing visual styling. The visual design task converts the validated
wireframe into one self-contained JavaScript renderer and its deterministic PNG
screen pack using JavaScript-generated SVG and the pinned Sharp renderer. The render
must cite and conform to an approved repository design-guideline path and digest;
missing or conflicting guidelines block design approval. `design.md` is the durable
renderer, asset, guideline-conformance, validation, and human
approval record. UI-bearing plans must enforce
`validated wireframes -> visual/interaction design approval -> all
remaining delivery work`. A completed design pack enters a global review pause: all
non-review work stops until a human approves it. Rejection permits only wireframe and
design revision before resubmission. Design approval releases, but does not itself
approve, downstream tasks. By default one design decision approves the exact validated
wireframe digest, renderer, and PNG manifest atomically. A standalone wireframe approval
remains available for teams that intentionally want an earlier behavior-only review,
but it is not a mandatory additional stop. Plan approval does not automatically approve
screen designs. `verification.md` is the
cross-task ledger of commands, artifacts, results, blockers, coverage, and independent
assurance. Generated task documents may evolve with execution evidence after plan
approval, but their stable identity, source provenance, scope, and dependency fields
remain managed.

An imported feature with an applicable manual-catalogue obligation generates
`test-cases.md` and `test-cases.csv` from one deterministic case model. Each covered
functional requirement has one stable manual-test ID,
preconditions, numbered steps, exact expected result, priority/type, requirement
reference, and frontend classification. Markdown records the source feature digest and
the exact CSV hash. Manual execution results remain in `verification.md`.

## 7. Validation and approval

Plan validation fails when:

- an accepted impact has no work item;
- a work item references unaccepted impact;
- IDs are duplicated or dependencies are missing/cyclic;
- complexity is invalid, or high-complexity work is not decomposed into at least two children;
- a linked task document is missing, escapes `agent-tasks/`, or lacks required scope, evidence, acceptance, validation, completion, or deferral sections;
- frontend work lacks the wireframe/design/frontend dependency chain or `design.md`;
- a UI-bearing requirement, screen, design artifact, or UI task is not classified as
  `public`, `customer`, or `backoffice`, or an affected type lacks its matched chain;
- unauthenticated endpoint scope lacks any required `PUBLIC-ENDPOINT-CACHE` task or
  task-document obligation;
- required verification evidence is absent;
- an applicable manual-test artifact is absent, stale, incomplete, or hash-inconsistent;
- acceptance criteria or validation is absent;
- outcome-level acceptance criteria remain TODO after any eligible approved-feature
  carry-forward;
- any blocking decision is open or deferred;
- impact review is incomplete or truncated; or
- no bounded work exists.

Outcome-derived plans retain their existing authority through authorized task
organization and refresh. `cis plan approve` remains the explicit exact-plan path
where that new human decision is actually required and cannot approve an invalid
plan. `cis plan derive` may instead reuse the exact current feature authority;
it never creates a reviewer or rationale and cannot bypass an exceptional finding.
An approved legacy exact plan cannot be silently rebuilt or treated as general delegation.

## 8. Idempotency and audit

Recreating identical analysis or plan content does not create different identities.
Disposition, analysis, decision creation/resolution/deferral/promotion, plan build,
approval, lifecycle creation, and closure append events. Drafting, analysis and
authorization do not execute agents or establish completion. Task completion uses
its normal checked transition. These commands never commit or push repository changes.

## 9. Initial acceptance scenarios

- A change draft is created against an exact baseline with only applicable records catalogued.
- One authorized outcome with several tasks retains one human decision; task organization and delegated allocation stay within its original total.
- Adding supporting work, splitting an unfinished task and refreshing state preserve identity, consumed costs and compatible results.
- A 100-task metadata case requires no task-count approval growth and cannot multiply aggregate capacity; it does not launch 100 workers.
- A real scope or budget expansion needs its specific decision without erasing completed work or holding unrelated eligible tasks.
- Accepted no-UI work creates no visual/manual templates unless a real manual obligation exists; genuine UI retains its review gate.
- Exact graph roots produce evidence-backed proposed findings.
- Accepted-impact planning is blocked while required findings remain proposed or deferred.
- Human disposition survives deterministic re-analysis.
- Every accepted finding is represented by validated bounded work.
- Open decisions block approval.
- Advisory deferred decisions remain visible without blocking approval.
- Resolved durable decisions can be promoted idempotently into catalogued ADRs.
- A valid plan can be explicitly approved and a second approval is unchanged.
- A current approved feature can atomically derive eligible impacts and an approved
  exact plan without duplicate approval prompts.
- Low-confidence, deferred, truncated, stale, ambiguous, or invalid derivation rolls
  back and requests only the exceptional human decision.
- One rendered-design decision approves the bound wireframe digest and PNG pack; a
  separate wireframe checkpoint remains optional.
- A changed graph baseline blocks impact analysis rather than silently changing scope.
- Managed dossier creation does not change the product/source graph identity.
- A genuine pre-impact rebaseline updates managed baseline fields and appends actor,
  rationale, and previous/new identities without rewriting reviewed evidence.
- Re-import retires obsolete task documents into historical catalog routes while
  preserving human evidence and lifecycle history.

## Representative workload assessment

Volume-sensitive plans expose missing workload evidence as an early warning. Final
Delivery Sweep uses the schema and artifact checks documented in
[plan validation](../manual/cis_plan_validate.md#representative-workload-evidence).
Component, installation and end-to-end results remain separate. This does not authorize
resource changes, rollout or workload retries.
