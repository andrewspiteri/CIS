---
title: "Change Impact Studio Product Intent"
type: repository-specification
status: Draft
scope: Product
owner: "Andrew Spiteri"
last_reviewed: "2026-08-25"
review_cadence: on product change
cis:
  stable_id: change-impact-studio:spec:product-intent
---

# Change Impact Studio product intent

## Purpose

Change Impact Studio (CIS) is a local-first, repository-backed engineering workspace
for governing the complete lifecycle of a proposed software change. It helps a team
establish the intended outcome, retrieve relevant engineering knowledge, review likely
impact, resolve decisions, approve bounded work, prepare execution, and independently
verify the result.

CIS addresses a growing asymmetry in software delivery: implementation is becoming
easier to produce than it is to understand, govern, and verify. Documentation, source,
contracts, tests, workflows, Git history, external trackers, and individual knowledge
often describe different parts of the same system. A developer or coding agent can
therefore produce a technically coherent change that is incomplete, inconsistent, or
aimed at the wrong outcome.

The intended result is not more documentation or more approval steps. It is:

- reliable, bounded context before implementation;
- earlier discovery of hidden business and engineering impact;
- explicit human decisions at genuine authority boundaries;
- safer execution by humans and coding agents;
- traceable evidence from intent through acceptance;
- fewer incorrect completion claims and less rework; and
- repository knowledge that improves after reviewed delivery.

## Accountability rule

> AI and coding agents may discover, propose, analyse, draft, and implement. Humans
> confirm meaning, resolve material decisions, approve exceptional or changed scope,
> accept risk, and accept completion.

Deterministic authority carry-forward may reuse an exact, current human approval when
the governed source, digest, scope, and evidence remain unchanged. It must stop when
content is stale, ambiguous, expanded, low-confidence, or otherwise outside the
approved boundary.

## Actors and stakeholders

| Actor | Need or responsibility | Success signal |
|---|---|---|
| Repository maintainer | Establish repository identity, documentation authority, standards, and safe operating policy | CIS can initialize and validate the repository without overwriting reviewed content |
| Change owner | Define the outcome, constraints, scope, and acceptance evidence | The change dossier remains understandable without relying on chat history |
| Software architect or technical authority | Resolve boundaries, compatibility, security, data, and operational decisions | Blocking decisions are explicit and settled before dependent work begins |
| Developer or coding agent | Receive bounded, current, evidence-backed work | The executor can act without rediscovering authority or silently expanding scope |
| Reviewer or assurer | Compare the approved change with implementation and verification evidence | Missing, unexpected, and failed work is visible before acceptance |
| Documentation or product authority | Maintain canonical business, product, technical, and standards knowledge | Delivered changes reconcile with current intent and retain provenance |
| Platform or system owner | Govern multi-repository dependencies, delivery constraints, and operational risk | Cross-repository impact is visible without centralizing every repository's local facts |

## Scope

### In scope

- Repository and multi-repository workspace initialization.
- Canonical repository-owned Markdown, stable identities, catalogues, and lifecycle state.
- Classification of supported repository technologies and selection of applicable
  specifications, standards, references, instructions, and skills.
- Derived local indexes and a typed, evidence-backed engineering graph.
- Business-requirements and technical-intent governance for workspace authorities.
- Baseline-bound change dossiers, impact analysis, human disposition, decisions, and
  bounded dependency-aware planning.
- Deterministic generation, shell-free workflows, governed AI routing, and
  provider-neutral task preparation.
- Optional projection to external trackers without transferring canonical authority.
- Planned-versus-actual verification, exact evidence, diagnostics, feedback, and
  human-reviewed learning.
- A thin Visual Studio Code client over the CLI and repository-owned Markdown.

### Out of scope

CIS is not:

- an autonomous software-development agent;
- a replacement for Git, GitHub, Jira, an IDE, or CI/CD;
- a general backlog, sprint, or portfolio-management system;
- a hosted repository-indexing or document-management service;
- an authority that may invent stakeholder intent, approve its own work, accept risk,
  or declare completion from an executor's claim; or
- a reason to move durable product meaning into generated local state.

## Product principles

1. **Repository source of truth.** Canonical meaning remains in versioned,
   repository-owned files.
2. **Derived state is disposable.** Indexes, graph databases, caches, run state,
   envelopes, diagnostics, and proposals can be deleted and rebuilt.
3. **Generate locally, review locally, commit deliberately.** CIS does not silently
   mutate canonical content or perform remote delivery actions.
4. **Human-confirmed meaning.** Deterministic and model-assisted discovery may surface
   evidence and questions; it does not establish stakeholder intent.
5. **Deterministic before probabilistic.** Structural facts, validation, and successful
   workflows should not require a model when a deterministic route exists.
6. **Provenance is mandatory.** Material findings and decisions retain their source,
   baseline, evidence, and authority.
7. **Bounded execution.** Work packages carry explicit objectives, non-goals,
   dependencies, acceptance criteria, validation, and scope-expansion rules.
8. **Independent verification.** Executors produce candidate changes; acceptance is
   based on independently gathered Git and validation evidence.
9. **Provider-neutral control.** Humans, coding agents, models, and trackers are
   replaceable execution or projection surfaces, not the owner of the change domain.
10. **Learning is governed.** Feedback may produce improvement proposals, but only
    reviewed changes enter canonical knowledge, policy, or instructions.

## Capabilities

The implemented product provides these capability groups:

