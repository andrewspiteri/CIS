---
title: "cis plan validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-05"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-plan-validate
---

# `cis plan validate`

Validates accepted-impact coverage, work-item identity, complexity, required
decomposition of high work, parent and dependency existence, dependency cycles,
acceptance criteria, validation, outcome criteria, and unresolved decisions.

Imported feature plans additionally require every catalogued task document and its
mandatory sections, the design dependency chain and `design.md` for frontend scope,
the feature requirement provenance set, `test-cases.md`, its synchronized
`test-cases.csv` projection, and `verification.md`. Manual-test validation checks the
source feature path/digest, one stable case per requirement, portable CSV header,
requirement coverage, and the CSV hash recorded by the Markdown catalogue. CIS resolves the
recorded feature-specification path inside the repository and compares its current
SHA-256 with the plan provenance. A missing, escaping, or changed source makes the plan
invalid even if its front matter still says `Approved`.

`Pending` automation is valid during planning. Automated-test completeness is a final
delivery concern enforced by `cis verify validate`, after implementation has had the
opportunity to create the mapped tests.

```text
cis plan validate <change-id> [--repo <path>] [--format <human|json|agent>]
```

Exit `0` means valid. Exit `5` means the plan is structurally present but cannot be
approved. Exit `2` means the change or request is invalid.

## Representative workload evidence

Plans mentioning datasets, throughput, capacity, bulk/streaming work, load tests or
memory limits prompt an early representative workload assessment. This is a planning
warning, not an inferred resource budget or permission to run an experiment.

Record `workload-evidence.json` beside the change dossier's `plan.md`. For schema version
1, set `schemaVersion` to `1` and declare `applicable` explicitly. If false, provide a concrete `rationale`. If true,
record positive `sourceBytes`, `recordCount`, `concurrency`, `wallSeconds`, `cpuSeconds`,
`memoryBytes` and `storageBytes`; describe `shape`, `softwareIdentity`, `fixtureIdentity`
and `fixtureDifferences`; and record separate `componentResult`, `installationResult`
and `endToEndResult`. Bind retained evidence using repository-relative `artifactPath`
and its hexadecimal `artifactSha256`.

When volume-sensitive work is detected or a profile is explicitly present, Final
Delivery Sweep completion requires a complete applicable record with
`endToEndResult` equal to `passed` and a matching artifact, or an explicit inapplicability
rationale. Component passes and installation success do not replace this result. The
check validates the declaration and artifact integrity; it does not independently
prove the artifact's conclusions or grant human acceptance, deployment authority,
changed budgets or permission to retry an installed workload.

Detection inspects work-item titles and acceptance criteria using keyword hints; it
is not a complete workload classifier. An explicit profile is checked even without a
keyword match. The profile is limited to 64 KiB, and the profile and artifact must
resolve within the repository without symbolic-link redirection. See
[`cis plan task transition`](cis_plan_task_transition.md) for the completion gate.
