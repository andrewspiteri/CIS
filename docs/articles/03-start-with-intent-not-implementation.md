---
title: "Start with Intent, Not Implementation"
type: article
status: Active
series: "Governed Software Change"
series_order: 3
owner: "Andrew Spiteri"
last_reviewed: "2026-09-22"
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

## Intent is a product baseline, not one document

Change Impact Studio distinguishes business intent from technical intent, then coordinates
both with architecture, contracts, experience direction, and a delivery map. The high-level
product-definition journey brings those authorities together before the repeatable feature
loop without replacing their canonical Markdown or individual ownership boundaries.

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

The coordinated definition progresses through eight concerns:

1. project foundation and repository evidence;
2. business definition;
3. technical direction;
4. solution architecture and diagrams;
5. contracts and governed dictionaries;
6. experience direction and UI preview;
7. a high-level delivery map; and
8. consolidated review and activation.

Each concern remains reviewable in its owning artifact. Consolidated activation binds the
exact set so that a feature cannot quietly combine business intent from one revision with
technical direction or contracts from another.

## Existing documents are evidence, not automatic authority

Many repositories already contain requirement documents, design documents, roadmaps,
or feature specifications. Finding them is useful, but discovery does not prove that
they are current or complete.

A document can be detailed and still describe an abandoned direction. A feature
specification can be current for one repository and incomplete at the workspace level.
A business requirements document copied into several repositories can leave the team
without a clear authority when the copies diverge.

CIS treats discovered business documents in product-owned repositories as source
evidence. A human classifies them as Adopted, Reference, or Rejected and records the
rationale. One authority repository owns the canonical product business requirements.
Dependency repositories remain separately governed context and do not silently become
product requirements.

This preserves useful history while avoiding a dangerous shortcut:

> “We found a document, therefore the decision has been made.”

## Approval must be bound to content

An approval that only records a status is weak. If the document changes later, the
status can continue to look valid even though the reviewed content no longer exists.

CIS approvals bind to normalized content digests and relevant repository or graph
baselines. The system can therefore detect:

- edits to approved meaning;
- changes in assessed source evidence;
- product-owned repository-set changes;
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

In a governed CIS product workspace, current technical intent follows current business
requirements and the governed technical questionnaire. The activated product-definition
baseline precedes the repeatable feature loop, while current technical intent remains a
direct gate for change-dossier creation, impact analysis, and planning. Commands that create
or plan governed work check this readiness.

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

## Worked contrast: define the invitation before its implementation

This is a hypothetical feature outline, not a CIS serialization or an approved Friends
Todo decision.

### Starting with implementation

> Add an `Invitations` table, create `POST /api/invitations`, email a link that expires
> after 24 hours, and add an invitation screen.

That request is concrete, but it has already selected storage, transport, expiry, and
experience before establishing the behavior. An executor still has to guess who may
invite, whether a link can be reused or forwarded, what revocation means, what happens
when the recipient already has access, and which events require an audit trail. A
polished implementation would hide rather than resolve those assumptions.

### Starting with intent

The reviewed feature definition begins with what must become true:

- a signed-in list owner can invite another person and revoke an outstanding invitation;
- expired or revoked access cannot be accepted;
- authorization and auditability remain enforceable across the invitation lifecycle;
- public or pre-authentication routes retain their applicable security and caching
  boundaries; and
- anonymous list administration, bulk invitations, and unrelated sharing redesign are
  outside this change.

It also keeps unresolved choices visible:

- whether acceptance is single-use, reusable until expiry, or account-bound;
- the approved expiry period and cleanup behavior;
- whether forwarding is allowed; and
- how an invitation behaves when the recipient already has access.

Those choices must be answered by the appropriate product and technical authorities
before dependent planning. Only then should the team decide whether the implementation
needs a table, which operations belong in the API, which screens are required, and how
expiry is enforced.

The intent-led version does not make implementation vague. It prevents implementation
details from masquerading as stakeholder decisions.

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
- [High-level product-definition wizard](../specs/high-level-product-definition-wizard-spec.md)
- [Technical intent](../specs/technical-intent-spec.md)
- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
