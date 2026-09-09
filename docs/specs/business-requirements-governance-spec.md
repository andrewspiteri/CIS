---
title: "Business Requirements Governance"
type: specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-05"
review_cadence: "on BRD workflow change"
cis:
  stable_id: change-impact-studio:spec:business-requirements-governance
---

# Business requirements governance

## Purpose

CIS establishes one canonical business requirements document for a multi-repository
product while preserving discovered BRDs, domain-equivalent product-design documents
such as game design documents (GDDs), and development feature specifications as
evidence. Discovery never proves currency or semantic absorption.

## Authority model

`cis workspace init` binds one workspace to one product in one ecosystem and registers
exactly one documentation repository with role `authority`. Other repositories have role
`participant` and explicit `owned` or `dependency` participation. The canonical BRD
lives at `<authority-documentation-root>/specs/business-requirements.md`. BRDs and feature
specifications in other product-owned repositories remain candidate evidence even when
they are detailed or appear current. Dependency-repository documents belong to another
product authority and are excluded from BRD discovery.

Legacy unqualified registries are rejected. BRD commands require an explicit product
identity and authority.

## Intake states

When source BRDs exist, discovery records stable candidate identity, repository, path,
content hash, and detection signals. Every source starts `Unreviewed`. When no source
exists, initialization creates the same canonical structure and explicitly records the
absence. Both paths begin `Review Required`.

Domain-specific product authority can be BRD evidence without using BRD terminology.
The deterministic intake recognizes `type: product-design`, GDD filenames, and Game
Design Document headings. These remain assessed source evidence; CIS does not silently
rename or replace the originating product-design document.

Humans classify each source as `Adopted`, `Reference`, or `Rejected`, provide rationale,
and author business outcomes, scope, actors, capabilities and processes, functional and
quality requirements, constraints, success measures, traceability, and open questions.
CIS does not generate stakeholder decisions from implementation evidence.

## Business-reader presentation

Imported-product authoring includes bounded, digest-cached implementation and test snapshots
for explicitly selected owned repositories. The author traces behaviour from entrypoints
through service logic, validations, state changes, integrations and related tests. Dictionary
projections and repository documents provide navigation and context. Code is observed
behaviour, not stakeholder intent or evidence of deployed configuration. Every indexed
implementation area has hidden coverage evidence or a concrete gap; entries are validated
against the snapshot's actual file paths and roles. A coverage row does not establish
semantic completeness. An independent review with authoring evidence receives the exact
snapshot and must examine narrated behaviour and omissions against it. Snapshot files are
never executed; bounded discovery excludes dependencies, tool state and credential files,
redacts likely credential literals and reports other source omissions.

The canonical BRD must provide a connected, plain-language narrative of how the product
works for business and product stakeholders, including nontechnical readers. Describe
participants, purpose, complete journeys, business rules, operating variations, exceptions
and outcomes. Stable requirement identities and business-readable acceptance conditions
remain available for downstream planning. Technical inventories and repeated extraction
caveats must not replace the product explanation.

All links, URLs, source IDs, anchors, paths, hashes and technical evidence mappings belong
inside HTML comments. The narrative must stand on its own without rendered citation markers
or source tables. Detailed mappings remain in comments under Traceability; CIS validation
and source-drift detection continue to read the Markdown source. Controller-owned baseline,
source-assessment and feature-traceability blocks retain their original markers and values
inside reversible evidence comments. Their hidden presentation preserves the business digest.

Initial independent review must treat missing or unusable narrative as a major issue to
address in that pass. Readability cannot be deferred until stakeholder questions are answered.
Bounded closure reviews retain their exact approved scope. These presentation rules do not
claim that declaration projections alone establish complete implementation behaviour.

## Open-question resolution

Agent-authored numbered questions remain unresolved human decisions. `cis brd questions
list` gives them deterministic `BRD-Q-NNN` identities. `cis brd questions guidance`
selects bounded, deterministic excerpts from the canonical BRD. `cis brd questions
suggest` may add digest-bound derived suggestions under `.cis/local/`; unsupported answers
remain explicitly absent. Accept-ready suggestions require at least medium model confidence and
one or more valid citations to the displayed bounded context. Low-confidence, uncited, malformed,
or stale model output cannot be presented as a supported answer. Remote disclosure requires
authorization, and no suggestion has
stakeholder authority. `cis brd questions answer` records one explicitly accepted or edited
substantive answer, human actor, and UTC timestamp in the canonical Open questions table.
The first answer normalizes the entire numbered list without changing question text;
repeated identical answers are idempotent. Partial progress is valid and retained.

Every recognized question must have an answer before BRD validation can pass. Answering
or changing a question clears any prior BRD approval and returns the catalog entry to
Review Required. CIS and agents may organize and present questions, but only supplied
human answers may resolve them. A section containing `None` or `No open questions` has no
question records; arbitrary narrative is not silently converted into a stakeholder
decision. Advisory suggestions may organize existing evidence but may not invent a missing
owner, date, target, policy, legal conclusion, technology choice, or scope decision.

After every question is answered, `cis agent incorporate brd-questions` binds the complete
governed answer table to a digest and performs an isolated one-file revision. The answers are
exact stakeholder authority; the provider reflects them in relevant BRD sections without
inventing technical choices or expanding scope. CIS requires the exact digest and question
identities in structured evidence, preserves the Open questions table and protected managed
blocks, and rejects concurrent or out-of-scope changes. The result is a new
`PRODUCT/BRD-QUESTION-REVISION` producer and therefore requires a different-provider
independent review before validation and approval.

