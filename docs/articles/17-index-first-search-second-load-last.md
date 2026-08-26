---
title: "Index First, Search Second, Load Last"
type: article
status: Draft
series: "Repository Knowledge and Context"
series_order: 8
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
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

## Search with a narrower hypothesis

Once the index identifies likely components or document families, text and symbol
searches operate on a smaller scope. A search for `AccessLink` inside the owning API
component is more meaningful than the same search across dependencies, test output,
generated clients, and archived documentation.

Exclusion rules should prune vendor, build, release, generated, cache, and sensitive
trees before matching.

## Query relationships

Search finds occurrences. Graph queries find callers, tests, owners, contracts, and
dependencies. Use the stable identity from the matched source to ask relationship
questions rather than inferring them from nearby text.

Relationship evidence and confidence determine whether the result can drive impact or
only suggest further review.

## Open the source last—but always open it

Loading last does not mean trusting summaries. Before making a factual claim or editing
a file, open the canonical document or implementation source. Check its current content,
lifecycle, and surrounding context.

The routing layers reduce how many sources must be opened. They do not remove the need
to verify them.

## Measure routing quality through outcomes

Possible token savings can be estimated by comparing compact routing output with the
selected source sizes. More important measures are:

- broad searches avoided;
- fewer unrelated files loaded;
- correct source selected on the first attempt;
- reduced repeated discovery;
- fewer missed contracts or tests; and
- no sensitive content sent to an unauthorized provider.

## Takeaway

Do not start every task by pouring the repository into a search or model context.

Reuse a local index, narrow the hypothesis, query typed relationships, then open the
authoritative sources needed for the task. Index first, search second, load last.

## Canonical CIS sources

- [File index cards](../specs/file-index-card-spec.md)
- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [`cis index find`](../manual/cis_index_find.md)
- [Feedback loop](../specs/feedback-loop-spec.md)
