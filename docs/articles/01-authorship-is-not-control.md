---
title: "Authorship Is Not Control"
type: article
status: Active
series: "Governed Software Change"
series_order: 1
owner: "Andrew Spiteri"
last_reviewed: "2026-09-22"
review_cadence: on product or governance change
summary: "Why faster code production increases the need to govern outcomes, authority, evidence, and acceptance."
cis:
  stable_id: change-impact-studio:article:authorship-is-not-control
---

# Authorship is not control

For much of software engineering's history, the person who wrote a change was also the
person most likely to understand it. Implementation forced that person to spend time in
the codebase, discover local conventions, inspect dependencies, make trade-offs, and
debug the consequences. Authorship was therefore treated as a practical proxy for
control.

It was only ever a proxy.

An engineer could understand the code and still misunderstand the outcome. A change
could be elegant inside one module while violating an external contract. It could pass
the tests that existed while missing the test that mattered. It could satisfy the ticket
while contradicting a product decision recorded somewhere else. Authorship made deep
engagement more likely; it did not guarantee that the right change had been made.

AI-assisted development makes this distinction impossible to ignore. Code can now be
produced, revised, and explained faster than a team can establish intent, retrieve
dispersed knowledge, examine impact, resolve material decisions, and verify the result.
The constraint moves away from typing and toward engineering judgment.

The important question is no longer:

> Who wrote the code?

It is:

> What controlled the meaning, scope, execution, evidence, and acceptance of the
> change?

That is the problem Change Impact Studio (CIS) is intended to address. CIS is not an
autonomous development agent. It is a local-first, repository-backed control layer for
governing a proposed software change from intent through acceptance. Its
[product intent](../specs/product-intent-spec.md) starts from a simple asymmetry:
implementation is becoming easier to produce than it is to understand, govern, and
verify.

## Four ideas that should not be confused

Discussions about AI-assisted development often use authorship, execution, authority,
and control as though they mean the same thing. They do not.

| Idea | The question it answers | What it does not prove |
|---|---|---|
| Authorship | Who or what produced the implementation? | That the requested outcome was correct |
| Execution | What work was performed against which instructions? | That the instructions covered the whole change |
| Authority | Who or what was permitted to establish meaning or accept a result? | That the implementation satisfied that authority |
| Control | What system kept intent, scope, decisions, execution, evidence, and acceptance aligned? | That every uncertainty disappeared |

A developer may author a change without owning the product decision behind it. A coding
agent may execute a well-bounded task without having authority to expand its scope. A
test runner may produce trustworthy evidence without having authority to accept the
residual risk. A reviewer may have acceptance authority but still need evidence from
the repository, tests, and affected specialists.

Control is therefore not a property of one participant. It is a property of the system
around the participants.

## Why authorship once looked like control

Traditional implementation was comparatively expensive. Even a modest feature could
require hours of reading, editing, compiling, testing, and correction. That effort
created friction, but it also created incidental opportunities to learn:

- a nearby handler revealed an authorization rule;
- a failing integration test exposed an undocumented contract;
- a database migration revealed an operational dependency;
- a reviewer remembered a decision that was absent from the ticket; or
- a deployment failure exposed an assumption that had never been written down.

None of this was an especially reliable governance system. It depended on time,
proximity, memory, and the right person noticing the right clue. Yet when implementation
was slow, the discovery and production phases often happened at roughly the same pace.
The person writing the code accumulated context while doing the work.

Generative tools change that balance. They can produce a plausible implementation
before the wider engineering system has supplied enough evidence to judge whether the
implementation should exist in that form. The code may arrive before the organization
has answered basic questions about product behavior, ownership, compatibility, data,
security, operations, or acceptance.

This does not make generated code inherently worse. It means that the historical proxy
has weakened. The speed or fluency of implementation says very little about the quality
of the control system that preceded and follows it.

## Local correctness is not outcome correctness

