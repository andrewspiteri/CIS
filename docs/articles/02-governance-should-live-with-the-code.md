---
title: "Governance Should Live with the Code"
type: article
status: Active
series: "Governed Software Change"
series_order: 2
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on product or governance change
summary: "Why engineering authority should be versioned, reviewable, and repository-backed while indexes and model output remain disposable."
cis:
  stable_id: change-impact-studio:article:governance-should-live-with-the-code
---

# Governance should live with the code

Most software teams already have governance. It appears in architecture meetings,
security reviews, ticket templates, onboarding pages, pull-request comments, runbooks,
API portals, chat threads, and the memories of experienced engineers.

The problem is rarely a complete absence of rules. It is that the rules live far from
the work they govern, carry uneven authority, and change on a different schedule from
the software.

When implementation begins, the business requirement may be in a product document,
the compatibility promise in an API portal, the architecture decision in a wiki, the
security exception in a ticket, and the operational constraint in somebody's head. A
well-connected engineer may know how to assemble that picture. A new team member or
coding agent usually sees whichever fragments happen to be included in the task.

Governance becomes usable engineering state when it is versioned, addressable,
reviewable, and available at the same change boundary as the software. That is why
Change Impact Studio (CIS) keeps durable engineering meaning in repository-owned
records while treating indexes, graphs, caches, workflow runs, and model output as
disposable aids.

The principle sounds simple:

> Keep the authority close enough to the implementation that both can be reviewed and
> changed together.

Applying it well requires more than putting a collection of Markdown files into Git.
It requires a clear answer to four questions:

1. Which records carry durable authority?
2. How are those records identified and found after they move?
3. How does their lifecycle affect what they are allowed to mean?
4. Which useful artifacts must remain derived rather than becoming a second source of
   truth?

## “In the repository” is not the same as “authoritative”

A file does not become an authority merely because it is committed. Repositories
contain experiments, outdated notes, examples, generated output, local configuration,
historical decisions, and drafts awaiting review. Treating every tracked file as
equally authoritative replaces one ambiguity with another.

CIS separates several properties that are often collapsed:

| Property | Question |
|---|---|
| Location | Where can the record be retrieved? |
| Identity | What durable thing is this record? |
| Type | What role does the record play? |
| Lifecycle | Is it proposed, draft, active, stale, deprecated, or withdrawn? |
| Authority | Does it establish meaning, route to meaning, derive from meaning, or propose a change? |
| Provenance | Which source, baseline, evidence, and review support it? |

A draft specification and an active specification can share a directory without
carrying the same authority. A navigation page may be committed and reviewed but still
only route readers to canonical sources. A graph node may describe a canonical document
while the graph database itself remains derived. An article may be the canonical version
of that article without defining CIS product behavior.

Repository-backed governance therefore means that durable authority is explicitly
represented in the repository. It does not mean that the repository automatically
grants authority to everything it contains.

## Why the repository is a useful governance boundary

The repository is not the only place where people collaborate, and it should not absorb
every conversation or operational system. It is useful because it already provides
several properties that durable engineering governance needs.

### Governance can be versioned with behavior

Git can show which version of a requirement, decision, standard, or plan accompanied a
particular version of the implementation. A reviewer can inspect changes to behavior
and changes to its governing records within the same branch or review boundary.

This matters when meaning changes over time. “The API standard says this” is incomplete
without knowing which revision of the standard applied to the baseline being changed.
Repository history makes that relationship inspectable.

Git history is still evidence, not interpretation. A commit can show that text changed;
it cannot prove why the change was correct or whether the person approving it had the
right authority. The repository provides the versioning substrate. The governed records
must still preserve owner, lifecycle, rationale, evidence, and review.

### Governance can travel through ordinary review

Repository-owned records appear in diffs. A product or technical authority can review
an altered requirement, a developer can see the resulting implementation change, and
an assurer can compare both with the planned evidence.

This does not require every authority to become a source-code reviewer. Review can be
assigned by document type, risk, ownership, and affected surface. The benefit is that
the accepted records and implementation share a traceable change boundary rather than
being updated through unrelated systems with no explicit reconciliation.

### Governance can remain portable

Plain, versioned records can be read by people, command-line tools, editors, coding
agents, continuous-integration jobs, and future systems. The durable meaning does not
depend on one model provider, issue tracker, editor extension, or hosted knowledge base
remaining available.

