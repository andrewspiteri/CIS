---
title: "Repository Knowledge Is More Than Source Code"
type: article
status: Draft
series: "Repository Knowledge and Context"
series_order: 1
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
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
- feature specifications and acceptance criteria;
- architecture decisions and rejected alternatives;
- standards, policies, and exceptions;
- API, event, command, permission, configuration, and data contracts;
- source files, symbols, packages, and dependencies;
- unit, integration, contract, browser, and architecture tests;
- build, release, deployment, and operational workflows;
- change dossiers and verification evidence; and
- agent guidance and reusable delivery skills.

Each source answers a different question. Code shows implementation. A specification
shows intended behavior. A standard shows the expected way of satisfying policy. A test
shows one verified assertion. A decision explains why one option was chosen over another.

Repository understanding requires their relationships, not one preferred format.

## Implementation cannot safely recreate intent

Reverse-engineering intent from source is tempting. Existing code is concrete, current,
and available. But implementation contains compromises, defects, migrations, experiments,
and legacy behavior. Treating all of it as desired intent turns accidental behavior into
policy.

The reverse inference may still be useful. CIS can discover facts and propose missing
documentation or relationships. The proposal must remain distinct from reviewed meaning.
A human decides whether the implementation represents an invariant, tolerated debt, or
behavior that should be removed.

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