| Capability | Outcome |
|---|---|
| Initialize and diagnose | Safely establish or reconcile a repository or workspace and report collisions before mutation |
| Govern documentation and standards | Inventory, validate, route, import, audit, and map repository-owned engineering knowledge |
| Build and query context | Derive local indexes and graph relationships without making generated state authoritative |
| Govern intent | Maintain canonical business requirements and technical direction with explicit lifecycle and drift |
| Review change impact | Bind a proposed outcome to an exact baseline and disposition evidence-backed impact findings |
| Decide and plan | Resolve material choices and create validated, dependency-aware, bounded work |
| Prepare and coordinate execution | Render deterministic artifacts, run resumable workflows, prepare agent tasks, and project work to trackers |
| Verify and accept | Compare approved scope with actual Git state and preserve exact completion evidence |
| Diagnose and learn | Collect sanitized local feedback and promote only reviewed learning into canonical history |

## Requirements and acceptance

| Requirement ID | Requirement | Acceptance evidence | Status |
|---|---|---|---|
| CIS-REQ-001 | CIS must preserve repository files as the source of canonical engineering meaning. | Deleting `.cis/local/` does not remove or change a durable decision, requirement, standard, plan, or acceptance record. | Implemented |
| CIS-REQ-002 | Repository and workspace initialization must be previewable, idempotent, collision-aware, and confirmation-gated. | Repeated dry runs are stable; divergent reviewed files are retained or reported rather than overwritten. | Implemented |
| CIS-REQ-003 | Derived context must retain stable identity, evidence, provenance, freshness, and repository boundaries. | Graph and context validation expose stale, ambiguous, missing, or cross-repository evidence. | Implemented |
| CIS-REQ-004 | Business and technical intent must have explicit authority, lifecycle, approval, and drift behavior. | Downstream governed work stops when required intent is missing, stale, incomplete, or unapproved. | Implemented |
| CIS-REQ-005 | Change impact must be analysed against an exact baseline and remain proposed until valid authority disposes it. | Re-analysis preserves identity and human rationale; a changed baseline cannot silently change reviewed scope. | Implemented |
| CIS-REQ-006 | Planning must cover accepted impacts with bounded objectives, dependencies, acceptance criteria, and proportionate validation. | Plan validation rejects uncovered impact, cyclic or missing dependencies, unresolved blocking decisions, and incomplete task contracts. | Implemented |
| CIS-REQ-007 | Execution preparation must be provider-neutral and stale-safe. | Prepared task envelopes bind exact task and source digests; result ingestion cannot mark work complete. | Implemented |
| CIS-REQ-008 | Models and remote services must operate through explicit routes, privacy boundaries, and human authorization. | Remote model use requires the governed route and explicit authorization; sensitive content and secrets are rejected. | Implemented |
| CIS-REQ-009 | Verification must independently compare planned and actual change and require explicit acceptance. | Verification reports expected, missing, unexpected, and failed work from Git and validation evidence. | Implemented |
| CIS-REQ-010 | External trackers, editor clients, diagnostics, and learning proposals must not acquire canonical authority. | These surfaces can project, route, or propose state but cannot approve scope, completion, risk, or policy. | Implemented |

## Controlled vocabulary

| Term | Meaning |
|---|---|
| Canonical | Repository-owned state that carries durable product, engineering, or review authority |
| Derived | Rebuildable local state used for routing, analysis, execution, or diagnostics |
| Authority | The identified human or approved canonical source permitted to establish meaning or accept an outcome |
| Baseline | The exact Git or graph identity against which a governed change is analysed and verified |
| Change dossier | The catalogued repository record containing a proposal, impact, decisions, plan, task contracts, design, tests, and verification |
| Impact finding | An evidence-backed proposal that a repository or workspace concern may be affected |
| Bounded work | A task with explicit scope, non-goals, dependencies, acceptance, validation, and evidence obligations |
| Acceptance | A human decision that current evidence satisfies the governed completion boundary; it is distinct from execution success |

## Success measures

CIS should be evaluated by whether it improves delivery outcomes, not by the number of
documents, graph nodes, model calls, or generated tasks. Useful measures include:

- time required to understand and safely start work in an unfamiliar repository;
- percentage of accepted impacts covered by validated work and verification evidence;
- unexpected or missing changes found before acceptance;
- stale or conflicting intent detected before implementation;
- repeated context, scope, and validation failures eliminated through reviewed learning;
- compact context and deterministic routes used instead of unnecessary model work; and
- absence of silent canonical mutation, inferred approval, secret disclosure, and
  unauthorized remote action.

## Material risks

- Incomplete documentation or graph evidence can create false confidence; CIS must
  describe impact completeness as bounded evidence, not semantic certainty.
- Excessive governance can add delay; deterministic carry-forward should remove
  duplicate approvals when exact authority remains current.
- Generated documentation can appear authoritative before review; lifecycle,
  provenance, and source-of-truth distinctions must remain visible.
- Provider integrations can drift or leak authority; adapters must stay subordinate to
  canonical repository contracts.
- Product documentation can become stale as implementation evolves; product intent,
  system context, technical intent, manuals, and the implementation roadmap must be
  reconciled in the same governed change.

## Related documents

- [System context](system-context-spec.md)
- [Technical intent](technical-intent-spec.md)
- [Business requirements governance](business-requirements-governance-spec.md)
- [Change impact and bounded planning](change-impact-and-planning-spec.md)
- [Execution, assurance, diagnostics, and learning](execution-assurance-and-learning-spec.md)
- [Implementation roadmap](implementation-roadmap.md)
