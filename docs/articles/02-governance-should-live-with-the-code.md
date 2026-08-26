---
title: "Governance Should Live with the Code"
type: article
status: Draft
series: "Governed Software Change"
series_order: 2
owner: "Andrew Spiteri"
last_reviewed: "2026-08-25"
review_cadence: on product or governance change
summary: "Why engineering authority should be versioned, reviewable, and repository-backed while indexes and model output remain disposable."
cis:
  stable_id: change-impact-studio:article:governance-should-live-with-the-code
---

# Governance should live with the code

Most software teams already have governance. It appears in architecture meetings,
security reviews, ticket templates, onboarding documents, pull-request comments,
runbooks, and the memories of experienced engineers.

The problem is not the complete absence of rules. It is that the rules often live far
from the work they govern.

When an implementation begins, the relevant requirement may be in a product document,
the compatibility promise in an API portal, the architecture decision in a wiki, the
security exception in a ticket, and the verification expectation in somebody's head.
A human may know how to assemble that picture. A coding agent usually sees whichever
fragment was included in its prompt.

Governance becomes useful to software delivery when it is versioned, addressable,
reviewable, and available at the same boundary as the change.

## Repository-backed does not mean documentation-heavy

Putting governance in the repository can sound like a proposal to create more files.
That is not the objective.

The repository is useful because it already provides properties that governance needs:

- identity through paths and stable references;
- version history through Git;
- review through ordinary diffs and pull requests;
- proximity to source, contracts, tests, and workflows;
- branching and release alignment;
- portability across tools and vendors; and
- availability to both humans and agents.

The goal is the minimum durable knowledge required to make change safe. A short,
current decision with a stable ID is more valuable than a large architecture document
that nobody can relate to the implementation.

## Durable meaning needs durable identity

A filename alone is a weak identity. Files move, titles change, and concepts are split
or consolidated. CIS gives governed documents and rule-level records stable IDs so
their relationships survive those changes.

A standard, for example, declares:

- its owner and lifecycle;
- targets and applicable technology stacks;
- review date and cadence;
- immutable document identity;
- stable rule IDs for each normative requirement;
- conformance mappings; and
- exception evidence.

This allows a plan or verification record to cite a precise obligation rather than a
vague instruction such as “follow the API standard.” A rule can be revised, retired,
or given stronger enforcement without losing its history.

Stable identity also makes disagreement visible. If two files claim the same ID, or a
catalog entry points to a missing document, validation fails. The system does not
silently guess which authority was intended.

## Canonical and derived knowledge are different

Repository-backed governance does not mean every useful artifact belongs in Git.
Change Impact Studio separates canonical meaning from derived routing state.

Canonical records include:

- product and technical intent;
- standards and decisions;
- governed references and inventories;
- change proposals, impact dispositions, plans, and acceptance evidence; and
- workflow definitions and reviewed learning history.

Derived state includes:

- file index cards;
- graph databases and query caches;
- model caches and usage records;
- workflow checkpoints and logs;
- agent envelopes and imported result snapshots;
- diagnostic analysis; and
- learning proposals awaiting review.

The test is simple:

> If `.cis/local/` were deleted, would any durable product meaning or approval disappear?

The answer must be no.

Derived state can make canonical knowledge easier to find and use. It must not become
the only location of a decision or requirement. Otherwise a cache rebuild can alter
meaning, and a local tool becomes an accidental source of truth.

## Lifecycle is part of the meaning

A generated document can look polished before anybody has reviewed it. A discovered
API row can look precise while several governance fields are still unknown. A model
proposal can look authoritative because it uses confident language.

Lifecycle labels prevent presentation quality from being mistaken for authority.

Useful distinctions include:

- **Draft:** content exists but remains under review;
- **Active:** the current reviewed authority;
- **Stale:** earlier authority no longer matches its governed baseline or source;
- **Deprecated or retired:** retained for history but not the current direction;
- **Proposed:** derived evidence or interpretation awaiting disposition; and
- **Completed or accepted:** a lifecycle transition backed by required evidence and authority.

Automation may detect that an Active document is stale. It may not restore Active
without the required human authority. Detection and approval are different operations.

## The catalog is a routing contract

CIS documentation roots include a `catalog.yml`. The catalog records stable document
identity, path, type, lifecycle, and authority. It is not a content-management database.
It is a small routing contract that lets tools and agents answer:

- Where is the canonical document?
- What kind of document is it?
- Is it current or still a draft?
- Does it carry authority or only route readers?
- Does its front matter agree with its catalog identity?

This makes ordinary repository documentation machine-navigable without moving the
content into a proprietary store.

## Governance must travel with change

Repository-backed governance is most valuable when it changes alongside behavior.
If an endpoint changes, the applicable specification, API inventory, compatibility
baseline, tests, and operational evidence should be reviewed in the same change.

That does not mean every change edits every document. It means documentation impact is
explicitly considered and any required updates are part of the approved work. A plan
that covers code but omits an affected contract is incomplete, even if the code passes.

This is also why external trackers remain projections in CIS. Jira or GitHub Issues may
be excellent coordination surfaces, but the durable task contract and evidence should
not disappear when an issue is renamed, closed, or moved between providers.

## The practical benefit for agents

Agents work better when authority is explicit and addressable. Repository-backed
governance lets a task cite:

- the exact feature specification and digest;
- accepted impact IDs;
- the applicable rule IDs;
- resolved decisions;
- target files or components;
- non-goals and prohibited scope;
- validation commands; and
- the evidence required for completion.

This is more reliable than relying on an agent to rediscover architecture and policy
from broad source searches. It is also more portable: the task survives a change of
agent, editor, or model.

## Takeaway

Governance is most effective when it is close enough to the code to be versioned,
reviewed, discovered, and verified with the change it controls.

Keep durable meaning in repository-owned records. Keep derived routing and analysis
disposable. Give every material rule and decision a stable identity and truthful
lifecycle. That turns governance from background knowledge into usable engineering
state.

## Canonical CIS sources

- [Product intent](../specs/product-intent-spec.md)
- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [Documentation governance standard](../standards/documentation-governance-standard.md)
- [Documentation inventory and validation](../specs/documentation-inventory-and-validation-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)

