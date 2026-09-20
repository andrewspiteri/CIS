---
title: "Change Impact Studio Articles"
type: navigation
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on article publication
cis:
  stable_id: change-impact-studio:docs:articles
---

# Change Impact Studio articles

This directory is the editorial home for articles that explain the engineering and
governance ideas behind Change Impact Studio.

Articles are explanatory material. They may summarize, illustrate, or challenge the
product, but they do not define CIS behavior. When an article and a canonical product
document differ, the applicable specification, standard, decision, reference, manual,
or implemented behavior takes precedence. Every technical article should link the
canonical sources behind its material claims.

## Editorial programme

The programme contains 78 publication-ready articles across nine connected topic tracks. The
sequence moves from the governance problem through the CIS model and implementation,
then closes with practical application and product learning.

The baselines were reconciled on 8 September 2026 against the current product boundary,
product-definition workflow, direct agent execution, reference governance, editor client,
and assurance behavior. The remaining short drafts were expanded to full text on
9 September 2026. All 78 articles completed editorial and publication review on
10 September 2026 and are Active.

| Track | Articles | Purpose |
|---|---:|---|
| Governed Software Change | 9 | Establish the overall governance thesis and end-to-end model |
| Repository Knowledge and Context | 8 | Explain canonical knowledge, graphs, provenance, and bounded context |
| Change Impact and Planning | 8 | Turn intent and evidence into reviewed, executable scope |
| Human and Agent Execution | 8 | Govern execution consistently across people, tools, and AI providers |
| Verification and Assurance | 8 | Compare approved intent with independent evidence of actual change |
| Engineering Standards and Compliance | 9 | Resolve, enforce, and evolve repository-owned engineering rules |
| Building Change Impact Studio | 10 | Explain the architecture, safety model, testing, and packaging choices |
| CIS in Practice | 10 | Apply the model to representative repository and change scenarios |
| Product Evolution and Learning | 8 | Convert diagnostic evidence into reviewed product improvement |
| **Total** | **78** | **A complete first-pass editorial programme for review** |

## Governed Software Change series

The first series explains CIS as a governance layer for AI-assisted software delivery.
Its connective thesis is:

> Faster implementation increases the need to govern intent, context, scope,
> evidence, and acceptance. Authorship is not control.

| Order | Working title | Focus | Status |
|---:|---|---|---|
| 1 | [Authorship Is Not Control](01-authorship-is-not-control.md) | Why faster code production requires stronger outcome governance | Active |
| 2 | [Governance Should Live with the Code](02-governance-should-live-with-the-code.md) | Repository-backed authority, lifecycle, stable identity, and disposable derived state | Active |
| 3 | [Start with Intent, Not Implementation](03-start-with-intent-not-implementation.md) | Business and technical intent, drift, and authority gates | Active |
| 4 | [Context Should Be Routed, Not Dumped](04-context-should-be-routed-not-dumped.md) | Provenance, bounded retrieval, typed graphs, and fact-versus-inference | Active |
| 5 | [Review Impact Before You Plan the Work](05-review-impact-before-you-plan-the-work.md) | Baselines, proposed findings, human disposition, and completeness boundaries | Active |
| 6 | [Bound the Work Before You Give It to an Agent](06-bound-the-work-before-you-give-it-to-an-agent.md) | Decisions, task contracts, provider-neutral envelopes, and scope expansion | Active |
| 7 | [Completion Is a Claim; Evidence Earns Acceptance](07-completion-is-a-claim-evidence-earns-acceptance.md) | Independent Git evidence, validation, and planned-versus-actual verification | Active |
| 8 | [Govern the Learning Loop](08-govern-the-learning-loop.md) | Sanitized feedback, diagnostic evidence, reviewed proposals, and canonical learning | Active |
| 9 | [Change Impact Studio in Practice](09-change-impact-studio-in-practice.md) | An end-to-end governed change using the Friends Todo golden path | Active |

All titles link to their canonical repository source. Every article is Active and is
the repository's current publication-ready version. Article lifecycle does not grant
product authority.

## Repository Knowledge and Context series

