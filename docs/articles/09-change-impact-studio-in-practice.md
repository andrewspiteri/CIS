---
title: "Change Impact Studio in Practice"
type: article
status: Active
series: "Governed Software Change"
series_order: 9
owner: "Andrew Spiteri"
last_reviewed: "2026-09-22"
review_cadence: on golden-path or product change
summary: "A worked example of governing a cross-repository feature from product intent through independent verification and reviewed learning."
cis:
  stable_id: change-impact-studio:article:cis-in-practice
---

# Change Impact Studio in practice

The principles behind Change Impact Studio become clearer when they are applied to a
feature that crosses real engineering boundaries.

Consider Friends Todo: a small product with separate web, API, and infrastructure
repositories. Users can manage todo lists and share access with friends. The example is
deliberately familiar. The interesting part is not the domain. It is the engineering
surface hidden behind a seemingly simple request.

The proposed outcome is:

> Allow a signed-in customer to invite another person to a todo list through a secure,
> time-bounded access link, while preserving public-route caching policy, authorization,
> auditability, and reliable expiration behavior.

This article follows the governed path from intent to acceptance.

## 1. Establish workspace authority

The product spans three repositories:

- `todo-web` owns the customer experience;
- `todo-api` owns application behavior, authorization, and persistence; and
- `todo-infra` owns deployment and supporting infrastructure.

A separate documentation repository acts as the workspace authority for the Friends
Todo product inside its sample ecosystem. It owns the canonical product business
requirements, workspace-scoped technical intent, solution definition, and shared delivery
map. The three product repositories are owned participants
and retain their local specifications, source, tests, and implementation references.

```powershell
cis workspace init --root docs --ecosystem friends-todo --product friends-todo --dry-run
cis workspace init --root docs --ecosystem friends-todo --product friends-todo --yes

cis repo import `
  --source C:\work\todo-web C:\work\todo-api C:\work\todo-infra `
  --root cisdocs `
  --participation owned --relationship none `
  --dry-run
```

The dry run exposes classification, generated documentation, standards, skills, and
collisions before any repository changes. After review and confirmed import, a workspace
graph is built from the registered repositories. Dependencies owned by another product
would be imported separately with `--participation dependency`, a directional
relationship, and optional component scope; they would not become Friends Todo
implementation targets.

## 2. Define and activate the product baseline

The business requirements establish the actors, sharing outcome, access constraints,
and success measures. Technical intent establishes ownership, API boundaries, identity,
data consistency, security, observability, and compatibility direction.

CIS coordinates that work through the product-definition journey:

```powershell
cis definition init --workspace C:\work\friends-todo-docs
cis definition status --workspace C:\work\friends-todo-docs
cis definition prepare --page business --workspace C:\work\friends-todo-docs
```

The eight pages cover foundation, business, technical direction, architecture, contracts,
experience direction, delivery, and consolidated review. Existing-system inference may
use bounded evidence from the owned product repositories, but observed code is not treated
as stakeholder intent. Dependency repositories are excluded from product inference.

The final activation binds the exact business, technical, architecture, dictionary,
experience, preview, and backlog artifacts. It does not collapse their ownership or turn
an inferred draft into approval.

This prevents the feature from beginning as “add an invite table and endpoint.” The
implementation shape remains open until the required behavior is clear.

Important questions include:

- Who may create and revoke an invitation?
- Is the link single-use or reusable until expiry?
- Can it be forwarded?
- What happens if the recipient already has access?
- Which repository owns link generation and validation?
- What must be audited?
- How are expired links cleaned up?
- Which unauthenticated routes are application endpoints and which are identity protocol routes?

Unresolved answers remain visible decisions rather than assumptions delegated to an agent.

## 3. Define the feature before creating work

The activated product baseline says what Friends Todo is. It does not yet say that this
invitation feature is ready for delivery.

Feature intake retains the prepared feature BRD, creates or connects its owned
implementation repository, and opens a canonical Draft request. The feature-definition
wizard then separates the review into foundation, business, technical direction,
architecture, contracts, experience, delivery, and final review.

