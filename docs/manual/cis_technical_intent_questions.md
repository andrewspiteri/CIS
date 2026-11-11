---
title: "cis technical-intent questions"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-11-11"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-technical-intent-questions
---

# `cis technical-intent questions`

Captures the governed high-level technology and architecture choices that precede the first
technical intent.

```text
cis technical-intent questions init [--workspace <path>] [--format <human|json|agent>]
cis technical-intent questions status [--summary] [--workspace <path>] [--format <human|json|agent>]
cis technical-intent questions answer <TI-Q-id> --answer <text> --actor <human> [--workspace <path>] [--format <human|json|agent>]
```

`init` requires an Active/current BRD and creates the catalogued canonical
`specs/technical-intent-questionnaire.md`. It is idempotent and preserves human-recorded answers.
The 16 stable questions cover:

- product surfaces, including public/customer/backoffice web, native mobile, API, workers,
  desktop, and integrations;
- frontend and backend technologies;
- architecture style, repository/component ownership, and deployable topology;
- primary and supporting data stores;
- contracts, integration, identity, authorization, tenancy, and asynchronous work;
- hosting, deployment, observability, operations, security, privacy, and compliance;
- quality and assurance, AI/model lifecycle, constraints, exclusions, and future options.

Every question contains context, common options, and an advisory starting direction. CIS first
classifies every registered repository and inspects bounded package, build, configuration, Compose,
and Terraform evidence. For an existing implementation, facts supported by that evidence are
recorded as `Derived`, with confidence and exact repository provenance. This includes detected
surfaces, frontend/backend stacks, current repository and architecture topology, a uniquely
identified transactional database, contracts, identity, deployment, asynchronous processing,
operations, testing, supporting stores, and AI boundaries where evidence exists. A human may
override any derived direction with `answer`.

Source interpretation can reuse explicit accepted facts before a questionnaire is
saved. This includes a statement that AI/model execution is outside the selected
architecture. Derived answers retain source references, digests and acceptance
basis; agent transcription supplies no new human actor or answer time. A current
AI requirement conflicting with that exclusion remains blocked for reconciliation.
Genuinely missing necessary choices remain unanswered.

For a greenfield project, and for any existing-project choice that cannot be established safely,
the question remains `Unanswered`. Advisory text is not an answer. The questionnaire becomes
Complete when every question is either evidence-derived or explicitly answered by a human and its
BRD semantic baseline remains current. CIS does not infer a primary database when multiple stores
are detected, nor does it invent security, compliance, or product constraints from absence of code.

`cis technical-intent init` then consumes the exact answer set into a schema-4 technical intent,
including the high-level choice table with human or repository provenance, logical components, primary interactions, architecture
guidelines, and accepted `TI-DEC-*` records. A changed questionnaire digest makes an approved
aggregate technical intent stale and requires reconciliation. Individual answers and document
decisions use their supporting basis: an unrelated answer does not reopen a current decision.
`assessmentStatus` and `assessmentReason` identify affected or unknown scope. Retained answers
keep their actor/time and count as recorded; `reassessmentCount` is separate from unanswered
questions. Renew only the named affected answers after reviewing the changed inputs. Identical
same-actor saves return `unchanged` without moving their timestamps.

`status --summary` omits individual question text and answers while retaining lifecycle,
currency, completion counts, paths, versions, warnings, and errors. Workspace projections
use this bounded form; the questionnaire page uses the complete result.

Exit `0` means the command completed and the questionnaire is current. Missing canonical state
exits `4`; an unmet Active-BRD gate exits `5`; structural or input errors exit `2`.