Portability is not only a vendor concern. It is also a continuity concern. A task should
remain understandable after the chat that created it is gone, after the person who knew
the background moves teams, and after a local index is deleted and rebuilt.

### Governance can inherit the repository's trust boundary

Repository policy, access control, branch protection, review, and release practices can
also protect governed engineering records. CIS remains local-first and does not transmit
repository content to a remote model merely because a document is indexed. Remote use
requires an explicit governed route and user authorization, as described by the
[system context](../specs/system-context-spec.md).

This does not make Git a secret store. Credentials, private keys, certificates, and
sensitive runtime data still do not belong in canonical Markdown. Repository-backed
governance stores the durable decision or policy, not every piece of data involved in
executing it.

## Repository-backed does not mean documentation-heavy

Moving governance closer to code can sound like a proposal to create more files and
more forms. That is not the objective.

The useful target is the minimum durable knowledge required to understand, constrain,
and verify material change. A short, current decision with a stable identity is more
valuable than a long architecture document that cannot be connected to the system. A
small standards table with explicit rule IDs is more actionable than a broad statement
that everyone should “follow best practice.”

CIS initializes an intentionally small documentation structure: navigation, a catalog,
architecture decisions, specifications, references, and change dossiers. Additional
folders are introduced only when the repository needs them. The initialization contract
also inspects existing content, previews intended changes, reports collisions, and does
not silently overwrite reviewed files. Those boundaries are defined in the
[repository initialization specification](../specs/cli-and-repository-initialisation-spec.md).

The question for each document is not “could we write this down?” It is:

> Will this record preserve meaning, authority, evidence, or a reusable contract that
> would otherwise be lost or repeatedly rediscovered?

If the answer is no, the information may belong in a generated view, a local cache, an
external operational system, or nowhere at all.

## Durable meaning needs stable identity

Paths help people locate files, but paths are weak identities. Documents move. Titles
change. One specification may be split into several records, and several records may be
consolidated. A path tells a tool where something is now; it should not be the only way
another record explains what that thing is.

CIS uses repository-scoped stable IDs for governed documents and stable record or rule
IDs inside them. In the graph model, document identity comes from catalog and front
matter before any inferred path identity. A path is retained as a location.

That distinction allows a requirement, plan, conformance mapping, or verification row
to cite a durable obligation rather than a fragile filename or prose phrase.

Consider a standard. A governed standard can identify:

- the document's immutable stable ID;
- its owner, lifecycle, review date, and cadence;
- the targets and technology stacks to which it applies;
- one stable rule ID for every normative requirement;
- the route by which each rule is checked; and
- any reviewed exception, scope, expiry, and compensating controls.

The repository's
[documentation governance standard](../standards/documentation-governance-standard.md)
requires these properties for standards. This means a plan can cite a rule such as
`CIS-STD-DOC-008` rather than saying only “make sure documentation is compliant.” The
rule can later receive stronger enforcement or be retired without reusing its identity
or erasing its history.

Stable identity also makes conflict visible. Duplicate IDs, duplicate paths, missing
catalog targets, and a mismatch between front matter and routing metadata should not be
resolved by guessing. Ambiguity at an authority boundary is a validation finding.

## Lifecycle is part of meaning

A generated document can look polished before anyone has reviewed it. A discovered API
row can look precise while important fields remain unknown. A model proposal can sound
decisive even though it has no approval authority.

Presentation quality is not lifecycle.

Useful lifecycle distinctions include:

- **Proposed:** an observation or interpretation awaits human disposition;
- **Draft:** a durable document exists but remains under review;
- **Active:** the current reviewed authority for its declared scope;
- **Stale:** an earlier authority no longer matches its governed source or baseline;
- **Deprecated or withdrawn:** the record remains for history but is no longer the
  current direction; and
- **Accepted or completed:** a governed transition has the required evidence and human
  authority.

The exact vocabulary varies by document type, but the principle does not: readers and
tools must be able to distinguish existence from authority.

Automation can detect that an active record is stale. It may identify a changed digest,
missing source, unresolved contradiction, or expired review date. That does not grant
automation the authority to restore the record to Active. Detection, proposal, review,
and approval are distinct operations.

This distinction is especially important for model-generated material. A model may
draft an excellent specification or propose a plausible relationship. Until the
appropriate authority reviews it, fluency does not change its lifecycle.