The architecture and screen views are proposals for review. On the delivery page, the
team separates required foundations from first-release MVP stories and possible Post-MVP
work. Before treating those stories as new implementation, CIS can compare them with
bounded code evidence from every product-owned repository. A result such as **Reuse
existing**, **Extend existing**, or **Scope conflict** is advisory evidence, not an
automatic scope decision. Reviewers inspect its code references, correct the proposed
stories and remaining work, resolve or retain visible conflicts, and then save the delivery
page. Using the reconciled stories as a draft does not save or approve them.

Final feature-definition review records human review of the proposed definition. It does
not create or approve the feature specification, approve the product backlog, authorize
implementation, or approve a release. The reviewed scope must still enter the governed
backlog, produce a current feature specification, and pass the applicable human approval
gate before CIS can carry that authority into a plan.

## 4. Create a baseline-bound dossier

The change proposal records the observable outcome, constraints, acceptance criteria,
exact graph build, and initial roots such as the access-management feature, API contract,
and relevant customer screen.

```powershell
cis graph find --text "access invitation"
cis graph related --id <access-node> --depth 2

cis change create `
  --title "Invite a friend to a todo list" `
  --outcome "A list owner can issue, revoke, and verify a time-bounded access link" `
  --root <access-node>#<kind>
```

The dossier becomes the durable home for proposal, impact, decisions, plan, wireframes,
design, test cases, task contracts, events, and verification.

## 5. Analyse and review impact

Bounded graph traversal proposes affected concerns across the workspace. Likely findings
include:

- customer access-management screens and navigation;
- API operations and problem responses;
- permission and ownership checks;
- access-link storage, expiration, revocation, and cleanup;
- public or pre-authentication route classification;
- rate limiting and abuse controls;
- audit or observability events;
- infrastructure configuration;
- manual and automated tests; and
- documentation and governed references.

The analyser does not approve these findings. A reviewer accepts, rejects, or defers
each with rationale. If traversal was truncated or any finding remains proposed, the
change is not ready for planning.

This review can reveal gaps in CIS itself. For example, a pre-authentication identity
callback should not inherit ordinary public application caching rules. An SDK-owned
dynamic route family should not be forced into a static endpoint model. Those
discoveries become product-learning candidates rather than quiet case-study exceptions.

## 6. Resolve decisions

The change records options before implementation. A link-lifecycle decision might compare:

- a single-use opaque token;
- a reusable token until expiry; and
- an account-bound acceptance flow.

Evidence includes threat boundaries, user experience, revocation requirements, storage
cost, and compatibility. A human selects an option and records rationale. Blocking
decisions must be resolved before plan approval.

If the choice has durable architectural value, the resolved record can be promoted to
an ADR without losing its originating change and alternatives.

## 7. Build bounded work

Planning consumes accepted impact only. The feature decomposes into ordered work such as:

1. reconcile requirements and contracts;
2. define customer wireframes and states;
3. approve rendered design;
4. define API and problem contracts;
5. implement permission and access-link lifecycle behavior;
6. implement persistence and cleanup;
7. implement the customer experience;
8. add observability and operational configuration;
9. verify cross-repository behavior; and
10. perform independent assurance and final delivery review.

Each work item carries accepted impact IDs, requirements, exact source digests,
dependencies, non-goals, validation, and completion evidence. High-complexity parents
are decomposed before execution.

## 8. Prepare execution without transferring authority

Approved tasks can be prepared for a human or coding agent. The portable envelope contains
the task contract and bounded context. CIS can also coordinate an explicit foreground
provider run with a declared mode, permission ceiling, transport, actor, and target
repository. Neither route allows the executor to approve design, expand scope silently,
or mark the task complete.

```powershell
cis agent prepare CIS-0001 WORK-005
cis agent run CIS-0001 WORK-005 `
  --provider codex --transport app-server `
  --mode implement --permission workspace-write `
  --actor "Andrew Spiteri"
```

Workspace-write runs use an isolated Git worktree by default. Provider events and results
remain derived under `.cis/local/`; an explicit import validates the result before it can
become canonical evidence, and imported evidence still does not grant completion.

If implementation discovers a missing repository or migration, the executor reports
the evidence and proposed expansion. The impact and plan return to review rather than
absorbing the surprise invisibly.

## 9. Verify the actual change