A coding agent is often very good at producing code that is locally convincing. Given a
route, handler, interface, and test pattern, it can extend all four consistently. It can
name the change clearly, explain its reasoning, and report that the relevant checks
passed.

The implementation may still be wrong at the product boundary.

Consider an apparently small request: expose a new unauthenticated endpoint that returns
product data. The visible implementation might involve only a controller, a query, and
a test. The actual engineering obligation may include:

- the business reason the data is public;
- the exact information that may be disclosed;
- API versioning and compatibility;
- error and empty-state behavior;
- caching, freshness, invalidation, and failure behavior;
- separation between the public route and persistence;
- rate limiting, monitoring, and abuse controls;
- frontend consumers and their fallback behavior;
- infrastructure or configuration changes;
- security, privacy, and operational review; and
- documentation and acceptance evidence.

An executor limited to the obvious source files cannot reliably infer all of these
obligations. Even a repository-wide search cannot establish stakeholder intent or prove
that the available documentation is complete. A graph can expose known relationships,
but it cannot prove that no relevant relationship is missing.

This is the central risk of ungoverned acceleration: a technically coherent answer can
be delivered before the problem has been bounded. The failure does not necessarily look
like broken code. It may look like a polished solution to an incomplete interpretation.

## Control is a chain of authority

Engineering control is not one approval at the end of a pull request. It is a chain in
which each link answers a different question and constrains the next:

```text
Intent
  → authoritative context
  → reviewed impact
  → resolved decisions
  → bounded work
  → constrained execution
  → independent evidence
  → human acceptance
```

### Intent establishes the outcome

Intent explains what should change, why it matters, who is affected, and which
constraints must remain true. A ticket title or prompt may initiate the conversation,
but it is not automatically sufficient authority. Material ambiguity belongs with the
product or technical authority that can resolve it.

Without intent, an executor can optimize only for the wording it received and the code
it can see. It cannot distinguish a deliberate constraint from an accidental pattern or
a current requirement from stale documentation.

### Context supplies bounded, attributable facts

Useful context includes more than source code. Requirements, specifications, standards,
architecture decisions, tests, workflows, API contracts, Git history, and operational
knowledge may all constrain the change.

Context also needs provenance. A generated summary, an old issue, and an approved
specification should not be presented as equally authoritative. CIS keeps durable
meaning in repository-owned files while treating indexes, graphs, caches, envelopes,
and other state beneath `.cis/local/` as derived and rebuildable. The distinction is
part of the [system boundary](../specs/system-context-spec.md), not merely a storage
preference.

### Impact turns context into reviewable scope

Impact analysis asks what may need to change against an exact baseline. Its findings are
proposals supported by evidence, not automatic declarations of completeness. A human
authority can accept, reject, or defer them with rationale. Unknowns remain visible
rather than being converted into confident prose.

This stage matters because planning too early freezes an untested interpretation into a
list of tasks. Reviewing impact first gives the team a place to identify missing
repositories, affected contracts, specialist reviews, and material uncertainties before
work is decomposed.

### Decisions settle choices before they become code

Some findings reveal a choice rather than a task: which compatibility strategy to use,
where a trust boundary belongs, how data should migrate, or who owns a cross-repository
contract. These questions should be recorded with options, evidence, rationale, and the
gate they block.

If the choice is left implicit, the executor makes it through implementation. That may
be convenient, but it transfers authority to the place where the decision is least
visible and hardest to review.

### Bounded work defines the execution contract

A bounded task states the objective, relevant authority, targets, dependencies,
non-goals, acceptance criteria, validation, evidence requirements, and the rule for
scope expansion. The task should remain understandable without depending on private
chat history.

This is more than prompt engineering. A good prompt can improve an answer; a governed
work contract defines what the executor is and is not authorized to do. If new impact is
discovered, the executor surfaces it. The executor does not silently absorb it and then
rewrite the meaning of completion.

