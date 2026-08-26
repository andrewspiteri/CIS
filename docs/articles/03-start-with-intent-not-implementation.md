---
title: "Start with Intent, Not Implementation"
type: article
status: Draft
series: "Governed Software Change"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-08-25"
review_cadence: on product or governance change
summary: "Why business outcomes and technical direction must be current before change analysis and implementation planning begin."
cis:
  stable_id: change-impact-studio:article:start-with-intent-not-implementation
---

# Start with intent, not implementation

A well-implemented feature can still be a failure if it solves the wrong problem.

That sounds obvious, yet many delivery workflows begin with an implementation-shaped
request:

- add a table;
- expose an endpoint;
- introduce a queue;
- replace a library;
- ask a model to summarize a result; or
- create a new screen.

Each instruction already contains a solution. The engineer or coding agent is invited
to optimize that solution before the team has established the outcome, constraints,
success evidence, and technical boundaries.

AI makes this failure mode more expensive precisely because it makes the requested
implementation easier. The wrong ladder can be climbed very quickly.

## Intent exists at more than one level

Change Impact Studio distinguishes business intent from technical intent.

Business intent describes:

- the problem and desired outcome;
- actors and affected processes;
- scope and non-goals;
- functional and quality requirements;
- constraints and success measures;
- assessed source evidence; and
- open stakeholder questions.

Technical intent describes:

- architectural principles and ownership;
- runtime and trust boundaries;
- data and consistency expectations;
- API, integration, and compatibility direction;
- security and privacy constraints;
- operational, migration, and recovery expectations;
- quality and verification requirements; and
- technical decisions with explicit gates.

These documents serve different authorities. Technical direction cannot quietly
invent business meaning, and business approval does not resolve every architectural
choice.

## Existing documents are evidence, not automatic authority

Many repositories already contain requirement documents, design documents, roadmaps,
or feature specifications. Finding them is useful, but discovery does not prove that
they are current or complete.

A document can be detailed and still describe an abandoned direction. A feature
specification can be current for one repository and incomplete at the workspace level.
A business requirements document copied into several repositories can leave the team
without a clear authority when the copies diverge.

CIS treats discovered business documents as source evidence. A human classifies them
as Adopted, Reference, or Rejected and records the rationale. One authority repository
owns the canonical workspace business requirements. Participant repositories retain
their local documents without competing for workspace authority.

This preserves useful history while avoiding a dangerous shortcut:

> “We found a document, therefore the decision has been made.”

## Approval must be bound to content

An approval that only records a status is weak. If the document changes later, the
status can continue to look valid even though the reviewed content no longer exists.

CIS approvals bind to normalized content digests and relevant repository or graph
baselines. The system can therefore detect:

- edits to approved meaning;
- changes in assessed source evidence;
- participant-set changes;
- incompatible baseline drift;
- missing required sections; and
- unresolved decisions.

When material meaning changes, the document becomes effectively stale. Automation can
identify that condition, but it cannot manufacture a new reviewer or rationale to
restore approval.

Not every baseline change is semantic. A participant graph may receive a new build ID
while the technical direction remains unchanged. CIS can reconcile that provenance
without demanding a duplicate approval. The key question is whether the authority and
meaning changed, not whether a timestamp moved.

## Intent should gate downstream work

Intent becomes operational when it controls what may happen next.

In a governed CIS workspace, current technical intent follows current business
requirements and precedes change-dossier creation, impact analysis, and planning.
Commands that create or plan governed work check this readiness.

This prevents a team from producing a detailed task plan while foundational questions
remain unresolved. Examples include:

- Which repository owns the contract?
- Is backward compatibility required?
- Which trust boundary is changing?
- Can data be eventually consistent?
- What is the rollback requirement?
- Which public surfaces require caching?
- Who accepts the operational risk?

If those questions affect the plan, deferring them until implementation transfers
architecture authority to the executor.

## A good change proposal carries the intent forward

Intent documents define enduring product and technical direction. A change proposal
turns that direction into one bounded outcome.

A useful proposal records:

- the observable outcome;
- why it matters;
- exact baseline;
- initial impact roots;
- constraints;
- in-scope and out-of-scope boundaries; and
- outcome-level acceptance evidence.

Compare two versions of the same request.

Weak:

> Send failed test output to a model and show a summary.

Intent-led:

> Passing workflows remain deterministic and make no model call. Failed runs retain
> raw logs, produce bounded failure evidence, prefer an approved local route, and block
> sensitive content from remote providers. Existing test pass/fail authority remains
> deterministic. Hosted escalation requires explicit authorization and recorded usage.

The second proposal does not prescribe every class or function. It establishes the
outcome and boundaries that implementation must respect.

## Intent reduces—not increases—implementation friction

Teams sometimes avoid intent work because it appears slower than beginning to code.
That comparison ignores the cost of rediscovery and rework.

Current intent gives implementers:

- a smaller decision surface;
- fewer contradictory sources;
- clearer escalation points;
- stable acceptance criteria;
- better context routing;
- safer deterministic planning; and
- less reason to infer product policy from existing code.

The work moves from “decide everything while implementing” to “execute within reviewed
boundaries and escalate genuine exceptions.” That is especially valuable for coding
agents, which otherwise fill missing intent with plausible assumptions.

## Takeaway

Implementation should begin after the team has established the outcome and the
technical boundaries that materially shape it.

Start with intent. Assess discovered documents rather than automatically trusting
them. Bind approval to exact content and evidence. Detect drift. Carry current authority
forward when nothing material changed, and stop when it did.

Speed is useful only after the direction is worth accelerating.

## Canonical CIS sources

- [Product intent](../specs/product-intent-spec.md)
- [Business requirements governance](../specs/business-requirements-governance-spec.md)
- [Workspace technical-intent governance](../specs/technical-intent-governance-spec.md)
- [Technical intent](../specs/technical-intent-spec.md)
- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
