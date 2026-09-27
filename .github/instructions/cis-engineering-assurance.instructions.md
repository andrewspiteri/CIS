---
applyTo: "**"
---

# CIS engineering assurance

Apply the relevant sections to the changed behavior. Preserve the repository's approved framework, concrete commands,
contract names, acceptance criteria, release ordering and stronger safeguards. Import does not approve a new technology
choice or prove that a test, scanner or CI gate ran. Read the applicable standards below `docs/standards/`.

## Secure feature implementation

- Before coding, identify actors, principal types, tenant/customer scope, assets, trust boundaries, entry points, abuse cases, affected contracts and minimum negative tests.
- Enforce authorization at the backend/application boundary. Hidden controls and frontend checks do not authorize access. Test unauthenticated, wrong-role, wrong-tenant and prohibited-principal cases where applicable.
- Check input validation, output filtering, sensitive-data disclosure, injection, CSRF/CORS, caching, rate limits, logging and secret handling according to the affected surface and repository policy.
- Keep test-only authentication and fixtures isolated from production. Production must not activate a test identity or authorization bypass through request headers, cookies, hostnames or ordinary configuration. Prove isolation with tests.
- Update relevant tests, specifications and contract references in the same change. Inventory configuration reads, environment variables and workflow environment keys consumed by running application code, including test deployments; document real inputs and distinguish pure fixtures without inventing dictionary entries.
- Preserve the repository's required SAST tool, including CodeQL where adopted, and its release gate. SAST does not replace authorization tests, browser security regression, dependency/secret scanning or DAST. Use the security-suite profile and `cis security validate`, `cis security reconcile` and exception validation for declared CIS evidence.
- Never expose real secrets in tests, logs, documents, snapshots or prompts. Use neutral fixtures; avoid credential-shaped names and values that trigger the repository's scanners. Do not weaken scanners, broad allowlists or ZAP suppressions to make a change pass.
- Accepted findings require exact evidence, owner, expiry, remediation tracking and explicit human approval under the repository policy. Never ignore failed governance checks or treat a model's triage as acceptance.
- For CI tooling tests, identify the owning workflow, include changed test files in trigger paths, declare their dependencies and run the owning check. An unrelated lint workflow is not evidence that they ran.

## Layered testing and browser regression

- Match the test layer to the behavior: unit, component, API/integration, business acceptance, architecture, browser, security and operational tests have distinct purposes. Keep exhaustive business rules and persistence edge cases in focused lower layers. Use mutation testing where required by risk or repository policy.
- Use the existing browser harness. Keep .NET Playwright in an established .NET test project with its adopted runner; keep Node Playwright where adopted. Do not switch runners without a reviewed technical decision. Register the real command, prerequisites and result format in `docs/references/test-suite-profile.md`.
- Keep browser regression separate from backend-only/unit jobs. Preserve release ordering, required lower-level gates, browser matrices and artifact-promotion checks. A solution-wide .NET test result must not be described as unit-only or browser-specific evidence.
- Run against deterministic application composition with required frontend, backend and production-shaped dependencies. Reuse Testcontainers or the approved harness, seed isolated identities and data, and dispose resources. Do not rely on previous runs, shared developer databases or manual setup.
- Use the repository's page/component objects, semantic roles, labels and stable test identifiers. Avoid brittle DOM structure, fixed sleeps and unconditional network-idle waits. Wait for an observable readiness or action outcome; retain stronger repository wait rules.
- For each changed actionable control, exercise the action and assert the resulting mutation, persisted value, navigation, refreshed state or expected validation/authorization/concurrency response. Presence alone is not coverage. For an editor, modal, drawer or picker, test opening and saving/applying.
- Cover loading, empty, error, denied, disabled and responsive states as applicable. A disabled control does not satisfy coverage of its enabled path. Check the exposed reason; shared controls need direct shared tests or parity scenarios across consuming screens.
- Include keyboard interaction, accessible names, focus behavior and relevant accessibility checks. Keep visual snapshots bounded to reviewed states; investigate differences before updating baselines.
- Retain failure traces, screenshots, console logs and relevant sanitized application logs; retain video where configured. Report the exact command, scope, result and unavailable prerequisites. Never claim browser coverage when only other layers ran.

## Contracts, review and delivery evidence

- Maintain affected API, data, configuration and permission contracts plus commands, events, workflow states, invariants, projections, problem details and module ownership. Read complete authoritative documents when governance, drift checks or completeness require them; never invent missing facts.
- Review changed callers, contracts, tests, architecture boundaries, security, schema/query behavior, concurrency, deployment and operational effects. Scale depth to risk and report findings by severity with exact sources and verification limits. Claim graph-assisted review only when graph evidence was actually obtained.
- Route repository discovery, documentation registration, planning and handoff through CIS. Preserve useful repository metadata, issue fields and domain obligations. External trackers mirror reviewed plans; they do not become a second planning authority.
- Read actual evidence artifacts and supply the required input paths to evidence composers. A tool's default or empty output is not proof that a contract is unchanged or a check is not applicable. Distinguish passed, failed, skipped, unavailable and not applicable with a recorded basis.
- Before retiring old routing or evidence instructions, inspect dependent CI scripts, validators, workflows and handoff procedures. Migrate their required inputs and checks explicitly. Removing prose or a link does not disable an automatically applied file or migrate an enforcing gate.
- Use the repository delivery policy and `cis verify` evidence. State unresolved findings, coverage gaps and next actions in the handoff; never approve or silently bypass a gate because instructions were merged.

## Scaffolding, diagnostics and model usage

- Use `cis template` or a repository's distinct deterministic scaffold generator where its templates fit. Identify and describe the smallest template, read its schema/example, gather verified source facts, prepare and validate the model, dry-run, inspect planned paths, then render. Never generate business policy or contract meaning from placeholders.
- Keep intentional overwrites explicit, inspect existing files first and record template, model, output paths and results. Generated code still requires domain implementation, tests, security and contract co-changes.
- Use `cis diagnostics` and retain distinct repository runtime-diagnostics tools when they supply needed telemetry. Bound queries and live tails, correlate traces, inspect stream health, stop tails before handoff and explain fallback to other logs. Do not substitute telemetry for tests or contract validation.
- Sanitize runtime evidence before sharing, keep streams out of commits and require explicit production access/retention controls. Do not transmit private diagnostics to a model without the applicable authorization.
- CIS feedback records CIS invocations. Preserve another tool's own run history, token/cost usage summaries, exports and retry-chain diagnostics when it remains in use; these are distinct evidence stores.
- Apply each tool's model qualification, cloud opt-in and execution rules to that tool's runs. Do not extend them to independently authorized CIS providers. Model approval does not grant repository, production or delivery approval.