### Execution produces a candidate, not an accepted outcome

A human, coding agent, deterministic generator, or workflow may perform the approved
work. Each is an executor with different strengths. None acquires product or acceptance
authority merely by producing the result.

Execution success means that a defined activity completed. It does not mean that the
definition was complete, the baseline remained current, every affected surface was
covered, or the residual risk is acceptable.

### Evidence tests the claim independently

Evidence should be gathered from the systems that can establish the relevant fact. Git
can identify what changed from a baseline. A compiler can establish whether the code
builds under a specific configuration. Tests can establish the behavior their cases
actually exercise. A contract comparison can identify a breaking API difference.

Each source has a boundary. A passing test suite cannot prove that every necessary test
exists. A clean build cannot prove that the product outcome is correct. An executor's
changed-file list cannot replace inspection of the actual Git diff.

### Acceptance remains a human decision

Acceptance asks whether the evidence satisfies the approved completion boundary and
whether the remaining risk is acceptable. It requires the reviewer, rationale,
timestamp, and residual risks to be explicit. It is separate from a successful workflow,
a merged pull request, or an external tracker status.

The [delivery and assurance specification](../specs/delivery-and-assurance-spec.md)
defines this separation for changes to CIS itself: the executor's claim, changed-file
list, test report, or tracker state is never authoritative on its own.

## What goes wrong when one prompt owns the whole chain

A request such as “understand this feature, decide the best design, implement it, run
the tests, and tell me when it is complete” appears efficient because it removes
handoffs. In practice, it collapses incompatible roles into one conversation.

The same executor is asked to:

1. interpret an outcome it did not authorize;
2. decide which sources deserve authority;
3. judge whether its own discovery was complete;
4. resolve choices that may belong to product, architecture, security, or operations;
5. define the scope it will later be measured against;
6. implement that scope;
7. select the evidence used to assess its own work; and
8. declare the result complete.

The problem is not that a model can never perform any of these activities. CIS explicitly
allows models and agents to discover, propose, analyse, draft, and implement. The problem
is allowing proposal, execution, and approval to become indistinguishable.

When the chain is collapsed, uncertainty tends to disappear in the wording. Assumptions
become design choices, design choices become implementation details, and implementation
details become evidence that the original interpretation was correct. A fluent final
summary can then conceal the absence of independent control.

## How Change Impact Studio separates the roles

CIS is designed around governed separation rather than autonomous completion. It keeps
intent, decisions, plans, reviews, and acceptance evidence in versioned,
repository-owned records. It derives local indexes and a typed graph to route context.
It binds impact and verification to exact baselines. It prepares bounded tasks for
humans or agents, and it compares observed Git and validation evidence with approved
scope.

The executor remains important, but it is one participant in a larger system:

```text
Approved bounded task
        ↓
Human, coding agent, or deterministic tool
        ↓
Candidate implementation and result claim
        ↓
Independent Git, contract, and validation evidence
        ↓
Human review and acceptance
```

This design also makes the executor replaceable. A task that works only because one
agent remembers an earlier conversation is not a durable task. Provider-neutral
preparation carries the current canonical path, digest, scope, evidence obligations,
and result contract so that another agent—or a human—can execute the same bounded work.

Result ingestion does not mark that work complete. As described in the
[execution and assurance contract](../specs/execution-assurance-and-learning-spec.md),
it validates the task envelope and appends evidence while leaving lifecycle transition
and completion to the governed plan and human authority.

## Human control should not mean human repetition

Weak governance often responds to risk by adding approvals everywhere. That creates
ceremony, not necessarily control. If a reviewer must confirm every deterministic copy,
format conversion, or unchanged derivation, genuine decisions become difficult to see
among routine clicks.

CIS therefore distinguishes new judgment from exact authority carry-forward. An
existing approval can be reused deterministically only while its source, digest,
baseline, scope, reviewer, rationale, and evidence remain exactly within the approved
boundary. The workflow must return to review when content is stale, ambiguous, expanded,
low-confidence, exceptional, or blocked by a new decision.

