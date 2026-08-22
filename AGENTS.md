# Agent guidance

## Architecture

- Keep `Cis.Host` as the composition root.
- Put public module contracts in `Cis.Abstractions`.
- Each module owns one top-level `cis <module>` command.
- Load built-in module assemblies explicitly.
- Never discover or execute assemblies from target repositories.
- Keep the future VS Code extension free of product-domain logic.

## Behaviour

- Provide human, JSON, and agent-oriented output when commands expose structured results.
- Keep standard output parseable and diagnostics on standard error.
- Use stable exit codes.
- Do not silently mutate canonical repository files.
- Add tests for module registration and command dispatch changes.

## Repository bootstrap

- Run `cis workspace init --root <docs-root>` first when creating the authority documentation repository for a multi-repository workspace.
- Run `cis repo init` with an explicit maintainer-selected documentation root when onboarding or reconciling a target repository.
- Treat confirmation-required status as a review gate. Do not pass `--yes` until the plan has been reviewed and authorization is clear.
- If init returns an error or collision, run `cis repo doctor` with the same `--repo` and `--root`, then report its evidence and suggested fixes before retrying.
- Import participant repositories with `cis repo import`, then build the workspace graph before starting governed documentation work.

## Business requirements

- Use `.github/skills/cis-govern-business-requirements/SKILL.md` for BRD discovery, reconciliation, validation, status, and approval work.
- Treat BRDs found in participant repositories as source evidence, never as proof that requirements are current.
- Agents may discover, draft, reconcile, and validate. They must not invent stakeholder decisions, assess sources on a human's behalf, or approve a BRD without explicit authorization, reviewer identity, and reason.
- After a development feature specification is created or changed, rebuild its repository graph and run `cis brd reconcile`; an Adopted feature specification must update the relevant BRD sections and be cited by source ID in Traceability.
- Run `cis graph build --workspace <workspace>` after BRD approval so derived context reflects the new canonical state.

## Technical intent

- Use `.github/skills/cis-govern-technical-intent/SKILL.md` after BRD approval and before creating a workspace change dossier.
- Treat the authority repository's workspace-scoped technical intent as distinct from participant repository intent documents.
- Agents may draft direction and options, but must not resolve or defer `TI-DEC-*` choices or approve technical intent without explicit human identity and rationale.
- `cis change create`, `cis plan build`, and `cis plan import-spec` require Active, current technical intent in a workspace authority.
- Rebuild the workspace graph after technical-intent approval.

## File routing

- Use `.github/skills/cis-file-index/SKILL.md` and `cis index find --text <terms>` before broad repository searches when an index is available.
- Treat `.cis/local/index-cards/` as disposable, non-authoritative model-assisted routing state; open source files before making factual claims.
- Automatic provider selection is local-only. Never pass `--allow-remote` without explicit user authorization to transmit the selected repository content.
- Never submit likely secrets, credentials, tokens, private keys, or certificate material to a model.

## Skill governance

- Use `.github/skills/cis-skill-governance/SKILL.md` after initialization, classification changes, imports, or edits beneath `.github/skills/`.
- Run strict skill validation before audit. Audit prefers a local model and falls back to a configured remote provider unless `--no-llm` keeps candidate skill content local.
- Treat model duplicate, overlap, and conflict findings as advisory review candidates. Only explicit `cis skills audit --fix` authorizes reversible quarantine; never merge, overwrite, or delete a skill automatically.

## Feedback loop

- CIS tool usage is recorded automatically under `.cis/local/feedback/` when an invocation can be associated with an initialized repository or workspace.
- Use `cis feedback summary` and `cis feedback opportunities`; treat the ledger as disposable local evidence.
- Preserve the recorded basis and confidence for possible token savings. Unestimated commands must remain zero-savings claims.
- If the same repository-aware command repeatedly fails, inspect recent usage and run `cis repo doctor` before retrying.

## Extension task providers

- Use `cis plan capability status` when loaded task providers overlap or a selected provider may be unavailable.
- Classify frontend requirements, wireframes, designs, and implementation tasks as exactly `public`, `customer`, or `backoffice`. Preserve a separate matched task chain for every affected type.
- Enforce `PUBLIC-ENDPOINT-CACHE`: every unauthenticated endpoint is cached and its public route/controller/handler never directly accesses a database or repository, including on cache miss. Cache population belongs behind an application/query abstraction.
- Never resolve mutually exclusive provider capabilities by load order. Record explicit human selection with `cis plan capability select`.
- Provider selection does not migrate existing tasks. Use `cis plan task migrate-type` only when both the canonical selection and replacement provider authorize it, preserving evidence and history.
