---
title: "Change Impact Studio Delivery and Assurance"
type: specification
status: Draft
scope: Repository
owner: "Andrew Spiteri"
last_reviewed: "2026-08-25"
review_cadence: on delivery-policy change
cis:
  stable_id: change-impact-studio:spec:delivery-and-assurance
---

# Change Impact Studio delivery and assurance

## Purpose

This specification defines the minimum control and evidence expected when changing
CIS itself. Detailed task obligations belong in an approved change dossier; the
[repository delivery policy](repository-delivery-policy-spec.md) governs branch,
commit, push, pull-request, merge, release, and other remote actions.

## Human control

- A human owns product scope, canonical intent, material decisions, exceptions, risk
  acceptance, plan or design approval, verification acceptance, and release approval.
- Agents and deterministic tools may discover, propose, implement, validate, and
  preserve evidence, but must surface ambiguity, collisions, changed scope, and
  unverified assumptions.
- Exact current feature approval may be reused only through deterministic authority
  carry-forward that preserves reviewer, rationale, source path, digest, and scope.
  Stale, ambiguous, expanded, low-confidence, or exceptional work returns to review.
- The executor's completion claim, changed-file list, test report, or external tracker
  status is never authoritative by itself.

## Change obligations

Every governed change should:

1. identify the intended outcome and exact repository or graph baseline;
2. update affected product intent, specifications, manuals, standards, references,
   examples, and agent guidance in the same change as behavior;
3. preserve stable IDs, lifecycle history, catalog entries, source digests, and human rationale;
4. cover every accepted impact with bounded work, acceptance criteria, and validation;
5. record decisions before the gate they block;
6. compare actual Git changes with planned targets and retain unexpected or missing work as findings;
7. record exact commands, artifacts, results, skipped checks, residual risks, and deferrals; and
8. leave release, merge, and remote mutation to explicit policy and human authorization.

## Validation strategy

Run the smallest deterministic check that can fail for the relevant reason, then
expand according to impact and risk.

| Change surface | Minimum focused evidence | Wider evidence when affected |
|---|---|---|
| Documentation, catalog, standards, or skills | `cis docs validate --strict`; relevant standards/skills validation | Documentation, standards, graph, and repository test projects |
| One C# module | Owning module test project | Referencing modules and `Cis.Modules.Delivery.Tests` |
| Host composition or public contracts | `Cis.Host.Tests` and affected module tests | Full solution build and test |
| Graph, context, index, or API evidence | Owning focused tests plus strict graph/API validation on a representative repository | Delivery tests and golden-path repositories |
| Change, impact, decision, design, plan, agent, or verification lifecycle | Focused owning tests | `Cis.Modules.Delivery.Tests` and a complete governed change replay |
| Tracker or provider transport | Provider/contract focused tests with no live credential requirement | Explicitly authorized integration checks |
| Visual Studio Code client | `node --check vscode-extension/extension.js` and `node --test vscode-extension/test/*.test.js` | VSIX packaging and manual client smoke test |
| Packaging, versioning, or release | `tools/build-release.ps1` | Install packaged tool in an isolated path and verify required modules and checksums |
| Cross-cutting or high-risk change | Affected focused checks | `dotnet build ChangeImpactStudio.slnx` and `dotnet test ChangeImpactStudio.slnx` |

Ordinary full-repository verification commands are:

```powershell
dotnet restore ChangeImpactStudio.slnx
dotnet build ChangeImpactStudio.slnx --no-restore
dotnet test ChangeImpactStudio.slnx --no-build --no-restore
node --check vscode-extension/extension.js
node --test vscode-extension/test/*.test.js
```

## Independent assurance

Independent assurance should be proportionate to the consequence of failure:

| Risk class | Required assurance |
|---|---|
| Low | Focused deterministic validation with recorded output |
| Medium | Focused validation plus a reviewer who did not rely solely on executor claims |
| High | Full affected-system validation, explicit independent assurance task, human review of scope and evidence, and recorded residual risk |
| Security, privacy, data migration, public contract, authority, or release boundary | Treat as at least Medium; require the relevant specialist or named authority when impact is material |

Independent assurance uses canonical task scope and independently gathered repository,
contract, test, and Git evidence. A model may identify candidates but cannot prove
compliance, dismiss a failure, accept an exception, or grant completion.

## Completion evidence

Completion evidence must identify:

- the task, baseline, and approved scope being verified;
- the exact command or artifact and its result;
- expected changes that occurred and expected changes that are missing;
- unexpected changes and their reviewed disposition;
- validation failures, unavailable checks, deferrals, and follow-up owners;
- documentation, catalog, graph, and generated-artifact freshness;
- independent assurance performed for the risk class; and
- reviewer identity, rationale, timestamp, and residual risk for final acceptance.

Evidence is recorded in the change dossier and `verification.md`. Local logs, caches,
workflow state, and tool-usage ledgers may support that record but remain disposable.

## Release boundary

A verified CIS change is a release candidate, not a release. Packaging, checksums,
publishing, push, pull-request, merge, tag, and deployment remain separate actions
governed by the repository delivery policy and explicit authorization.

## Related documents

- [Product intent](product-intent-spec.md)
- [Technical intent](technical-intent-spec.md)
- [Change impact and bounded planning](change-impact-and-planning-spec.md)
- [Execution, assurance, diagnostics, and learning](execution-assurance-and-learning-spec.md)
- [Repository delivery policy](repository-delivery-policy-spec.md)
- [Standard delivery workflow](../workflows/standard-delivery.md)