This leads to a more useful division of responsibility:

| Participant | Appropriate responsibility | Authority it must not acquire |
|---|---|---|
| Human authority | Establish meaning, resolve material choices, approve exceptions, accept risk and completion | None beyond the person's actual role |
| Deterministic tool | Preserve exact authority, validate structure, compute diffs, run defined checks | New stakeholder meaning or risk acceptance |
| Model | Retrieve, summarize, analyse, propose, and draft within an authorized route | Approval, undisclosed remote transmission, or semantic certainty |
| Executor | Implement bounded work and report discoveries or scope pressure | Silent scope expansion or self-acceptance |
| Assurer | Compare approved scope with independent evidence | Product decisions outside the assurance role |

The point is not to keep a human in every loop. It is to place human judgment at the
boundaries where meaning or risk changes, and to let deterministic systems carry exact
authority between those boundaries without inventing anything new.

## Evidence matters more than confidence

AI systems can produce polished and persuasive accounts of their own work. Those
accounts are useful as claims. They help a reviewer understand what the executor
believes it changed, why, and where it encountered difficulty. They should not be
mistaken for independent evidence.

A governed verification process asks the repository and the approved plan directly:

- What exact baseline was approved?
- Which files actually changed from that baseline?
- Which planned targets changed?
- Which planned targets are missing?
- Which paths changed unexpectedly?
- Which exact validation commands or artifacts supplied evidence?
- Which checks failed, were skipped, or could not run?
- Did the task or any canonical source become stale during execution?
- Which risks, exceptions, and deferrals remain?

These questions do not assume that an unexpected file is wrong or that a missing target
automatically proves failure. They make the discrepancy visible so that it can be
reviewed. An unexpected change may be necessary newly discovered work. A planned target
may legitimately remain unchanged. Control comes from recording and disposing the
difference, not from forcing reality to resemble an obsolete plan.

Evidence is also cumulative rather than magical. Git evidence, tests, contract checks,
manual review, security assessment, and operational validation each answer different
questions. Confidence should grow from the combination and its stated limits, not from
the fluency of a completion summary.

## Governance should accelerate the right work

The most common objection to a control chain is that it will slow delivery. It can, if
implemented as a sequence of universal forms and approvals. That is not the intended
model.

Good governance moves expensive discovery and disagreement earlier, makes routine
derivations deterministic, and applies assurance in proportion to risk. A low-risk
documentation correction should not require the same evidence as a public contract,
security boundary, data migration, or release change. A current approval should not be
repeated merely because a tool rendered it into another exact form.

The comparison is not between governance and no governance. Every team already pays for
misunderstood intent, hidden dependencies, review churn, escaped defects, emergency
fixes, and knowledge that lives only in conversations. The useful question is whether
the control system exposes these costs before implementation becomes expensive to undo.

When it works, governance improves flow by giving an executor a smaller and clearer
problem:

- authoritative sources are identified;
- known uncertainties are explicit;
- material decisions are already resolved;
- scope and non-goals are bounded;
- validation obligations are known before coding; and
- escalation has a defined path.

That is valuable to a human developer and to a coding agent for the same reason: neither
has to rediscover the change's authority while trying to implement it.

## Practical questions for engineering teams

A team does not need to adopt an entire platform before improving control. It can begin
by asking a small set of questions for every material change.

### Before implementation

- Where is the intended outcome recorded?
- Which source is allowed to resolve ambiguity?
- What baseline are we analysing?
- Which repositories, contracts, data, security, operational, and user surfaces may be
  affected?
- Which findings are evidence-backed facts, which are proposals, and which remain
  unknown?
- Which decisions must be made before work begins?

### Before assigning work

- Does each task have an objective, non-goals, targets, dependencies, acceptance
  criteria, and validation?
