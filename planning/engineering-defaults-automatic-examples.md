# Automatic local reference examples

Historical qualification record: candidate identities, commands and unresolved states below describe that checkpoint. For the completed Windows .NET milestone and current trial instructions, use the [readiness record](engineering-defaults-trial-readiness.md) and [trial guide](engineering-defaults-dotnet-trial-guide.md). Earlier receipts and findings remain retained.

Normal C# initialization now plans a bundled reference copy under `.cis/local/examples/dotnet-engineering/`. It is Git-ignored and excluded from project classification, implementation graph inputs and execution identity. Initialization copies files only. It does not restore packages, run tests, start containers or adopt the example's framework in the application.

`--examples off` persists in `.cis/example-settings.json`; it preserves existing copies and disables subsequent installation and updates. `--examples auto` re-enables them. Use `--dry-run` before applying a reconciliation. Generated engineering guidance, assurance instructions and the .NET testing skill point to the local README and portable replay skill.

## Ownership and updates

The local manifest identifies the available bundle by content digest and records last-applied file hashes. Only unchanged CIS-installed files are updated. Edited and unowned files are preserved, including preexisting files identical to the bundle. Retired files keep their hashes and recurring warnings while present. Reintroduced unchanged managed files may update. Missing retired files are removed from metadata without deleting content.

Writes replace individual directory entries, preserving any external hard-linked alias. Unsafe paths, malformed metadata and documentation-root overlap fail before initialization writes. Planned destinations must be unique. Reads are bounded; Unix file-type inspection uses the system metadata predicate before opening files. No multi-file crash atomicity or protection against hostile concurrent path swaps is claimed.

## Qualification checkpoint

Candidate **0.3.0-engineering.20261009.28** is installed only at `.cis/local/engineering-tool-rehearsal-28/cis.exe`. Package SHA-256 is `eca537c0f1870ea6b935b4c47784c934d7f5d2b9779aca7eadf5c6b8f5280bc0`. All 42 packaged CIS/host assemblies match Release output, the installed Repository assembly matches the tested copy, and all 62 recipe files match source.

Final native qualification passes **256 Repository tests and 17 Host tests**. Two Unix-only FIFO cases are explicitly skipped on Windows; Unix execution is not qualified. The 17-case focused selection overlaps the full suite and is not additive. Four Windows reproductions failed before correction and then passed. Independent source review requested five corrections; its separate closure review is **ready**, with no remaining in-scope findings. Strict CIS source documentation and graph validation pass, as does `git diff --check`.

The first native CLI rehearsal passed copying, Git ignore, graph exclusion, strict docs/skills, deterministic skill audit, idempotence, opt-out and edit preservation. It used the pre-review built CLI and is retained as historical qualification. The separate candidate28 installed-tool rehearsal now passes the same checks after correction, including all 62 installed file hashes, zero discovered project components and no reference-code graph inputs. Generated skills pass strict validation and deterministic audit. No example tests, containers or dependency restoration were invoked by initialization.

Evidence is retained under `.cis/local/qualification/automatic-examples/`. Earlier candidate27 Foundation completion and its seven native task receipts remain historical evidence for that exact package; this change does not relabel them as candidate28 runs. The active installation and the user's trial directory are unchanged.

## Try the new initialization

In PowerShell, select this exact candidate and preview the changes to the existing trial repository:

```powershell
$cis = Join-Path '<CIS-worktree>' '.cis/local/engineering-tool-rehearsal-28/cis.exe'
& $cis repo init --repo '<trial-repository>' --root docs/cis --stack csharp --dry-run
```

After reviewing the preview, repeat with `--yes` instead of `--dry-run`. The automatic copy appears
under `.cis/local/examples/dotnet-engineering/`; an earlier manually exported demo is preserved
and is not adopted or moved by this command. Use the selected repository root for CIS graph commands,
not the local example directory. The CLI assembly version remains the base version; package identity
and the exact executable path above select candidate28.

See [initialization guidance](../docs/manual/cis_repo_init.md#local-reference-examples) for opt-out,
update ownership, metadata recovery and explicit example execution. Candidate27's completed Foundation
rehearsal is not rerun or reapproved by this bounded automatic-example qualification.
