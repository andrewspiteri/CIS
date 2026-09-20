---
title: "Index First, Search Second, Load Last"
type: article
status: Active
series: "Repository Knowledge and Context"
series_order: 8
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on file-routing change
summary: "A layered repository-discovery strategy that reduces broad searches, context volume, and unnecessary model work."
cis:
  stable_id: change-impact-studio:article:index-search-load
---

# Index first, search second, load last

Repository exploration often begins with a broad recursive search. It is fast and
familiar, but it treats every matching path as equally likely to matter. Agents then
open many files, consume large contexts, and rediscover the same routes in later tasks.

A better discovery sequence is:

```text
Index
  → focused search
  → governed reference inventory
  → relationship query
  → open canonical sources
  → load bounded excerpts
```

## Index for routing

CIS file index cards record compact local metadata such as likely purpose, concepts,
symbols, sensitivity, and content hash. Finding cards is cheaper than loading candidate
files and can reuse unchanged results across tasks.

Cards remain disposable. They identify where to look; they do not support final factual
claims.

An index card should be small enough to compare many candidates and specific enough to
explain its ranking. Useful fields include repository-relative path, content hash,
language or document type, likely purpose, important symbols or concepts, sensitivity,
and the route that produced the summary. A changed hash makes the card stale; a renamed
file should not leave an invisible duplicate in the active index.

## Search with a narrower hypothesis

Once the index identifies likely components or document families, text and symbol
searches operate on a smaller scope. A search for `AccessLink` inside the owning API
component is more meaningful than the same search across dependencies, test output,
generated clients, and archived documentation.

Exclusion rules should prune vendor, build, release, generated, cache, and sensitive
trees before matching.

Search is strongest when it tests a hypothesis. After an index suggests that invitation
policy belongs to the access component, a focused search can locate the stable contract,
handler, and tests. If the hypothesis fails, broaden deliberately—first across the owned
repository, then across owned workspace participants, and only then into bounded
dependencies where authority permits.

## Use governed inventories before inference

When the question concerns a contract or domain concept, check the applicable reference
dictionary before inferring meaning from occurrences. CIS can inventory and reconcile APIs,
commands, events, permissions, data, routes, configuration, packages, ownership, and
traceability while preserving lifecycle and source provenance. Draft source observations
remain distinct from reviewed contract meaning.

Inventories are especially valuable for concepts whose implementation names vary. A
permission such as `lists.invite` may appear through an attribute, policy registration,
frontend capability, and test fixture. The governed permission row provides the identity;
search and graph evidence reveal its implementations.

## Query relationships

Search finds occurrences. Graph queries find callers, tests, owners, contracts, and
dependencies. Use the stable identity from the matched source to ask relationship
questions rather than inferring them from nearby text.

Relationship evidence and confidence determine whether the result can drive impact or
only suggest further review.

## Work through one routing example

For “change the response when an invite has expired,” the sequence might be:

1. use the index to locate invitation, API-problem, and access-policy candidates;
2. search those bounded paths for the stable invite and problem identifiers;
3. inspect the API and problem dictionaries for current lifecycle and ownership;
4. query relationships to find the handler, consumers, and verifying tests;
5. open the canonical requirement, contract rows, implementation, and focused tests; and
6. prepare excerpts only after the required source set is understood.

The process is not rigid. If a stable ID is already known, begin with the inventory or
graph. If the repository has no current index, a bounded text search is the correct
fallback. The principle is to use the cheapest trustworthy routing evidence before
loading large content.

## Open the source last—but always open it

Loading last does not mean trusting summaries. Before making a factual claim or editing
a file, open the canonical document or implementation source. Check its current content,
lifecycle, and surrounding context.

The routing layers reduce how many sources must be opened. They do not remove the need
to verify them.

Source verification catches errors that summaries cannot. A card may describe the file's
old purpose, a match may occur only in a comment, an inventory row may be Draft, or a graph
edge may be possibly-stale. Opening the source restores its surrounding conditions,
negative requirements, and lifecycle.

## Measure routing quality through outcomes

Possible token savings can be estimated by comparing compact routing output with the
selected source sizes. More important measures are:

- broad searches avoided;
- fewer unrelated files loaded;
- correct source selected on the first attempt;
- reduced repeated discovery;
- fewer missed contracts or tests; and
- no sensitive content sent to an unauthorized provider.

## Know when broad search is appropriate

Broad search remains useful for proving the absence of a literal within a declared tree,
finding uncatalogued legacy names, or recovering when routing metadata is missing. It
should still exclude generated and sensitive paths, report its scope, and avoid treating
no match as semantic proof.

The anti-pattern is not recursive search itself. It is using an unbounded search as the
first and only model of repository relevance.

## Make routing reproducible

A repeatable route records the query, selected repository scope, index or graph build
identity, filters, omitted results, and opened canonical sources. Another maintainer can
then understand why a file entered the context pack and whether the route remains current.

That record also creates useful learning evidence. If every task must bypass the index to
find the same component, the index specification or provider needs improvement. If a
common search returns hundreds of generated files, exclusions need correction. Routing
quality becomes an observable engineering property instead of a personal browsing skill.

## Takeaway

Do not start every task by pouring the repository into a search or model context.

Reuse a local index, narrow the hypothesis, query typed relationships, then open the
authoritative sources needed for the task. Index first, search second, load last.

## Canonical CIS sources

- [File index cards](../specs/file-index-card-spec.md)
- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [`cis index find`](../manual/cis_index_find.md)
- [Feedback loop](../specs/feedback-loop-spec.md)
- [Reference governance and drift](../specs/reference-governance-and-drift-spec.md)
