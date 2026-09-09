---
title: "Context Packs: Enough, but Not Everything"
type: article
status: Draft
series: "Repository Knowledge and Context"
series_order: 4
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on context-routing change
summary: "How task-specific context packs combine exact sources, excerpts, relationships, and provenance without sending an executor the whole repository."
cis:
  stable_id: change-impact-studio:article:context-packs-enough-not-everything
---

# Context packs: enough, but not everything

A coding task rarely needs an entire repository. It needs the right contract, the owning
component, relevant implementation, related tests, applicable decisions, and the
constraints that shape the requested outcome.

A context pack packages that bounded view.

The constraint is not merely token count. Attention is an engineering resource for both
people and models. Every irrelevant file competes with the rule, exception, or failure
condition that actually controls the task. Excess context can make a result less reliable
even when the consumer technically accepts it.

## Start with a question

Context selection should begin with an explicit task or query:

- explain an API contract;
- find callers of a symbol;
- identify tests for a component;
- prepare one approved work item; or
- trace a requirement to implementation and evidence.

Without a question, “relevant context” expands until it becomes a repository dump.

## Include authority and evidence

A useful pack contains more than file excerpts. It should identify:

- repository and baseline;
- stable IDs and canonical paths;
- document lifecycle and authority;
- selected graph relationships;
- evidence method and confidence;
- bounded source excerpts;
- omitted or truncated material; and
- freshness of the derived state used for routing.

This lets the consumer distinguish a reviewed specification from an inferred relationship
and open the original source when necessary.

The pack should also explain why each item was selected. “Included because it implements
the target operation” is more useful than an unexplained list of files. Selection reasons
make omissions reviewable and allow a later pack builder to improve its routing rather
than copying a mysterious bundle.

## Select in layers

A practical selection strategy is:

1. resolve the exact task, contract, symbol, or document identity;
2. load its canonical source;
3. traverse only the relationship types useful to the question;
4. select the owning implementation and focused tests;
5. add applicable standards, decisions, and workflow constraints; and
6. stop when the declared depth, size, or evidence boundary is reached.

This produces an explainable pack. Every included source has a reason.

## A bounded example

For a task that changes invitation expiry, the initial roots might be the approved work
item and the stable identity of the invitation contract. A two-hop traversal could add
the owning service, persistence abstraction, public response contract, cleanup workflow,
and focused tests. The pack might then include:

- the exact requirement and resolved lifecycle decision;
- the current API and data dictionary rows;
- the handler and expiration policy implementation;
- the cleanup job contract;
- tests that cover issue, redeem, revoke, and expire behavior;
- the security and observability obligations; and
- explicit exclusions such as account linking and unrelated session policy.

It should not automatically include every authentication source file, the entire database
schema, all workspace repositories, or every document mentioning “time.” Those may become
relevant if evidence expands the scope, but they are not justified by the initial question.

## Excerpts are routing aids

Bounded excerpts reduce noise and token usage, but they can remove important surrounding
context. A pack must keep the source path and line or section identity so the executor
can inspect the full file before changing it.

An excerpt is never a replacement for the source. It is an invitation to the right part
of the source.

Excerpts also need boundary markers. A consumer should know whether it received a complete
document section, a line window, a table row, or truncated output. Silent truncation is
particularly dangerous near exception clauses and negative requirements.

## Preserve non-goals

Task context should include explicit non-goals and prohibited scope. Otherwise a model
may find a related concern and assume it should be redesigned.

For example, a task that changes cache invalidation may need the public endpoint policy,
handler, query abstraction, cache implementation, and tests. It may explicitly exclude
authentication protocol routes and unrelated persistence refactoring.

Knowing what not to do is part of useful context.

## Expansion is a governed event

Bounded context must not become a reason to ignore new evidence. If implementation reveals
an undocumented consumer, the executor should preserve the discovery and request context
expansion. The impact or plan may need review before work continues.

This is different from allowing the executor to search and change anything it finds.
Context can expand; authority and scope do not expand silently with it.

## Respect privacy and provider routes

A pack prepared for local use is not automatically authorized for a remote model. CIS
suppresses likely sensitive files and requires explicit remote authorization for the
exact content-bearing operation. Credentials and secrets are invalid inputs even when
they appear relevant.

The same rule applies to product-definition authoring and agent execution: source/provider
selection and disclosure are explicit, while the executor receives only its selected
task contract and context rather than the whole graph or repository.

Provider choice changes the permitted context boundary, not the canonical task.

For remote routes, authorization should apply to the concrete source set rather than a
general belief that the repository is safe. A later pack that adds a configuration file
or diagnostic excerpt is a new disclosure boundary. Local routing remains the default
when classification or sensitivity is uncertain.

## Measure compactness honestly

CIS can estimate the difference between selected excerpts and complete selected sources.
That provides a defensible directional token-saving estimate. It does not prove that the
smaller pack improved correctness.

Pack quality should be evaluated through outcomes: fewer broad searches, less
rediscovery, correct scope, fewer missing impacts, and successful validation.

## Recognize a poor pack

A pack is probably too broad when most files have no stated relationship to the task,
when the consumer cannot identify the controlling requirement, or when large generated
artifacts dominate the content. It is probably too narrow when the implementation target
appears without its contract, tests, ownership, or non-goals.

The best evaluation occurs after delivery. Unexpected changes, repeated searches, missed
validation, and requests for missing authority show where routing failed. Those facts can
improve the next pack without turning the current pack into a permanent source of truth.

## Takeaway

Give an executor enough context to understand the authority, boundaries, implementation,
and evidence for one task. Keep source paths and provenance. Expose omissions and
truncation. Respect provider privacy boundaries.

The best context pack is not the largest. It is the smallest pack that supports the
right engineering decision.

## Canonical CIS sources

- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [File index cards](../specs/file-index-card-spec.md)
- [`cis context pack`](../manual/cis_context_pack.md)
- [AI routing profile](../references/ai-routing-profile.md)
