---
title: "Change Impact Studio Technical Intent"
type: specification
status: Draft
scope: Repository
owner: "Andrew Spiteri"
last_reviewed: "2026-08-25"
review_cadence: on architecture change
cis:
  stable_id: change-impact-studio:spec:technical-intent
---

# Change Impact Studio technical intent

## Design goals and principles

CIS should remain a small, inspectable control layer whose durable state can be
reviewed with ordinary repository tools. Its implementation is constrained by these
principles:

- Keep canonical authority in repository-owned Markdown and explicit structured files.
- Keep derived databases, indexes, caches, runs, envelopes, and analysis disposable.
- Prefer deterministic discovery, transformation, and validation before model-assisted work.
- Separate proposal, execution, evidence, approval, and acceptance as distinct states.
- Preserve exact identity, baseline, digest, provenance, and reviewer rationale across the lifecycle.
- Stop on collision, stale authority, ambiguous evidence, scope expansion, or conflicting providers.
- Keep integration providers subordinate to stable domain contracts and replaceable at the boundary.
- Make operations safe for agents through bounded inputs, parseable outputs, stable exit codes, and no silent remote action.
- Keep the editor client thin and free of product-domain logic.

## Runtime and delivery surface

| Concern | Technical direction |
|---|---|
| Runtime | .NET 10 command-line tool targeting `net10.0` and the `10.0.400` SDK feature band |
| Composition | Explicit dependency injection and built-in assembly registration in `Cis.Host` |
| Module contract | Public `ICisModule` and related contracts in `Cis.Abstractions`; one top-level command per module |
| Canonical documents | Markdown plus catalogued YAML metadata within the configured documentation root |
| Repository state | `.cis/repository.yml`, optional `.cis/workspace.yml`, starter manifests, and other explicit structured configuration |
| Derived storage | Bounded rebuildable state below `.cis/local/`, including SQLite graph data, indexes, caches, runs, envelopes, feedback, and diagnostics |
| Editor | Dependency-light Visual Studio Code extension over CLI JSON and canonical Markdown |
| Packaging | `AndrewSpiteri.ChangeImpactStudio` .NET tool plus a version-aligned VS Code package |
| Verification | Focused module tests, cross-module delivery tests, full solution build/test, editor checks, package smoke tests, and checksums |

The current component inventory and ownership are maintained in the
[module catalogue](module-catalog-spec.md) and
[module ownership map](../references/module-ownership-map.md), rather than duplicated here.

## Architecture and boundaries

- `Cis.Host` is the only composition root. It owns top-level command wiring, output
  conventions, dependency composition, and host-wide sanitized usage capture.
- `Cis.Abstractions` contains stable public contracts only; product behavior belongs in modules.
- Each built-in module owns a coherent capability and exactly one top-level command.
- Tracker and similar transports implement provider-neutral contracts in separately
  loaded provider assemblies; load order never resolves a semantic conflict.
- Repository initialization selects documentation, standards, skills, instructions,
  and reference families from deterministic classification evidence.
- The graph, index, context, and API modules derive engineering knowledge but do not
  replace its canonical source.
- The change, impact, decision, design, and plan modules own governed change state.
- Generation, workflow, AI, agent, and tracker modules prepare or coordinate work but
  cannot grant completion.
- Verification independently observes repository and validation evidence; learning may
  propose improvements but cannot self-modify source or policy.
- The Visual Studio Code extension renders and invokes stable CLI capabilities. Domain
  rules remain in the CLI so alternative clients receive identical behavior.

## Data, lifecycle, and consistency

- Canonical Markdown and structured configuration are the systems of record.
- Stable IDs are immutable; retired IDs are not reused.
- Applied starter hashes distinguish unchanged generated content from human edits.
- Writes that change governed state are atomic where practical and validate paths before mutation.
- Append-only event or usage ledgers preserve evidence without becoming the only source
  of canonical state.
- Graph builds and indexes are content-hash based, freshness checked, and safe to rebuild.
- Change dossiers bind to an exact Git commit or graph build. Rebaseline is explicit,
  actor-and-rationale audited, and blocked after reviewed evidence exists.
- External tracker synchronization uses durable links and three-way reconciliation;
  conflicts persist until explicitly resolved.
