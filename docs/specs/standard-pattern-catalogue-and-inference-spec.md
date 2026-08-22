---
title: "Standard Pattern Catalogue and Inference Specification"
type: technical-specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-16"
review_cadence: "on compiler graph or inference contract change"
cis:
  stable_id: change-impact-studio:spec:standard-pattern-catalogue-inference
---

# Standard Pattern Catalogue and Inference Specification

## Intent

CIS uses compiler-backed graph evidence to recognize recurring implementation shapes and propose possible repository standards. Code establishes what exists; it does not establish what ought to be normative. Every inferred candidate remains non-canonical until explicit human review creates or changes a standard.

## Pattern catalogue

A pattern has a stable lowercase dot-separated ID, positive immutable version, lifecycle, compiler languages, repository roles and capabilities, required graph capabilities, a constrained subject selector, required and forbidden adjacent relationships, bounded occurrence and consistency thresholds, and proposed standard wording with verification guidance.

Providers register patterns through `ICisStandardPatternProvider`. Repository extensions are canonical Markdown below `references/standard-patterns/`, declare `type: standard-inference-pattern`, and require a matching `catalog.yml` entry with the same stable ID and path, `type: standard-inference-pattern`, and `authority: canonical`.

Duplicate IDs are blocking conflicts across built-in, extension, and repository providers. Load order, assembly order, or a higher version cannot silently replace a pattern. A later replacement policy must require an explicit durable selection and migration decision.

Selectors support exact node kind and subtype, required facets, exact and substring property constraints, included path prefixes, and excluded path prefixes. Relationships have `incoming` or `outgoing` direction, an exact edge type, and a constrained adjacent-node selector. The matcher does not execute repository code, regular expressions, scripts, or provider callbacks.

## Known C# patterns

| Pattern | Intent | Current graph readiness |
|---|---|---|
| `csharp.testing.calls-production-symbol` | Tests call compiler-bound production symbols | Ready |
| `csharp.modularity.command-registration` | Modules register commands through the host registry | Ready |
| `csharp.api.thin-endpoint` | HTTP endpoints delegate without direct persistence | Ready; version 2 uses endpoint attributes and semantic call edges |
| `csharp.security.authorization-boundary` | Protected operations enforce authorization at a trusted boundary | Ready; version 2 uses effective attributes and authorization calls |
| `csharp.application.handler-boundary` | Explicit application handlers own orchestration | Ready; version 2 uses compiler-resolved interfaces |
| `csharp.persistence.repository-transaction` | Writes use an explicit transaction boundary | Ready; version 2 uses command-handler and bounded-flow facts |
| `csharp.events.transactional-outbox` | State and outbox writes commit atomically | Ready; version 2 uses ordered semantic milestones |
| `csharp.events.idempotent-consumer` | Consumers expose retry-safe idempotency | Ready; version 2 uses consumer interfaces and ordered milestones |
| `csharp.configuration.options-binding` | Configuration uses typed options | Ready; version 2 uses external calls and generic arguments |
| `csharp.observability.structured-logging` | Logging uses stable structured events | Ready; version 2 uses argument shapes without retaining values |
| `csharp.generated-code.exclusion` | Generated declarations do not distort prevalence | Ready; version 2 uses attributes, file names, and generated headers |

Patterns with missing graph capabilities are reported as `requires-graph-capability` and are never approximated through lexical matching.

## Matching and evidence

For every applicable ready pattern, the matcher selects eligible subject nodes and evaluates all required and forbidden relationships. Consistency is `matching occurrences / eligible occurrences`. A candidate is emitted only when both the minimum occurrence and minimum consistency thresholds pass.

Evidence records the exact graph build, pattern ID and version, eligible and matching counts, consistency, bounded matching nodes, bounded counterexamples, related graph nodes, and repository-safe source locations. Component and language filters may reduce scope but never change the pattern definition.

Inference requires a fresh graph. Missing, incompatible, partial, or stale graph evidence is surfaced; stale evidence blocks inference. Compiler-marked generated nodes are excluded from every pattern population unless the pattern explicitly selects generated evidence.

## Authority and storage

`cis standards infer` writes derived JSON and Markdown below `.cis/local/standards/inference/`. These files are disposable routing and review evidence. They are not cataloged standards, cannot satisfy conformance, and cannot authorize implementation behavior.

Candidates begin `Unreviewed`. Candidate acceptance and rejection commands are a later slice. Acceptance must require reviewer identity and rationale and may create only a Draft canonical standard with conservative conformance mappings. Rejection must be durable enough to prevent the same unchanged evidence from being repeatedly proposed.

## Compiler roadmap

The first implementation is C# and Roslyn. Graph schema version 2 includes effective attributes, direct interface and base-type relationships, repository and external calls, generic arguments, privacy-safe invocation argument shapes, generated-source markers, per-call ordering, and bounded intraprocedural semantic flow summaries. The dataflow capability proves only recognized source-order milestones within one callable symbol; it is not branch-sensitive, interprocedural, or runtime evidence. TypeScript compiler API, Swift compiler/SourceKit, and Kotlin compiler analysis follow the same normalized graph capability contract; lexical adapters cannot satisfy compiler-only patterns.

## Safety

- Frequent code may represent legacy debt or an anti-pattern.
- Consistency never implies correctness or human approval.
- Counterexamples are preserved, not discarded to improve confidence.
- Model output may rank or explain candidates later but cannot manufacture compiler evidence.
- Inference never edits canonical standards, conformance mappings, source code, or tests.
