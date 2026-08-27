---
title: Standards Conformance Matrix
type: standards-conformance-reference
status: Active
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:reference:standards-conformance-matrix
---

# Standards Conformance Matrix

This canonical matrix maps every active standard rule to an enforcement route. Advisory model output identifies candidates only; it does not prove a breach or compliance.

| Standard ID | Rule ID | Governed surface | Enforcement | Evidence | Status | Notes |
|---|---|---|---|---|---|---|
| change-impact-studio:standard:documentation-governance | CIS-STD-DOC-001 | `change-impact-studio documentation root` | deterministic | `cis docs validate --strict`; `cis standards validate --strict` | Active | Catalog and location checks. |
| change-impact-studio:standard:documentation-governance | CIS-STD-DOC-002 | `standards/*.md` | deterministic | `cis standards validate --strict` | Active | Required metadata and stable-ID alignment. |
| change-impact-studio:standard:documentation-governance | CIS-STD-DOC-003 | `standards/*.md` | deterministic | `cis standards validate --strict` | Active | Rule presence and repository-wide uniqueness. |
| change-impact-studio:standard:documentation-governance | CIS-STD-DOC-004 | `standards/*.md` | manual-review | maintainer standard review | Active | Normative clarity requires semantic review. |
| change-impact-studio:standard:documentation-governance | CIS-STD-DOC-005 | `references/standards-conformance-matrix.md` | deterministic | `cis standards validate --strict` | Active | Every active rule requires a row. |
| change-impact-studio:standard:documentation-governance | CIS-STD-DOC-006 | model and heuristic findings | manual-review | finding disposition record | Active | Human or deterministic evidence confirms findings. |
| change-impact-studio:standard:documentation-governance | CIS-STD-DOC-007 | change dossiers and ADRs | manual-review | recorded exception approval | Active | Agents cannot approve exceptions. |
| change-impact-studio:standard:documentation-governance | CIS-STD-DOC-008 | governed implementation | manual-review | change plan and completion evidence | Active | Applicable standards are routed before work. |
| change-impact-studio:standard:documentation-governance | CIS-STD-DOC-009 | standard imports | deterministic | `cis standards import --dry-run`; catalog and matrix validation | Active | Canonical writes require confirmation and reject divergent destinations. |
| change-impact-studio:standard:documentation-governance | CIS-STD-DOC-010 | standards collection | deterministic | `cis standards audit`; quarantine history | Active | Model classifications remain advisory candidates. |
| change-impact-studio:standard:agent-documentation-compliance | AGENT-DOC-001 | documentation, repository-governance | manual-review | Review the recorded context package or tool evidence. | Active | PARR-derived starter; strengthen with deterministic enforcement where practical. |
| change-impact-studio:standard:agent-documentation-compliance | AGENT-DOC-002 | documentation, repository-governance | manual-review | Review conflict handling and canonical citations. | Active | PARR-derived starter; strengthen with deterministic enforcement where practical. |
| change-impact-studio:standard:agent-documentation-compliance | AGENT-DOC-003 | documentation, repository-governance | manual-review | Review change evidence and decision records. | Active | PARR-derived starter; strengthen with deterministic enforcement where practical. |
| change-impact-studio:standard:agent-documentation-compliance | AGENT-DOC-004 | documentation, repository-governance | manual-review | Review final delivery evidence and documentation traceability. | Active | PARR-derived starter; strengthen with deterministic enforcement where practical. |
| change-impact-studio:standard:testing | TEST-001 | testing, verification | manual-review | Inspect the feature or task verification matrix and trace each selected layer to acceptance or risk. | Active | PARR-derived starter; strengthen with deterministic enforcement where practical. |
| change-impact-studio:standard:testing | TEST-002 | testing, verification | manual-review | Execute the narrow unit suite and review assertions for observable behavior rather than implementation mirroring. | Active | PARR-derived starter; strengthen with deterministic enforcement where practical. |
| change-impact-studio:standard:testing | TEST-003 | testing, verification | manual-review | Inspect unit fixtures and execute the suite without external service or container prerequisites. | Active | PARR-derived starter; strengthen with deterministic enforcement where practical. |
| change-impact-studio:standard:testing | TEST-004 | testing, verification | manual-review | Execute the targeted component or integration suite and review its real-boundary evidence. | Active | PARR-derived starter; strengthen with deterministic enforcement where practical. |
| change-impact-studio:standard:testing | TEST-005 | testing, verification | manual-review | Review pinned dependency versions, readiness, isolation, cleanup, runtime prerequisites, and actual container-backed execution evidence. | Active | PARR-derived starter; strengthen with deterministic enforcement where practical. |
| change-impact-studio:standard:testing | TEST-006 | testing, verification | manual-review | Execute the focused business or acceptance scenarios and confirm they use domain language and thin fixtures. | Active | PARR-derived starter; strengthen with deterministic enforcement where practical. |
| change-impact-studio:standard:testing | TEST-007 | testing, verification | manual-review | Run structural checks or inspect the conformance matrix and recorded manual evidence. | Active | PARR-derived starter; strengthen with deterministic enforcement where practical. |
| change-impact-studio:standard:testing | TEST-008 | testing, verification | manual-review | Execute the applicable component and journey suites and inspect accessible selectors, state coverage, and failure artifacts. | Active | PARR-derived starter; strengthen with deterministic enforcement where practical. |
| change-impact-studio:standard:testing | TEST-009 | testing, verification | manual-review | Review the preserved reproduction and confirm the new or changed test distinguishes broken from corrected behavior. | Active | PARR-derived starter; strengthen with deterministic enforcement where practical. |
| change-impact-studio:standard:testing | TEST-010 | testing, verification | manual-review | Review bounded mutation, security, architecture, property, or independent-review evidence and disposition surviving risks. | Active | PARR-derived starter; strengthen with deterministic enforcement where practical. |
| change-impact-studio:standard:testing | TEST-011 | testing, verification | manual-review | Review changed-scope coverage, exclusions, meaningful assertions, and any approved exception. | Active | PARR-derived starter; strengthen with deterministic enforcement where practical. |
| change-impact-studio:standard:testing | TEST-012 | testing, verification | manual-review | Inspect exact commands, results, artifacts, unrun checks, environmental limits, and residual risks. | Active | PARR-derived starter; strengthen with deterministic enforcement where practical. |
| change-impact-studio:standard:testing | TEST-013 | testing, verification | deterministic | CIS workflow and testing module regression tests; `cis test reconcile` artifact manifest | Active | Enforces bounded live attempt logs, stale-evidence exclusion, and correlated diagnostic hashes. |
| change-impact-studio:standard:versioning-release | REL-001 | product version declarations | deterministic | `tools/build-release.ps1`; compare `Version.props` and `vscode-extension/package.json` | Active | Release construction rejects version divergence. |
| change-impact-studio:standard:versioning-release | REL-002 | public release compatibility | manual-review | release version review against Semantic Versioning rules | Active | Compatibility impact determines patch, minor, or major. |
| change-impact-studio:standard:versioning-release | REL-003 | SDK and package dependencies | deterministic | `global.json`; `Directory.Packages.props`; CI restore/build | Active | Toolchain and dependencies remain explicitly pinned. |
| change-impact-studio:standard:versioning-release | REL-004 | release tag and platform verification | deterministic | `.github/workflows/ci.yml`; `.github/workflows/release.yml` | Active | The tag must match the version and both supported operating systems must pass. |
| change-impact-studio:standard:versioning-release | REL-005 | release artifacts and packaged CLI | deterministic | `tools/build-release.ps1`; `SHA256SUMS`; packaged `cis host modules` smoke test | Active | Publication requires complete artifacts and module registration evidence. |