- Generated task and design artifacts retain their source digest so stale results cannot
  be accepted against changed authority.

## Integration and compatibility

- Human, JSON, and agent-oriented output are public CLI interaction contracts.
- Standard output is parseable; diagnostics are emitted on standard error; exit codes are stable.
- Git is invoked for read-only baselines, history, and diff evidence unless an explicit
  repository delivery policy authorizes a separate remote workflow.
- GitHub Issues and Jira Cloud are external projections, not canonical planning stores.
- Model providers are selected through named capabilities and governed routing profiles.
  Automatic selection remains local-only; remote use requires explicit authorization.
- API governance compares implemented and documented contracts against all configured
  supported baselines where compatibility requires it.
- Backward compatibility for catalog, repository, workspace, task, and provider schemas
  must be explicit, versioned, and validated rather than inferred from tolerant parsing.

## Security and privacy

- Resolve every repository and output path to an allowed root before reading or writing.
- Never execute assemblies discovered in a target repository.
- Never send likely secrets, credentials, tokens, private keys, certificates, or marked
  sensitive sources to a model.
- Remote model use requires both an approved route and explicit authorization for the
  content-bearing operation.
- Tracker and provider credentials come from environment configuration and are never
  written to canonical documents, logs, task envelopes, or usage ledgers.
- Feedback and diagnostics store bounded, sanitized metadata and normalized analysis,
  not prompts, responses, command option values, source content, or secrets.
- Public application endpoint plans must apply the governed cache and direct-persistence
  isolation policy; identity-protocol routes remain separately classified and `no-store`.

## Operations and observability

- Commands expose bounded diagnostics and actionable exit codes without changing the
  underlying result when feedback persistence fails.
- Workflow definitions store executable and argument lists rather than shell strings,
  checkpoint after every step, and resume only against the same definition digest.
- `cis repo doctor` remains read-only and is the first diagnostic route after repeated
  repository-aware failures or initialization collisions.
- Release construction runs restore, build, tests, editor checks, tool packaging,
  packaged-tool smoke tests, source archive creation, and checksum generation.
- Version declarations for the .NET tool and VS Code extension must remain aligned.

## Quality attributes

| Attribute | Requirement | Verification |
|---|---|---|
| Safety | Canonical or remote mutation is explicit, bounded, previewable where applicable, and collision-aware. | Initialization, path, provider, and delivery-policy tests; dry-run review |
| Reliability | Repeated identical operations preserve identity and yield equivalent state. | Idempotency, re-analysis, reconciliation, and workflow-resume tests |
| Traceability | Material output retains source, baseline, digest, evidence, and authority. | Catalog, graph, dossier, task-envelope, and verification validation |
| Maintainability | Modules have coherent ownership and depend on public contracts rather than host internals. | Composition, duplicate-registration, architecture, and focused module tests |
| Performance | Large repositories use incremental hashing, bounded traversal, compact output, and routing before broad loading. | Graph/index reuse tests, bounds, feedback metrics, and representative repositories |
| Privacy | Repository content remains local by default and persisted telemetry is sanitized. | Remote-authorization, sensitive-source, redaction, and usage-record tests |
| Portability | Core workflows work from the CLI and repository files independently of a specific editor, tracker, agent, or model. | Provider-neutral contracts, packaged-tool smoke tests, and alternate output formats |

## Decisions and delivery constraints

- Durable architectural choices are recorded as ADRs under `docs/architecture/decisions/`.
- Product behavior changes must update the owning specification, manual, tests, and
  navigation or catalog entries together.
- High-risk or cross-module work requires proportionate focused and wider validation.
- A passing build, agent result, workflow, external tracker status, or model assessment
  is evidence only; it never implies plan, design, verification, release, or risk acceptance.

## Related documents

- [Product intent](product-intent-spec.md)
- [System context](system-context-spec.md)
- [Module catalogue](module-catalog-spec.md)
- [CLI and repository initialization](cli-and-repository-initialisation-spec.md)
- [Context model and local graph](context-model-and-graph-spec.md)
- [Change impact and bounded planning](change-impact-and-planning-spec.md)
- [Execution, assurance, diagnostics, and learning](execution-assurance-and-learning-spec.md)
- [Repository delivery policy](repository-delivery-policy-spec.md)