| Order | Title | Focus | Status |
|---:|---|---|---|
| 1 | [Repository Knowledge Is More Than Source Code](10-repository-knowledge-is-more-than-source-code.md) | Intent, contracts, decisions, tests, workflows, and operational knowledge | Active |
| 2 | [Canonical Knowledge Versus Derived Knowledge](11-canonical-knowledge-versus-derived-knowledge.md) | Durable repository authority and disposable local state | Active |
| 3 | [Building a Typed Engineering Graph](12-building-a-typed-engineering-graph.md) | Stable identity, typed nodes, evidence-backed edges, and rebuildable storage | Active |
| 4 | [Context Packs: Enough, but Not Everything](13-context-packs-enough-but-not-everything.md) | Task-specific source selection, excerpts, bounds, and privacy | Active |
| 5 | [Provenance, Confidence, and Relationship State](14-provenance-confidence-and-relationship-state.md) | The evidence and review state behind engineering relationships | Active |
| 6 | [Understanding Change Across Multiple Repositories](15-understanding-change-across-multiple-repositories.md) | Workspace authority, participant ownership, and federated context | Active |
| 7 | [Why a Knowledge Graph Cannot Prove Completeness](16-why-a-knowledge-graph-cannot-prove-completeness.md) | Honest traversal bounds and workflow completeness | Active |
| 8 | [Index First, Search Second, Load Last](17-index-first-search-second-load-last.md) | A layered repository-discovery strategy | Active |

## Change Impact and Planning series

| Order | Title | Focus | Status |
|---:|---|---|---|
| 1 | [What Is a Change Dossier?](18-what-is-a-change-dossier.md) | A durable home for outcome, scope, work, evidence, and acceptance | Active |
| 2 | [Binding Change Analysis to an Exact Baseline](19-binding-change-analysis-to-an-exact-baseline.md) | Reproducible impact and verification | Active |
| 3 | [From Proposed Impact to Approved Scope](20-from-proposed-impact-to-approved-scope.md) | Evidence-backed findings and human disposition | Active |
| 4 | [Decisions Are Part of the Plan](21-decisions-are-part-of-the-plan.md) | Options, gates, rationale, and ADR promotion | Active |
| 5 | [Why High-Complexity Tasks Must Be Decomposed](22-why-high-complexity-tasks-must-be-decomposed.md) | Complexity as an execution constraint | Active |
| 6 | [Planning Across Frontend, Backend, Data, Security, and Operations](23-planning-across-engineering-surfaces.md) | Complete surface-aware workstreams | Active |
| 7 | [From an Approved Feature Specification to Bounded Work](24-from-approved-feature-to-bounded-work.md) | Exact authority carry-forward and atomic derivation | Active |
| 8 | [Plan Public, Customer, and Backoffice Experiences Separately](25-plan-public-customer-and-backoffice-separately.md) | Classification-specific behavior and evidence chains | Active |

## Human and Agent Execution series

| Order | Title | Focus | Status |
|---:|---|---|---|
| 1 | [The Coding Agent Should Not Own the Plan](26-the-coding-agent-should-not-own-the-plan.md) | Separating planning authority from execution | Active |
| 2 | [Designing a Provider-Neutral Agent Task](27-designing-a-provider-neutral-agent-task.md) | Portable authority, context, and result contracts | Active |
| 3 | [Human and Agent Execution from the Same Contract](28-human-and-agent-execution-from-the-same-contract.md) | One canonical task for every executor | Active |
| 4 | [Stale-Safe Task Envelopes](29-stale-safe-task-envelopes.md) | Digest binding and current-authority validation | Active |
| 5 | [When Deterministic Tools Should Replace Model Calls](30-when-deterministic-tools-should-replace-model-calls.md) | Exact evidence before probabilistic interpretation | Active |
| 6 | [Governing Local and Remote AI Routes](31-governing-local-and-remote-ai-routes.md) | Capability routing, privacy, caching, and usage | Active |
| 7 | [Resumable Workflows Without Shell Execution](32-resumable-workflows-without-shell-execution.md) | Structured steps, checkpoints, and digest-safe resume | Active |
| 8 | [External Trackers Are Projections, Not Sources of Truth](33-external-trackers-are-projections.md) | Provider-neutral synchronization and conflict authority | Active |

## Verification and Assurance series