After implementation, CIS compares actual Git state with planned repositories and paths.
It identifies expected changes, missing work, unexpected changes, and validation failures.

Evidence can include:

- focused authorization and lifecycle tests;
- browser tests for link creation, revocation, expiry, and error states;
- API contract and compatibility validation;
- public-route cache and persistence-boundary checks;
- documentation and catalog validation;
- workspace graph freshness; and
- infrastructure or operational checks.

The agent's changed-file report is supporting information. Git and deterministic checks
provide the independent view.

## 10. Accept and learn

A human reviewer examines the planned-versus-actual comparison, failed or unavailable
checks, unexpected changes, deferrals, and residual risk. Acceptance records identity
and rationale.

The workspace then reconciles product knowledge. Legitimate surprises may lead to
reviewed updates in graph relationships, planning triggers, API classification,
standards, tests, or agent guidance. Those updates remain proposals until approved and
delivered through their own governed changes.

## The same feature without the governed path

The governed workflow is easier to understand when compared with a plausible shortcut.
This parallel path is hypothetical; it is not a claim about an actual Friends Todo
implementation.

An issue says “let a customer invite a friend to a list.” An implementer searches the
API repository, selects a token and expiry design, adds persistence and endpoints, then
finds the customer repository and adds a basic form. A setting needed for expiry or
cleanup causes an infrastructure edit late in the work. Focused tests pass, the pull
request is merged, and the issue is closed.

Nothing in that sequence requires obviously poor code. The problem is that important
questions have no durable answer:

- Was the implemented link lifecycle chosen by an authorized product or technical
  reviewer, or inferred during coding?
- Were revocation, forwarding, existing access, abuse controls, auditability, and error
  states deliberately included or omitted?
- Were public and pre-authentication routes classified before their caching and
  persistence boundaries were implemented?
- Was the infrastructure edit expected scope, legitimate discovery, or unrelated work?
- Which repositories and supported contracts were examined against the same baseline?
- What evidence supports acceptance beyond the checks selected by the implementer?

The two paths can be compared at each stage:

| Moment | Ungoverned shortcut | Governed path |
|---|---|---|
| Request | Implementation begins from a short issue | Product and feature intent expose outcomes, constraints, non-goals, and open decisions |
| Discovery | The implementer searches where the feature seems likely to live | Bounded workspace evidence proposes impact across owned repositories |
| Scope | Tasks emerge while coding | A reviewer dispositions findings and resolves blocking decisions before planning |
| Execution | The implementer absorbs newly discovered work | Bounded tasks require new impact to return to review |
| Verification | Selected tests and a completion summary support the claim | Git, contracts, required checks, and planned-versus-actual comparison supply independent evidence |
| Completion | Merge or issue closure implies success | An authorized human records acceptance, rationale, and residual risk |
| Learning | Surprises remain in conversation or become ad hoc rules | Evidence produces a bounded proposal that requires separate review and delivery |

Governance does not guarantee that the final source code will differ. It guarantees that
the product can explain why this implementation was authorized, what was considered,
what remained uncertain, and why the available evidence was sufficient to accept it.

## What the example demonstrates

The value of CIS is not that it generated an invitation feature. A capable engineer or
agent could implement the code without it.

The value is that the product can show:

- which intent authorized the change;
- which repositories and obligations were considered;
- which findings a human accepted;
- which decisions shaped the solution;
- which tasks were approved;
- what the executor was allowed to do;
- what actually changed;
- which evidence supports completion; and
- what the delivery taught the engineering system.

That is governed software change in practice.

## Canonical CIS sources

- [Product intent](../specs/product-intent-spec.md)
- [Business requirements governance](../specs/business-requirements-governance-spec.md)
- [Technical-intent governance](../specs/technical-intent-governance-spec.md)
- [High-level product-definition wizard](../specs/high-level-product-definition-wizard-spec.md)
- [`cis brd feature intake`](../manual/cis_brd_feature_intake.md)
- [`cis brd feature wizard status`](../manual/cis_brd_feature_wizard_status.md)
- [`cis brd feature wizard delivery`](../manual/cis_brd_feature_wizard_delivery.md)
- [Provider-neutral agent execution](../specs/features/agent-execution-coordination-feature.md)
- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