## The catalog is a routing contract

A CIS documentation root contains `catalog.yml`. The catalog records each document's
stable identity, repository-relative path, type, lifecycle status, and authority class.
It is not a content-management database, and it does not replace the Markdown content.
It is a compact routing contract.

That contract lets a person or tool ask:

- Where is the record with this stable identity?
- What kind of record is it?
- Is it active, draft, or another lifecycle state?
- Does it carry canonical authority, route to another source, derive from source
  evidence, or propose a change?
- Does the document's front matter agree with the catalog?

The [documentation inventory and validation specification](../specs/documentation-inventory-and-validation-spec.md)
defines deterministic inventory and strict validation over this structure. Validation
detects duplicate identities and paths, repository escapes, missing targets, malformed
metadata, and uncataloged Markdown. It reports ambiguity instead of silently applying a
classification.

This is a modest but important property. Ordinary Markdown remains readable without
CIS, while the catalog makes it machine-addressable without moving the source of truth
into a proprietary database.

## Canonical and derived knowledge must remain separate

Repository-backed governance does not mean every useful artifact belongs in Git. CIS
draws a firm boundary between durable meaning and rebuildable state.

| State | Typical examples | Authority boundary |
|---|---|---|
| Canonical repository state | Intent, specifications, standards, ADRs, governed references, change dossiers, decisions, plans, evidence, accepted learning | May carry durable meaning according to type, lifecycle, and reviewer authority |
| Derived local state | Index cards, graph databases, query caches, model caches, task envelopes, workflow checkpoints, diagnostics, proposals awaiting review | Helps route, analyse, or execute; cannot become the only location of durable meaning |
| External projections | GitHub Issues, Jira items, editor views, dashboards, generated reports | Coordinate or present canonical state; cannot silently approve or rewrite it |
| External evidence systems | Git, CI, test runners, deployment and observability platforms | Establish bounded facts; do not automatically grant product or acceptance authority |

The most useful test is destructive in theory but simple in meaning:

> If `.cis/local/` were deleted, would any requirement, decision, approval, exception,
> or acceptance record disappear?

The answer must be no.

CIS's graph illustrates the boundary. It can represent canonical documents, requirements,
contracts, implementation files, tests, and evidence-backed relationships. It stores
provenance, confidence, lifecycle, and source locators. Yet the graph remains derived.
A graph node marked `canonical` is a projection of a canonical source; the database row
does not become that source. Deleting and rebuilding the graph cannot change product
meaning. The full contract is described in the
[context model and local graph specification](../specs/context-model-and-graph-spec.md).

This separation prevents several subtle failures:

- a cache rebuild cannot alter an approved decision;
- a model re-run cannot silently replace a human disposition;
- a local database migration cannot erase the only copy of a requirement;
- a deleted workflow checkpoint cannot turn completed evidence into an unknown claim;
- a new search index cannot make a proposed relationship look canonical; and
- one developer's workstation cannot hold authority unavailable to the rest of the
  repository.

Derived state can be sophisticated. It can use SQLite, full-text indexes, compiler
analysis, hashes, confidence levels, and model proposals. Its technical richness does
not increase its authority.

## Provenance matters as much as content

Two statements can contain the same words and deserve different trust. One may be an
approved requirement; another may be a model summary of an old ticket. Repository-backed
governance should preserve the distinction rather than flattening both into “context.”

For material facts, provenance can include:

- the repository and stable source identity;
- the exact path or record locator;
- the Git baseline or observed commit;
- a content digest used to detect staleness;
- whether the fact was declared, deterministically discovered, inferred, or proposed;
- the extractor or method that produced it;
- confidence and the reason for that confidence; and
- any human confirmation, rejection, rationale, and timestamp.

Provenance does not prove that every source is correct. It makes the basis inspectable.
It also prevents a deterministic observation from being mislabeled as a canonical
declaration and a high-confidence model suggestion from being presented as a confirmed
relationship.

## Governance must travel with change

Repository-backed governance is most valuable when it changes alongside behavior.
If an endpoint changes, the applicable feature specification, API inventory,
compatibility baseline, standards, tests, and operational evidence should be considered
within the same governed change.

That does not mean every code change edits every document. It means documentation and
contract impact are explicitly assessed. Required updates become approved work, and a
conscious “no documentation impact” conclusion can be challenged during review.