| Order | Title | Focus | Status |
|---:|---|---|---|
| 1 | [Why Agent-Reported Changed Files Are Not Evidence](34-agent-reported-changed-files-are-not-evidence.md) | Executor narration versus independent Git evidence | Active |
| 2 | [Comparing Approved Impact with the Actual Git Diff](35-approved-impact-versus-actual-git-diff.md) | Planned and observed scope | Active |
| 3 | [Expected, Missing, and Unexpected Change](36-expected-missing-and-unexpected-change.md) | Actionable verification findings | Active |
| 4 | [The Difference Between Testing, Verification, and Acceptance](37-testing-verification-and-acceptance.md) | Behavior evidence, delivery evidence, and human authority | Active |
| 5 | [Proportionate Assurance for Different Risk Classes](38-proportionate-assurance-for-risk.md) | Evidence depth by consequence and uncertainty | Active |
| 6 | [Recording Completion Evidence That Survives the Conversation](39-completion-evidence-that-survives-the-conversation.md) | Durable command, artifact, deferral, and risk records | Active |
| 7 | [Why a Passing Workflow Cannot Approve Completion](40-a-passing-workflow-cannot-approve-completion.md) | Workflow evidence and its authority boundary | Active |
| 8 | [Independent Assurance for AI-Assisted Development](41-independent-assurance-for-ai-assisted-development.md) | Independent scope, Git, validation, and risk review | Active |

## Standards and Engineering Policy series

| Order | Title | Focus | Status |
|---:|---|---|---|
| 1 | [Turning Engineering Guidance into Governed Standards](42-turning-guidance-into-governed-standards.md) | Identity, lifecycle, rules, mappings, and exceptions | Active |
| 2 | [Stable Rule IDs and Why They Matter](43-stable-rule-ids-and-why-they-matter.md) | Traceable obligations and durable history | Active |
| 3 | [Resolving Standards by Technology and Change Surface](44-resolving-standards-by-technology-and-surface.md) | Evidence-based applicability | Active |
| 4 | [Conformance Is Not the Same as Compliance](45-conformance-is-not-compliance.md) | Enforcement routing versus broad certification | Active |
| 5 | [Deterministic, Manual, and Advisory Enforcement](46-deterministic-manual-and-advisory-enforcement.md) | Distinct evidence strengths | Active |
| 6 | [Governing Exceptions and Compensating Controls](47-governing-exceptions-and-compensating-controls.md) | Exact, bounded, human-owned deviations | Active |
| 7 | [Importing Standards Without Overwriting Local Authority](48-importing-standards-without-overwriting-authority.md) | Staged admission and reconciliation | Active |
| 8 | [The Public Endpoint Cache Boundary as a Worked Example](49-public-endpoint-cache-boundary.md) | Policy propagation into complete work | Active |
| 9 | [API Compatibility as a Governance Problem](50-api-compatibility-as-governance.md) | Baselines, consumers, lifecycle, and decisions | Active |

## Building Change Impact Studio series

| Order | Title | Focus | Status |
|---:|---|---|---|
| 1 | [Why CIS Is a Modular CLI](51-why-cis-is-a-modular-cli.md) | Local, scriptable, editor-independent governance | Active |
| 2 | [One Module, One Top-Level Command](52-one-module-one-top-level-command.md) | Capability ownership and CLI vocabulary | Active |
| 3 | [Keeping the Host as the Composition Root](53-keeping-the-host-as-composition-root.md) | Wiring, contracts, behavior, and trust boundaries | Active |
| 4 | [Why the VS Code Extension Is a Thin Client](54-why-the-vscode-extension-is-a-thin-client.md) | One domain implementation across clients | Active |
| 5 | [Repository-Backed State Versus Local SQLite](55-repository-state-versus-local-sqlite.md) | Durable authority and efficient derived queries | Active |
| 6 | [Designing Human, JSON, and Agent Output](56-designing-human-json-and-agent-output.md) | Readable, stable, compact command contracts | Active |
| 7 | [Safe Path Handling and Atomic Repository Mutation](57-safe-paths-and-atomic-repository-mutation.md) | Containment, preview, collision, ownership, and atomicity | Active |
| 8 | [Provider Contracts and Explicit Assembly Loading](58-provider-contracts-and-explicit-assembly-loading.md) | Replaceable integrations without repository code execution | Active |
| 9 | [Testing an Engineering Governance Tool](59-testing-an-engineering-governance-tool.md) | Rules, lifecycle, safety, integration, and packages | Active |
| 10 | [Packaging CIS as a .NET Tool and VS Code Extension](60-packaging-cis-as-tool-and-extension.md) | Versioned, smoke-tested, checksummed releases | Active |

## CIS in Practice series