## Independent review and remediation

An agent-authored or agent-revised BRD may be reviewed by a provider different from its
latest successful producer. Review is read-only advisory evidence with stable
`BRD-REV-NNN` findings; it does not create human authority.

`cis brd review init` creates a canonical Markdown disposition record bound to the exact
review-result and BRD digests. The client presents the complete recommendation set together.
A named human may approve a finding as written, approve exact edited recommendation text,
or atomically approve every remaining recommendation as written with `cis brd review
accept-all`; acceptance does not require a separate rationale. Recommendations already
edited and approved are preserved by batch approval. The final decision mechanically locks
the exact set when none remain pending; there is no second aggregate human approval.
`cis brd review approve` exists only to recover complete legacy records that predate
automatic locking. Legacy rejected findings remain explicit guardrails. Agents cannot create
these decisions or approve the set.

Review freshness is a governed CLI classification, not a client-side whole-file hash guess.
`cis brd review freshness` validates the successful run and its retained read-only BRD
snapshot. An exact match is `current`. A later write that changes only Open-question answer,
human actor, or answer timestamp fields is `question-answers-only` and remains covered by the
review as an intermediate question-resolution state. Once those answers are incorporated into
the BRD's business sections, the resulting revision supersedes that review. Question identity
or wording changes, question-set changes, and every edit outside the answer surface are also
`stale` and require another independent review.

When approved findings exist, `cis agent revise brd` requires a provider different from
the reviewer and applies only their exact approved recommendation text in an isolated one-file workspace.
It preserves BRD frontmatter, baseline and traceability blocks, source identity/hash/assessment/
ordering/provenance, and existing human question answers. An approved recommendation may
change only the rationale text of an existing source row. A successful retained revision that
fails deterministic copy-back is revalidated and reused without another paid provider run.
CIS records the revised BRD digest against the approved dispositions. A secondary review by
a provider different from the reviser is closure-only: it checks approved findings, legacy
rejected guardrails, regressions introduced by the bounded revision, and protected evidence or
scope changes. It cannot reopen broad completeness, repeat pre-existing observations, or propose
unrelated refinements. A clean result closes the review cycle before open-question resolution
and final BRD approval continue. When
all findings are rejected, no synthetic revision or secondary review is required.

## Currency and baselines

The canonical BRD records product-owned participant graph builds. Dependency graph
freshness is reported as integration-context warning rather than a blocker to product
business authority. The authority graph is deliberately
excluded from the embedded baseline because the BRD is itself an authority-graph input;
including it would create a self-referential build identity. Candidate documents in the
authority repository remain content-hashed source evidence.

Approval captures the current product-owned participant baselines and a normalized digest of the
approved canonical content excluding approval metadata. Later canonical content edits,
participant build changes, candidate changes, missing evidence, or validation gaps make
an approved BRD effectively `Stale`. Deterministic checks may detect staleness but may
never restore `Active`.

## Feature-specification feedback

Feature specifications created during delivery are downstream design evidence that can
reveal refined, changed, or previously missing business intent. A copied feature template
declares `type: feature-specification`. After its repository graph is rebuilt, discovery
assigns the document a stable source ID and content hash.

`cis brd status` makes an Active BRD Stale when that evidence is unresolved.
`cis brd reconcile` adds new or changed feature specifications to the managed source
assessment table, preserves human-authored sections, resets affected assessments to
`Unreviewed`, and clears prior approval. It never performs an unreviewed semantic merge.

Humans may classify a feature specification as `Adopted`, `Reference`, or `Rejected`.
`Adopted` means its relevant business intent has been incorporated into the canonical
BRD. Validation therefore requires the feature specification's managed `BRD-SRC-*` ID
in the Traceability section before the BRD can be approved again.

## High-level backlog derivation

After the BRD and technical intent are Active and current, `cis brd backlog build` derives
one stable high-level outcome for every structured functional requirement. It accepts the
canonical table representation (`BRD-FR-*`) and the agent-authored bold narrative-bullet
representation (`BR-FR-*`). The same compatibility applies to `BRD-NFR-*` and `BR-NFR-*`
global obligations. Narrative labels supply readable outcome names while their complete,
including line-wrapped, statements remain acceptance intent. Both functional identity forms
normalize to `HLT-FR-*`; presentation differences do not justify changing the Active BRD.

Validation enforces one-to-one functional-requirement coverage, stable identities, repository
routing, supported frontend classifications, and acyclic dependencies. Derivation is a
mechanical transformation and grants no backlog approval.

Product-owned participant repositories are the normal implementation-routing candidates. A greenfield
workspace containing only its authority repository uses that repository as the deterministic
implementation target; an empty repository list is invalid. Once owned participants are
imported, subsequent idempotent builds route against them. Dependency repositories are
never implementation targets; changes to them require their own product authority.

## Human authority

Only `cis brd approve` can record `Active`, and it requires human reviewer identity and
rationale. Agents may discover, initialize, reconcile, edit proposed content under
human direction, and validate. They must not assess evidence or approve autonomously.

## Safety and idempotence

Reconciliation preserves human sections and unchanged source assessments. New or
changed BRD or feature-specification evidence resets only affected assessments and clears approval. Managed IDs,
hashes, baseline rows, and block markers remain deterministic. An unmanaged canonical
path or catalog identity is a collision and is never overwritten.