This alignment solves a common timing problem. When documentation is scheduled as a
separate clean-up activity, it is reviewed after the implementation has already fixed
the design in code. Review becomes transcription instead of governance. When meaning
and behavior travel together, a disagreement in the specification can still change the
implementation before it is accepted.

Branching also becomes useful. A branch can contain a proposed change dossier, draft
decisions, altered specifications, implementation, and verification evidence without
making any of them current on the main line. Their lifecycle remains explicit while
ordinary repository review shows how the pieces fit together.

After acceptance, history preserves which authority accompanied which implementation.
It does not remove the need to reconcile conflicts or supersede old decisions, but it
makes those transitions inspectable.

## External trackers should coordinate, not govern

Jira and GitHub Issues are valuable collaboration surfaces. Teams use them for queues,
assignment, labels, milestones, notifications, and cross-team visibility. They are not
necessarily the right source of durable engineering authority.

Issues are renamed, reformatted, closed, moved, archived, or deleted. Workflows and
custom fields differ between providers. A remote status can mean “the assignee finished
their part,” “the pull request merged,” or “the product owner accepted the outcome.”
Mapping any of these automatically to governed completion loses meaning.

CIS therefore treats tracker items as projections of canonical task records. Stable CIS
identity, dependencies, criteria, gates, evidence obligations, and deferrals remain in
the repository. Remote changes are detected and reconciled; they do not silently rewrite
canonical Markdown or grant approval. The
[external tracker synchronization specification](../specs/external-tracker-synchronization-spec.md)
defines that direction and conflict model.

This is not an argument against trackers. It is an argument for giving each system the
job it performs best:

- the repository preserves durable engineering meaning and its history;
- the tracker coordinates attention and external work;
- synchronization preserves identity and exposes drift; and
- humans resolve conflicts where authority or intent differs.

## Multi-repository governance needs ownership, not duplication

“Governance lives with the code” becomes more complicated when a product spans web,
service, mobile, data, and infrastructure repositories. Copying the same intent and
standards into every repository creates drift. Centralizing every local fact in one
governance repository creates stale shadows of participant code.

A better model separates product authority from repository ownership:

- a workspace authority can own one product's business requirements, technical direction,
  cross-repository decisions, and change dossiers;
- each owned or dependency repository retains its local components, contracts, standards,
  implementation facts, and validation;
- repository-qualified stable identities prevent two local records from becoming one
  accidental fact;
- derived graphs can federate evidence without transferring ownership; and
- cross-repository findings remain explicit about which repository supplies each fact.

The principle is not that every authority file must sit next to every source file in the
same directory. It is that authority must live in the repository that owns it and remain
addressable from the change it governs.

A shared API contract, for example, needs one declared owner. Consumer repositories can
reference that contract and preserve their own compatibility evidence. They should not
silently fork the contract into several competing authorities.

## The practical benefit for humans and agents

Repository-backed governance helps anyone entering unfamiliar work. A bounded task can
cite:

- the exact feature specification and content digest;
- the accepted impact and decision IDs;
- applicable standard and rule IDs;
- target repositories, components, files, or contracts;
- explicit non-goals and prohibited scope;
- validation obligations; and
- evidence required before completion can be considered.

A human developer no longer has to reconstruct the entire authority chain from meetings
and chat. A coding agent no longer has to guess which of several similarly named files
is current. A reviewer can trace an implementation claim back to the approved source
and determine whether the source changed during execution.

This also makes execution provider-neutral. The task survives a switch of model, agent,
editor, tracker, or team member because its meaning does not live in the private context
of the original executor.

Repository-backed authority does not eliminate discovery. It improves the quality of
discovery by giving tools stable routes and truthful metadata. Search can still find
candidate evidence, and a graph can still reveal relationships. The executor can tell
which results are canonical, derived, proposed, stale, or incomplete.

## Common failure modes

Several patterns look repository-backed while weakening the authority model.

### One large governance document

A monolithic handbook may be tracked and reviewed, but it is difficult to route,
reference precisely, apply by technology or change surface, and keep current. Separate
stable records and rule IDs make impact and ownership clearer.

### Generated Markdown treated as approved

Generation can create a draft or exact rendering. It cannot create stakeholder meaning.
Generated content needs a truthful lifecycle and, when material, provenance to the
sources and method that produced it.