| Order | Title | Focus | Status |
|---:|---|---|---|
| 1 | [Initializing a Newly Created Repository](61-initializing-a-new-repository.md) | Establishing governance before implementation grows | Active |
| 2 | [Onboarding an Existing .NET Application](62-onboarding-an-existing-dotnet-application.md) | Classification, reconciliation, and reviewed evidence | Active |
| 3 | [Governing a Multi-Repository Product](63-governing-a-multi-repository-product.md) | Authority and federated participant facts | Active |
| 4 | [The Friends Todo Golden Path](64-the-friends-todo-golden-path.md) | End-to-end product and packaging gaps | Active |
| 5 | [Planning a Cross-Surface Authentication Change](65-planning-a-cross-surface-authentication-change.md) | Public, protocol, customer, API, security, and operations | Active |
| 6 | [Governing a Public Cached Endpoint](66-governing-a-public-cached-endpoint.md) | Cache, persistence, failure, and evidence boundaries | Active |
| 7 | [Detecting a Breaking API Change](67-detecting-a-breaking-api-change.md) | Supported baselines, consumers, and strategy | Active |
| 8 | [Handing One Task to Different Coding Agents](68-one-task-different-coding-agents.md) | Controlled provider comparison | Active |
| 9 | [Finding Unexpected Work During Verification](69-finding-unexpected-work-during-verification.md) | Scope discrepancy and future context | Active |
| 10 | [Turning a Golden-Path Failure into Reviewed Product Learning](70-from-golden-path-failure-to-product-learning.md) | Owning-contract fixes and packaged replay | Active |

## Product Evolution and Learning series

| Order | Title | Focus | Status |
|---:|---|---|---|
| 1 | [What the Golden Path Taught Us](71-what-the-golden-path-taught-us.md) | Integration lessons beyond isolated tests | Active |
| 2 | [Where CIS Still Requires Human Judgment](72-where-cis-still-requires-human-judgment.md) | Meaning, decisions, exceptions, risk, and acceptance | Active |
| 3 | [Designing Feedback Without Collecting Source or Prompts](73-designing-feedback-without-source-or-prompts.md) | Sanitized operational evidence | Active |
| 4 | [Why CIS Cannot Self-Approve Its Improvements](74-why-cis-cannot-self-approve-improvements.md) | Learning proposals and authority separation | Active |
| 5 | [From Diagnostic Evidence to a Reviewed Learning Proposal](75-from-diagnostic-evidence-to-learning-proposal.md) | Bounded evidence, proposal, and promotion | Active |
| 6 | [Release Notes with Engineering Context](76-release-notes-with-engineering-context.md) | Outcome, compatibility, evidence, and limits | Active |
| 7 | [Architectural Decisions Behind a CIS Release](77-architectural-decisions-behind-a-cis-release.md) | Change decisions, ADRs, and release communication | Active |
| 8 | [What Changed in CIS and Why](78-what-changed-in-cis-and-why.md) | A repeatable product-evolution narrative | Active |

## Article conventions

- Store articles in this directory as `NN-kebab-case-title.md`.
- Give every article front matter with title, `type: article`, truthful lifecycle
  status, owner, review date, and a stable ID beneath `change-impact-studio:article:`.
- Register every created article in `docs/catalog.yml` with `authority: canonical`.
  Canonical means the file is the authoritative version of that article, not that the
  article overrides product specifications.
- Use `Draft` while claims, examples, links, or diagrams remain under review; use
  `Active` only for the repository's current publication-ready version.
- Link the sources for product behavior rather than duplicating large specification
  sections.
- If product behavior changes, update the canonical product documentation first and
  then reconcile affected articles.
- Keep diagrams and other article assets beneath `docs/articles/assets/<article-slug>/`.

## How to find articles

From the repository:

1. Start with this index for the curated series and publication status.
2. List created article files with `git ls-files "docs/articles/*.md"`.
3. Search titles or text with `rg -n -i "<topic>" docs/articles`.
4. Once the repository graph is current, use
   `cis graph find --kind document --text "<topic>"` for catalog-aware discovery.
5. Use `cis docs inventory` to see article metadata and detect uncatalogued drafts.

The root [getting-started guide](../../README.md) links this index for readers entering
the repository for the first time.

## Canonical sources for the series

- [Product intent](../specs/product-intent-spec.md)
- [System context](../specs/system-context-spec.md)
- [Technical intent](../specs/technical-intent-spec.md)
- [Business requirements governance](../specs/business-requirements-governance-spec.md)
- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Command manual](../manual/README.md)
