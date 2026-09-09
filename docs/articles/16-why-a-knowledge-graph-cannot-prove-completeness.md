---
title: "Why a Knowledge Graph Cannot Prove Completeness"
type: article
status: Draft
series: "Repository Knowledge and Context"
series_order: 7
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on graph or impact-completeness change
summary: "A graph can prove what it traversed and validated, but not that every semantic obligation exists in its model."
cis:
  stable_id: change-impact-studio:article:graph-cannot-prove-completeness
---

# Why a knowledge graph cannot prove completeness

A well-built engineering graph can reveal relationships that a developer would otherwise
miss. That power creates a dangerous temptation: treat a successful traversal as proof
that every impact has been found.

No graph can support that claim unless the world it models is already known to be complete.

Software delivery is an open-world problem. The absence of an edge means “the current
model has no included evidence for this relationship,” not “the relationship does not
exist.” Closed-world reasoning is appropriate inside carefully defined schemas; it is
unsafe when applied to stakeholder promises, runtime integrations, and organizational
knowledge.

## Some knowledge is absent

Material obligations may exist outside the repository:

- an undocumented customer promise;
- production behavior not represented by tests;
- a vendor constraint;
- a regulatory interpretation;
- an operational workaround;
- a planned but uncommitted migration; or
- knowledge held by a specialist.

The graph cannot traverse a node that does not exist.

Some knowledge is also intentionally excluded. Secrets, sensitive diagnostics, ignored
vendor trees, generated outputs, or repositories outside the authorized workspace should
not be pulled into a graph merely to make coverage look larger. Safe omission must remain
visible rather than being confused with evidence of no impact.

## Some relationships are ambiguous

Textual similarity, naming, and model interpretation can propose relationships, but
proposals contain false positives and false negatives. Even compiler evidence has a
boundary: dynamic dispatch, reflection, configuration, external calls, and generated
code can hide runtime relationships.

Confidence and extraction method help describe this uncertainty; they do not eliminate it.

False positives matter too. A broad graph can connect concepts through shared words,
framework conventions, or stale declarations and create an expensive impact set that
looks authoritative. Review must remove unsupported findings without teaching the system
that similar future evidence can always be ignored.

## Bounds are necessary

Unbounded graph traversal can become unusable. CIS sets explicit roots, relationship
states, directions, depth, confidence, and path-count limits. Cycles are handled safely.

When a bound truncates the result, truncation remains visible. The user can broaden the
search and rerun it. The tool does not relabel a partial traversal as complete.

Bounds should be chosen for the question. A depth of one may be appropriate for finding
the direct owner of an API operation and inadequate for assessing its consumers,
deployment workflow, and operational evidence. Increasing every limit by default only
moves the problem: it produces more paths without proving that the missing relationship
was represented.

## What completeness can honestly mean

CIS impact completeness means that, within the declared baseline and bounds:

- findings were discovered;
- every proposed finding received a disposition;
- accepted findings are known scope;
- deferred findings are not hidden; and
- traversal was not truncated.

This is workflow completeness, not semantic omniscience.

That distinction gives reviewers a useful contract. They can prove that the declared
analysis was executed, every resulting proposal was dispositioned, deferrals remain
visible, and no reported truncation was ignored. They cannot honestly sign a statement
that no person, system, or undocumented dependency could know anything else.

## Several completeness questions coexist

A change may be complete in one dimension and incomplete in another:

- **input completeness:** were all required repositories and authority records available?
- **traversal completeness:** did the configured query finish within its bounds?
- **review completeness:** did every proposed finding receive a disposition?
- **plan completeness:** does approved work cover every accepted impact?
- **evidence completeness:** did every required validation produce usable evidence?
- **semantic completeness:** does the model contain every real obligation?

CIS can enforce the middle dimensions strongly and expose evidence about the first. The
last remains an open-world human and organizational judgment.

## Validation and completeness are different

Graph validation can prove that identities are unique, endpoints exist, evidence is
well formed, paths are contained, inputs are fresh, and storage is structurally sound.
It cannot prove that the model includes every real dependency.

A green validator means the represented graph is valid. It does not mean reality has no
unrepresented edge.

## Human review remains necessary

Reviewers contribute knowledge the graph does not have. They can add roots, declare
relationships, reject misleading proposals, and identify external obligations. Surprises
found during implementation and verification should feed reviewed improvements to future
graph builds.

The system becomes more useful over time without ever claiming perfect coverage.

## Use surprises to improve the next baseline

If implementation discovers an unplanned consumer, preserve the actual path, contract,
and repository evidence. Review whether the gap came from a missing node, weak extractor,
stale graph, insufficient traversal bound, or undocumented external knowledge. The
durable correction may be a declaration, reference row, provider enhancement, planning
trigger, or operating procedure.

This learning improves expected recall. It does not retroactively prove that the next
graph is complete. A mature governance system gets better at showing what it knows and
what it does not know, rather than becoming more confident in an impossible guarantee.

## A reviewer’s completeness check

Ask which sources were included, which were unavailable or intentionally excluded, what
states and directions traversal accepted, whether any limit truncated output, how weak
relationships were handled, and which external specialists were consulted. Record that
boundary with the impact decision so later verification can distinguish a genuine
surprise from work that was knowingly deferred.

## Takeaway

Use an engineering graph to make evidence and relationships navigable. Ask it to show
its baseline, bounds, confidence, provenance, diagnostics, and truncation.

Do not ask it to prove that no unknown impact exists. Honest bounded completeness is a
stronger control than an impossible universal claim.

## Canonical CIS sources

- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [`cis impact completeness`](../manual/cis_impact_completeness.md)
- [`cis graph validate`](../manual/cis_graph_validate.md)
