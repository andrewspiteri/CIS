---
title: "Governing Exceptions and Compensating Controls"
type: article
status: Active
series: "Standards and Engineering Policy"
series_order: 6
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on exception-policy change
summary: "An exception needs exact rule identity, human authority, bounded scope, review conditions, and alternative risk controls."
cis:
  stable_id: change-impact-studio:article:exceptions-compensating-controls
---

# Governing exceptions and compensating controls

Real systems sometimes cannot satisfy a standard exactly. Hiding the deviation weakens
governance; banning every deviation pushes decisions outside the record.

## Name the exact rule

An exception cites a stable rule ID rather than suspending an entire standard.

## Record authority and rationale

A named human approver explains why the exception is acceptable. An agent cannot approve
an exception to the rules governing its own work.

## Bound the scope

The record identifies repositories, components, change, environments, and duration. An
exception for one legacy endpoint cannot silently become product-wide policy.

## Require review or expiry

Temporary constraints need an expiry date or review condition. Durable deviations may
indicate that the standard itself needs revision.

## Preserve compensating controls

Alternative monitoring, isolation, validation, or operational procedures reduce risk
while the primary rule is unmet. Their evidence belongs in the change and verification record.

## An exception has a complete contract

A useful record includes:

- stable exception and rule IDs;
- the exact affected repository, component, path, operation, or change;
- the authority allowed to approve the deviation;
- evidence showing why the rule cannot currently be met;
- rationale and consequence;
- compensating controls and their evidence;
- start, expiry, or explicit review condition;
- owner and remediation or follow-up; and
- current lifecycle.

Without those fields, an exception becomes an informal permission that expands through
memory.

## Work through a cache-policy exception

Suppose a legacy public endpoint cannot immediately move persistence access behind the
required query abstraction. The exception should name the exact cache-isolation rule and
operation, not “legacy APIs.” Evidence explains the coupling and why immediate correction
is unsafe.

Compensating controls might include response allow-listing, stricter rate limits,
short-lived caching, focused data-exposure tests, enhanced monitoring, and a blocked
expansion of the endpoint's fields. The record names who approved the residual risk and
when the architecture must be reviewed again.

The controls do not mean the implementation conforms. They explain why one bounded
deviation is temporarily tolerated.

## Validate compensating controls

Controls need owners and evidence just like the primary rule. “Monitor closely” is not a
control until the signal, threshold, response, and operational responsibility are defined.
A test or workflow that supplies the evidence must be current and valid.

If a compensating control fails, the exception may no longer be acceptable even before
its calendar expiry.

## Prevent silent inheritance

An exception for one change, environment, or endpoint does not apply to a new consumer or
similar component automatically. Applicability resolution should match the exact scope and
surface the deviation to every task it affects.

Copying an exception template must create a new review decision, not duplicate the old
approver and rationale.

## Expiry is a governance event

At expiry or review condition, the owner must remove the deviation, renew it with current
evidence and authority, or revise the underlying standard through normal governance. A
tool should not extend the date automatically because remediation is incomplete.

Repeated long-lived exceptions may show that the rule is unrealistic or that remediation
has been underfunded. Either conclusion requires a visible product or policy decision.

## Keep agents outside approval

An agent can identify the conflict, propose controls, implement remediation, and assemble
evidence. It cannot approve the exception, especially when the exception changes the rules
against which its own work is judged.

## Report exceptions beside conformance

An applicable-rule report should show active exceptions and their exact scope rather than
turning the evaluator green globally. Verification then confirms that compensating
controls ran and that the candidate change stayed within the exception boundary. Work
outside that boundary must satisfy the original rule or obtain its own authorization.

## Takeaway

Exceptions are governed decisions, not validator switches. Make them exact, human-owned,
bounded, reviewable, and supported by explicit compensating controls.

## Canonical CIS sources

- [Documentation governance standard](../standards/documentation-governance-standard.md)
- [Standards governance](../specs/standards-governance-spec.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