### A database as the only source

A local graph or hosted knowledge tool may make governance easy to query. If deleting or
rebuilding it removes a decision or changes what is approved, the query system has
become an accidental authority.

### Paths used as permanent identity

Paths are necessary locators, but renames then break every relationship or encourage
duplicate records. Stable IDs allow locations to change while identity and history
remain intact.

### Everything marked canonical

If navigation pages, drafts, model proposals, generated reports, source observations,
and approved specifications all carry the same authority, the label becomes meaningless.
Authority must describe the role of the record, not its importance to the author.

### Governance updated after delivery

Documentation written after implementation often describes what the code now does rather
than testing whether it does what was intended. Governing records should participate in
the change early enough to affect it.

### External status treated as acceptance

A closed issue, green build, or merged pull request is useful evidence of a transition in
another system. It does not automatically prove that governed scope, risk, and completion
were accepted.

## A practical adoption path

Teams can move toward repository-backed governance incrementally.

### 1. Name the documentation authority

Choose an explicit repository-relative documentation root. Do not let tools guess
between several existing `docs` folders or overwrite established material. Record the
repository identity and selected root in configuration.

### 2. Inventory before generating

Discover existing Markdown, decisions, standards, references, and contracts. Classify
what already carries authority and what merely offers historical or routing value. Report
ambiguity instead of replacing it with new files.

### 3. Create a small catalog

Give material documents stable IDs, paths, types, lifecycle states, and authority
classes. Validate duplicate identities, missing targets, and uncataloged records under
the governed root.

### 4. Make lifecycle truthful

Do not mark generated or newly imported material Active because it appears complete.
Use Draft or Proposed until the appropriate owner reviews it. Mark stale and superseded
records visibly instead of deleting inconvenient history.

### 5. Separate durable and disposable state

List which files must survive a local-state deletion. Keep indexes, graphs, caches,
diagnostics, workflow checkpoints, and model proposals outside the canonical authority
boundary until a reviewed process promotes an exact outcome.

### 6. Bind governance to real changes

For the next material feature, review documentation and contract impact before planning.
Carry affected records, decisions, implementation, and verification through the same
governed change boundary.

### 7. Project outward deliberately

Use trackers, dashboards, and editor clients for visibility and coordination. Preserve
stable links back to the canonical record and treat incoming divergence as a conflict to
review, not as an instruction to overwrite repository meaning.

## The repository is necessary, but not sufficient

Repository-backed governance does not guarantee good governance. A repository can hold
stale specifications, weak decisions, contradictory standards, and approvals by the
wrong people. Git can preserve the history of a bad choice just as effectively as a good
one.

The repository supplies durable location, versioning, review, portability, and proximity
to implementation. Control still depends on truthful lifecycle, clear ownership,
provenance, appropriate review, independent evidence, and human acceptance at material
authority boundaries.

Nor should every source of evidence be copied into the repository. CI logs, production
telemetry, security platforms, external contracts, and provider systems may remain where
they are. The canonical record should preserve the durable decision, relevant locator,
baseline, result, and rationale required to understand their role without turning Git
into an operational data warehouse.

The goal is not to make the repository contain everything. It is to make sure durable
engineering meaning does not exist only somewhere else.

## Takeaway

Governance is most effective when it is close enough to the code to be versioned,
reviewed, discovered, and reconciled with the change it controls.

Keep durable meaning in the repository that owns it. Give material documents, rules,
decisions, and records stable identities and truthful lifecycle. Use a catalog as a
small routing contract. Keep indexes, graphs, caches, model output, tracker state, and
other projections disposable or explicitly subordinate to canonical sources.

That turns governance from scattered background knowledge into usable engineering
state—without confusing “stored in Git” with “authorized to define the product.”

## Canonical CIS sources

- [Product intent](../specs/product-intent-spec.md)
- [System context](../specs/system-context-spec.md)
- [CLI and repository initialization](../specs/cli-and-repository-initialisation-spec.md)
- [Context model and local graph](../specs/context-model-and-graph-spec.md)
- [Documentation governance standard](../standards/documentation-governance-standard.md)
- [Documentation inventory and validation](../specs/documentation-inventory-and-validation-spec.md)
- [External tracker synchronization](../specs/external-tracker-synchronization-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