- Can another executor understand the task without private conversation history?
- What should happen if the executor discovers new scope?
- Is remote model or external service use explicitly governed?

### Before accepting completion

- Is the actual Git diff compared with the approved baseline and targets?
- Are missing and unexpected changes visible?
- Are exact validation results recorded with their limits?
- Has the appropriate independent or specialist review occurred?
- Who accepts the residual risk, and why?
- Which canonical documentation or standards must be reconciled before closure?

These questions turn “the agent says it is done” into a reviewable engineering claim.
They also expose where the organization still depends on memory, chat history, or a
single person's undocumented knowledge.

## What changes for engineering leadership

As code production accelerates, the valuable engineering investment shifts toward the
system around implementation. Teams need to strengthen:

- explicit product and technical intent;
- repository-owned contracts, standards, and decisions;
- context retrieval with stable identity and provenance;
- impact review before detailed planning;
- bounded work with clear escalation rules;
- deterministic validation where facts can be computed;
- independent and proportionate assurance;
- acceptance records that survive the conversation; and
- reviewed learning that improves future context without silently rewriting authority.

Useful measures change as well. Lines generated, prompts issued, or tasks completed say
little about whether control improved. Better signals include unexpected impact found
before acceptance, stale intent detected before implementation, reduction in review
rework, coverage of approved impact by evidence, and the absence of silent scope or
authority expansion.

This is not a retreat from automation. It is the foundation that allows automation to
become more capable without becoming less accountable.

## Worked contrast: one request, two control systems

The following Friends Todo example is hypothetical. It illustrates the control problem;
it is not captured CIS output.

A product owner asks for invitation links so that a customer can share a todo list with
a friend.

### Without governance

The request reaches a coding agent as one instruction:

> Add invitation links. Choose a sensible design, implement it, run the tests, and open
> a pull request when it is complete.

The agent finds the API and customer web repositories. It adds an invitation table, an
endpoint, a form, and focused tests. While implementing expiry, it also changes a
deployment workflow to supply a new setting. The tests it selected pass, and its final
message reports that invitations are complete.

The code may be sound, but the team cannot answer several control questions from that
result:

- Who decided whether a link is single-use, reusable, or account-bound?
- Were revocation, forwarding, existing access, abuse controls, and audit events in
  scope?
- Was the workflow change expected, or did implementation discover missing impact?
- Were all three product repositories considered against the same baseline?
- Which evidence supports acceptance beyond the executor's own tests and summary?

The agent authored the change, selected much of its scope, made design choices, chose
its evidence, and declared success. Those roles have collapsed into one execution.

### With governance

The same request first becomes a reviewed outcome. Product and technical authorities
record the actors, constraints, non-goals, open decisions, and acceptance boundary. An
exact workspace baseline anchors impact analysis. A reviewer dispositions proposed
impact across the web, API, and infrastructure repositories. Material link-lifecycle
choices are resolved before they become code.

The approved plan then separates contract, backend, customer experience, operational,
and verification work. Each executor receives only its bounded task and must report
new scope rather than absorb it. After implementation, Git supplies the actual changed
paths, required checks supply behavior evidence, and a reviewer disposes missing or
unexpected work before accepting residual risk.

The governed path can still use a coding agent for substantial implementation. The
difference is that authorship no longer has to stand in for intent, authority, scope,
evidence, or acceptance.

## Takeaway

The central mistake in AI-assisted development is treating code authorship as the main
control surface. Authorship identifies who or what produced an implementation. It does
not establish that the intended outcome was correct, the scope was complete, the
decisions were authorized, the evidence was independent, or the result should be
accepted.

Control comes from the system that keeps intent, context, impact, decisions, bounded
work, execution, evidence, and acceptance connected without confusing one role for
another.

Faster implementation makes that system more important, not less.

## Canonical CIS sources

- [Product intent](../specs/product-intent-spec.md)
- [System context](../specs/system-context-spec.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
