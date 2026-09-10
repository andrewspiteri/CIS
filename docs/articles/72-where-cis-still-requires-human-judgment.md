---
title: "Where CIS Still Requires Human Judgment"
type: article
status: Active
series: "Product Evolution and Learning"
series_order: 2
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on authority-model change
summary: "The boundaries where evidence and proposals stop and product, architecture, exception, risk, and acceptance authority begins."
cis:
  stable_id: change-impact-studio:article:human-judgment-boundaries
---

# Where CIS still requires human judgment

CIS automates discovery, validation, derivation, routing, and evidence collection. It
deliberately stops at several boundaries.

## Stakeholder meaning

Source and documents can suggest outcomes. Humans decide current business intent,
actors, scope, priorities, and success measures.

They also decide ecosystem and product identity, whether a repository is product-owned
or a dependency, which sources may be used, and when the product-definition baseline is
ready to activate.

## Material technical choices

Models can propose options. Humans resolve architecture, compatibility, trust, data,
and operational decisions and record rationale.

## Impact disposition

Analysis proposes affected concerns. A reviewer accepts, rejects, or defers scope unless
exact existing authority can be carried forward deterministically.

## Standards exceptions

Only the named authority can accept a deviation, scope it, set review conditions, and
approve compensating controls.

## Design and usability

Renderers and validators can prove artifact structure. Human review decides whether the
behavior and visual result are acceptable for the intended users.

## Risk and completion

Verification can show evidence and gaps. Humans decide whether residual risk is acceptable
and whether completion can be accepted. Direct agent execution does not move this gate:
permission ceilings, isolated attempts, and structured results constrain work, while a
reviewer still decides whether a result may enter the governed repository.

## Learning and policy

Diagnostics can propose improvement. Humans decide whether it becomes canonical guidance,
standards, source, or process.

## Evidence can narrow a decision without making it

CIS can show source digests, graph paths, contract differences, test results, provider
capabilities, and historical rationale. That evidence may eliminate unsafe options. The
remaining choice can still depend on product value, organizational responsibility, legal
interpretation, customer expectation, or risk tolerance that the tool does not own.

Good automation brings the decision to the right person with a bounded question. It does
not hide the question inside implementation.

## Product-definition judgment is foundational

Humans name the product and ecosystem, classify repositories as owned or dependencies,
assess source documents, answer open business and technical questions, dispose review
findings, and activate the consolidated definition. Agents may draft from bounded evidence
and separate providers may review; neither can decide that inferred behavior is desired.

This gate prevents the rest of the lifecycle from becoming precise execution of the wrong
product interpretation.

## Impact and planning judgment remain distinct

An analyser proposes affected concerns. A reviewer decides whether each belongs to the
current change, is unsupported, or must be deferred. Material alternatives become
decisions with options and rationale. The plan then carries accepted scope into bounded
work.

Automatically accepting every graph path would create unnecessary work; automatically
discarding weak paths would hide risk. Human disposition determines consequence.

## Permission and exception decisions carry risk

A controller can enforce that a provider stays inside read-only or workspace-write
ceilings. A human authorizes the chosen run, any supported request approval, remote content
disclosure, and every exception to a standard. The agent cannot expand its own permissions
or approve the rule deviation that makes its result acceptable.

## Design and acceptance require situated review

Validators can prove that wireframes contain required states, renderers produced every
screen, and accessibility checks ran. Humans decide whether the result communicates the
right behavior to the intended public, customer, or backoffice user.

Similarly, verification can assemble a structurally complete evidence pack. Acceptance
decides whether unavailable checks, known defects, deferrals, and residual risk are
tolerable now.

## Learning needs editorial authority

Diagnostics and repeated failures can produce a persuasive improvement proposal. A human
decides whether the cause is understood, whether the recommendation conflicts with wider
policy, and whether it belongs in source, a standard, a graph declaration, documentation,
or nowhere.

## Human gates should be meaningful

Do not require manual approval for deterministic copies whose source, digest, scope, and
prior authority are unchanged. Preserve human attention for new meaning, changed risk,
exceptions, ambiguity, and acceptance. Governance works best when it automates exact
carry-forward and makes genuine decisions impossible to miss.

## Takeaway

CIS should automate the preservation and application of authority, not manufacture it.
The remaining human gates are where meaning, trade-offs, exceptions, and risk genuinely enter.

## Canonical CIS sources

- [Product intent](../specs/product-intent-spec.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Standards governance](../specs/standards-governance-spec.md)
- [Product and ecosystem boundary](../specs/product-and-ecosystem-boundary-spec.md)
- [High-level product-definition wizard](../specs/high-level-product-definition-wizard-spec.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
