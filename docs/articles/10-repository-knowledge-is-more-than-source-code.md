---
title: "Repository Knowledge Is More Than Source Code"
type: article
status: Active
series: "Repository Knowledge and Context"
series_order: 1
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on context-model change
summary: "A repository contains intent, contracts, decisions, tests, workflows, and operational knowledge that source analysis alone cannot recover."
cis:
  stable_id: change-impact-studio:article:repository-knowledge-beyond-source
---

# Repository knowledge is more than source code

Source code is indispensable evidence about a software system. It shows what can be
compiled or interpreted, which symbols exist, how components call each other, and where
implementation decisions have accumulated.

It does not explain the whole system.

Source alone rarely tells a reader why a behavior exists, which stakeholder outcome it
serves, whether a compatibility promise is still active, which exception was approved,
or what operational evidence is required before release. Those facts live across several
knowledge surfaces.

## The repository is an engineering record

A governed repository can contain:

- product and technical intent;
- product-definition questionnaires, solution architecture, diagrams, and delivery maps;
- feature specifications and acceptance criteria;
- architecture decisions and rejected alternatives;
- standards, policies, and exceptions;
- API, event, command, permission, configuration, and data contracts;
- governed dictionary indexes and source-evidence projections;
- source files, symbols, packages, and dependencies;
- unit, integration, contract, browser, and architecture tests;
- build, release, deployment, and operational workflows;
- change dossiers and verification evidence; and
- agent guidance and reusable delivery skills.

Each source answers a different question. Code shows implementation. A specification
shows intended behavior. A standard shows the expected way of satisfying policy. A test
shows one verified assertion. A decision explains why one option was chosen over another.

Repository understanding requires their relationships, not one preferred format.

## Different records answer different kinds of truth

It helps to separate three questions that are often collapsed during delivery:

| Question | Best starting evidence | Important limitation |
|---|---|---|
| What should the product do? | Active requirements, specifications, decisions, and approved policy | Authority may be stale or incomplete |
| What does the system currently do? | Source, configuration, deployed contracts, and runtime evidence | Observed behavior may be accidental or defective |
| What has been demonstrated? | Tests, verification records, reviews, and operational evidence | Evidence proves only the conditions it actually covered |

A repository becomes governable when those answers can disagree visibly. If a test
demonstrates behavior that contradicts an Active requirement, the test does not silently
rewrite the requirement. If production evidence reveals behavior absent from the
specification, the observation becomes a reconciliation question. If a decision record
is superseded, its historical rationale remains useful without controlling new work.

This is why “the code is the documentation” is too blunt. Code is often the strongest
evidence of implementation, but it is a poor container for stakeholder authority,
rejected alternatives, review state, and residual risk.

## Implementation cannot safely recreate intent

Reverse-engineering intent from source is tempting. Existing code is concrete, current,
and available. But implementation contains compromises, defects, migrations, experiments,
and legacy behavior. Treating all of it as desired intent turns accidental behavior into
policy.

The reverse inference may still be useful. CIS can discover facts and propose missing
documentation or relationships. The proposal must remain distinct from reviewed meaning.
A human decides whether the implementation represents an invariant, tolerated debt, or
behavior that should be removed.

Current CIS product-definition preparation makes that boundary explicit. It can consolidate
bounded observations from product-owned repositories into Draft dictionaries and use
implementation and tests as authoring evidence. Dependency repositories are excluded from
product inference, and observed behaviour never becomes stakeholder intent merely because it
was found in code.

Consider a public endpoint that reads through a cache. Source analysis may prove the
route exists, identify the query handler, and show a database client further down the
call chain. It cannot decide whether the data was meant to be public, how long it may be
cached, which consumers rely on its shape, or whether a temporary bypass was approved.
Those answers may live in product intent, an API inventory, a caching standard, a
compatibility baseline, and a time-bounded exception. The implementation is understood
only when those records can be connected.

## Tests are evidence, not names

A test named `ShouldRejectExpiredInvite` appears to verify expiration behavior. The
actual assertion might only check an HTTP status, omit the database state, or bypass the
production route entirely.

CIS therefore keeps test evidence tied to source location and extraction method. It does
not infer verification merely from a test filename or method name. A relationship between
a requirement and a test is stronger when it is declared, observed through a recognized
contract, or reviewed by a human.

## Documentation needs structure without becoming a database

Markdown is useful because humans can read and review it with ordinary Git tools. CIS
adds a small catalog contract: stable document identity, path, type, lifecycle, and
authority. Governed tables can add stable row identities for APIs, permissions, packages,
or traceability.

This is enough structure for tools to route and validate knowledge without moving every
sentence into a proprietary schema.

Useful repository knowledge normally has five properties:

- **identity:** a stable ID survives renames and allows other records to refer to it;
- **ownership:** a named role or person is responsible for meaning and review;
- **lifecycle:** Draft, Active, deprecated, and withdrawn records do not carry equal weight;
- **provenance:** readers can see whether a statement was declared, discovered, inferred,
  or verified; and
- **traceability:** intent can be followed toward implementation and evidence without
  pretending that every link is equally strong.

Structure should support those properties and no more. A readable Markdown requirement
with stable metadata is often preferable to a large opaque database row. A governed
inventory table is preferable when each contract item needs its own lifecycle and stable
identity. The form follows the authority and query need.

## Knowledge changes with delivery

Repository knowledge is not initialization output that can be forgotten. A delivered
feature may change:

- the intended behavior;
- a public contract;
- component ownership;
- a dependency or package;
- the tests that support acceptance;
- an operational workflow; or
- the guidance needed by the next executor.

Those changes should be reviewed alongside implementation. Otherwise the repository
gradually becomes easier to execute and harder to understand.

## A practical knowledge review

Before approving material work, a reviewer can ask:

1. Which document establishes the intended outcome?
2. Which contracts or standards constrain the solution?
3. Which source and configuration show the current implementation?
4. Which decisions explain non-obvious boundaries or exceptions?
5. Which tests and operational checks will demonstrate the change?
6. Which of those records are current, and which are only discovered or proposed?
7. What must be updated together if the change is accepted?

The objective is not to require a document for every line of code. It is to ensure that
material meaning does not exist only in source archaeology, a private conversation, or
the memory of the last maintainer.

## Documentation is part of the delivered change

A feature that alters a contract but leaves the API inventory unchanged has delivered a
split repository. The same is true when ownership changes without updating the module
map, a new permission lacks a governed dictionary entry, or a workflow begins producing
evidence that no verification record knows how to consume.

CIS treats reconciliation as ordinary delivery work. Graphs and indexes are rebuilt from
the changed repository, while canonical documents move only through reviewable edits.
That keeps the knowledge system useful to the next human or agent rather than merely
accurate at the moment initialization ran.

## Takeaway

Source code is one essential layer of engineering knowledge. Reliable change also needs
intent, decisions, contracts, standards, tests, workflows, and evidence.

Treat the repository as a connected engineering record. Keep each kind of knowledge in
the form best suited to its authority, then make the relationships queryable.

## Canonical CIS sources

- [Product intent](../specs/product-intent-spec.md)
- [Documentation inventory and validation](../specs/documentation-inventory-and-validation-spec.md)
- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [Traceability matrix specification](../specs/traceability-matrix-spec.md)
- [Reference governance and drift](../specs/reference-governance-and-drift-spec.md)
- [High-level product-definition wizard](../specs/high-level-product-definition-wizard-spec.md)
