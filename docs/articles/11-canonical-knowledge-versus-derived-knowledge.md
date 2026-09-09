---
title: "Canonical Knowledge Versus Derived Knowledge"
type: article
status: Draft
series: "Repository Knowledge and Context"
series_order: 2
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on authority or storage change
summary: "A practical boundary between durable repository authority and rebuildable local routing, analysis, and execution state."
cis:
  stable_id: change-impact-studio:article:canonical-versus-derived-knowledge
---

# Canonical knowledge versus derived knowledge

Engineering tools generate useful state: indexes, graph databases, summaries, caches,
workflow runs, task envelopes, diagnostics, and model analysis. The difficult design
question is not whether to generate them. It is whether any of them are allowed to carry
durable meaning.

Change Impact Studio uses a strict boundary: canonical knowledge is reviewable repository
state; derived knowledge is disposable local state that can be rebuilt from evidence.

## What belongs in canonical state

Canonical records include facts and decisions that must survive a local cache deletion,
tool upgrade, or provider change:

- product and technical intent;
- standards and approved exceptions;
- architecture decisions;
- governed contract inventories;
- approved change scope and resolved decisions;
- task identity, dependencies, and acceptance criteria;
- design and verification approvals; and
- reviewed learning history.

These records belong in version-controlled Markdown or explicit structured repository
files. Humans can inspect the diff, review the lifecycle, and trace the authority.

Canonical does not mean infallible. It means that the record is the recognized place
where a particular decision or meaning must be reviewed. An Active specification can be
wrong; correcting it requires a visible governed change. A cache can be perfectly
accurate; it still cannot approve that correction.

Authority is therefore attached to responsibility and lifecycle, not to a file format.
A YAML workspace registry can be canonical because it declares product ownership. A
Markdown impact report can be derived because it is regenerated from an analysis. A row
in a reference inventory may be Active and authoritative while a neighboring discovered
row remains Draft evidence awaiting review.

## What belongs in derived state

Derived state exists to improve routing, performance, or execution:

- SQLite graph generations;
- per-file routing cards;
- model response caches;
- sanitized usage ledgers;
- workflow checkpoints and bounded logs;
- portable agent envelopes;
- imported result snapshots;
- diagnostic analysis; and
- unreviewed learning proposals.

CIS stores this material beneath `.cis/local/` where practical and excludes it from Git.
Deleting it can make the next operation slower, but it cannot erase a requirement,
decision, approval, or acceptance record.

Retention does not change that authority. CIS may inventory, archive, compact, restore, or
retrieve eligible local artifacts with manifests, hashes, containment checks, and recovery
rules. Those controls protect useful derived evidence; they do not promote a run log, cache,
or archive into canonical product meaning.

## Classify by consequence, not convenience

The most useful classification question is:

> What would it mean if two maintainers had different copies of this value?

If the difference changes product scope, ownership, compatibility, permissions, an
approved exception, or the definition of completion, the value needs canonical authority.
If the difference changes query speed, routing suggestions, cached output, or the ability
to resume a local operation, it is normally derived.

Some records contain both kinds of information and should be split. A workflow run may
hold thousands of disposable events but produce a concise canonical verification entry:
exact command, baseline, result, artifact digest, unavailable evidence, and reviewer
disposition. The event stream remains local; the durable conclusion remains reviewable.

## Why the distinction matters

When derived state becomes authoritative, several failure modes appear:

- rebuilding an index changes the apparent product meaning;
- a local database becomes more current than reviewed documentation;
- one developer's cache contains a decision nobody else can inspect;
- a model summary replaces the source it summarized;
- an external tracker closure becomes the only evidence of task completion; or
- changing provider invalidates the historical record.

The canonical/derived boundary makes these situations structurally incorrect rather
than merely discouraged.

Another failure mode is accidental promotion through repetition. If every engineer uses
the same generated summary, the summary can start to feel authoritative even though no
one reviewed its sources. Popularity does not change provenance. CIS keeps links and
digests so the source can be reopened and the derived view can be invalidated when its
inputs move.

## A canonical projection is still derived

A graph node may have authority `canonical` because it represents a catalogued
specification. The node is not itself the authority. It points to the source file and
retains its stable ID, path, content hash, and provenance.

This distinction allows graph extraction to improve without rewriting product history.
The graph can add a new relationship, change a schema, or rebuild storage while the
canonical document remains unchanged.

The reverse is also important. When a canonical document changes, the projection becomes
stale until it is rebuilt. Consumers should see that condition rather than unknowingly
querying an old graph. Freshness is a property of the projection; approval is a property
of the source.

## Lifecycle crosses the boundary carefully

Derived state may describe lifecycle, but it does not own lifecycle. An index can report
that a standard is Active because the standard says so. It cannot mark the standard
deprecated because an analyser found few references. A model can propose that a decision
has been superseded. Only the governed source and its authority can record that outcome.

This rule prevents a rebuild, tool upgrade, or changed model from altering which policy
controls delivery.

## Promotion requires review

Useful derived knowledge sometimes deserves to become canonical. A diagnostic pattern
may reveal a missing standard. A repeated impact surprise may justify a new relationship.
A resolved change decision may have durable architectural value.

Promotion is explicit:

1. preserve the source evidence and digest;
2. formulate the proposed canonical change;
3. identify the owning authority;
4. review conflicts and scope;
5. apply the change through ordinary repository governance; and
6. record the reviewer and rationale.

Copying model output directly into an Active document skips the step where meaning is
confirmed.

Promotion should normally create a small, inspectable canonical diff. The evidence may
be large, but the authorized conclusion should state what changes, why, who reviewed it,
which source digest it came from, and what future event would require another review.

## Operating the boundary as a team

Teams can make the distinction visible in everyday work:

- keep canonical records in ordinary reviewed repository paths;
- exclude local graphs, caches, runs, archives, and prompts from Git;
- make commands report when they are reading stale derived state;
- retain source paths and digests in projections;
- summarize durable evidence instead of committing raw logs;
- rebuild derived stores after canonical changes; and
- test that deleting `.cis/local/` cannot remove approved meaning.

This approach also makes tooling replaceable. A team can change databases, indexing
strategies, providers, or editor clients without migrating the authority model every
time.

## A useful deletion test

Ask of every generated store:

> If this directory disappeared now, what durable meaning would be lost?

If the answer includes a decision, requirement, approval, exception, or acceptance,
the boundary is wrong. Move that meaning into a reviewable canonical record and let the
generated store point to it.

## Takeaway

Canonical state owns durable meaning. Derived state makes that meaning easier to route,
analyse, execute, and verify.

Use content hashes and provenance to connect the two, but never allow convenience stores
to become invisible authorities.

## Canonical CIS sources

- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Feedback loop](../specs/feedback-loop-spec.md)
- [Documentation governance standard](../standards/documentation-governance-standard.md)
- [Local artifact retention and recovery](../specs/local-artifact-retention-spec.md)
