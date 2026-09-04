---
title: "CIS-0002 verification evidence"
type: verification-record
status: Accepted
change_id: CIS-0002
test_run_id: cis-0002-release-20260828-r9
security_run_id: cis-0002-security-20260828-r10
authority: human-reviewed
---

# Verification evidence

Record exact commands, artifacts, results, blockers, deferrals, coverage, and
independent-assurance findings as agent tasks are completed.

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-030 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1019; failed=83; possibleTokenSavings=425975; ledgerDigest=sha256:c21c5d6258d8a6f2b47e7a76c2bc77851e55ee2c48861f0eb687cc08bfed37d1 |
| WORK-040 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1021; failed=83; possibleTokenSavings=425975; ledgerDigest=sha256:85add7c4c64655e3da5d7d8d36e79cec150a5f1dc4031eeb87821603e7c2de75 |
| WORK-080 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1023; failed=83; possibleTokenSavings=425975; ledgerDigest=sha256:b276b8bb6d9daf57684175c7e047beb8a7531f2c6d0ace5966bd252a09972cf1 |
| WORK-090 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1025; failed=83; possibleTokenSavings=425975; ledgerDigest=sha256:3430d5b4a27cca6ce58e8b8725bf1ac2d4f1dd31af671926729b714a3f537396 |
| WORK-130 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1027; failed=83; possibleTokenSavings=425975; ledgerDigest=sha256:0e950e46d990683d1264d71f4fe4ce33c2bce66526d048ffe161ceb83f5e95d8 |
| WORK-140 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1029; failed=83; possibleTokenSavings=425975; ledgerDigest=sha256:e0af6215403e864905469da094fd97c41b627f1f42a7f41600bb78313a50803f |
| WORK-160 | Exact test trace | `cis test trace CIS-0002 --run cis-0002-release-20260828-r9` | Passed | 24/24 `TC-AGENT-*` identities passed in reconciled executions. |
| WORK-160 | Regression and coverage | `.cis/local/testing/runs/cis-0002-release-20260828-r9/manifest.json` | Passed | 448/448 tests; 29,602/37,663 lines (78.6%) against 75%. |
| WORK-170 | Mutation assurance | `.cis/local/testing/mutation/cis-abstractions.json` | Passed | 81.54% score against 80% break threshold; zero no-coverage mutants. |
| WORK-170 | Security assurance | `.cis/local/workflows/cis-0002-linux-20260828-r7/` | Passed | Semgrep SAST, bounded secret scanning, and Trivy filesystem scanning completed without findings. |
| WORK-170 | Failure classification | `.cis/local/workflows/cis-0002-linux-20260828-r6/state.json` | Resolved | Source-only host lacked a global `cis` executable; classified `missing-prerequisite`, corrected, and proven by fresh R7/R9 runs. |
| WORK-170 | Source provenance | Canonical snapshot `69dcf7ccfd3dcd12fbd4ec2fcd9d551d73a3da044efd5a8b742c9c7d4bac4f52` | Recorded | Final manifest-based archive contained 918 canonical tracked/untracked files and excluded `.git`, `.cis/local`, and build output. |
| WORK-170 | Evidence transfer | `cis-0002-r9-evidence.tgz` | Passed | SHA-256 `baccad64f0e3305241fcf315676369b77a192faaf8441ad0ba26a9a3425fb3c0`; archive paths were inspected before extraction. |
| WORK-180 | Packaged CLI smoke | `AndrewSpiteri.ChangeImpactStudio.0.3.0.nupkg` | Passed | SHA-256 `f4c4f000a67d9896f9660538fddee6d83c9010db1ee82aa8feecaa605bbedbf4`; isolated install exposes `agent`, `agent-codex`, `agent-claude`, and every agent lifecycle command. |
| WORK-180 | VSIX construction | `change-impact-studio-0.3.0.vsix` | Passed | SHA-256 `91ff6cfb0ad2bf92c1cec5f9d549584c6b9eccc7dde31568c51b791e39dd3ab9`; Windows PowerShell 5.1 path composition was corrected and rerun successfully. |
| WORK-180 | Final security delta | `cis-0002-security-20260828-r10` | Passed | Semgrep, secret scanning, and Trivy passed against final canonical snapshot `69dcf7…c4f52`; evidence archive SHA-256 `347521dd778e698497fc1325246a88cc5bcd2e32d07bec0679682859ad6bc98c`. |
| WORK-160 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1032; failed=83; possibleTokenSavings=425975; ledgerDigest=sha256:467261e7de14afa2bd59e331c327aa0b96c07d6268b7ec6e57e72070aa582c0d |
| WORK-170 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1034; failed=83; possibleTokenSavings=425975; ledgerDigest=sha256:2d2120e3f0af8cec1bc5b703cd7e0911a21c125e5cae32c64c724692087fc71e |
| WORK-180 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1051; failed=83; possibleTokenSavings=453430; ledgerDigest=sha256:2c7b82d16fc74f41f92ae0ac671cd5dfd9e7d8851aae36f91afeabb51ea0036c |
| WORK-000 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1086; failed=83; possibleTokenSavings=467162; ledgerDigest=sha256:9a85109401ec41fba5ff035fdbee33891dfef1c14774c388acd54379ec3aadd9 |

## Human acceptance

- Reviewer: Andrew Spiteri
- Accepted UTC: 2026-08-28T13:11:52.8693183+00:00
- Rationale: The provider-neutral contracts, Codex and Claude adapters, bounded foreground lifecycle, permission and isolation controls, durable evidence, initialization and Doctor integration, exact test traceability, independent assurance, and release packaging are accepted with no product deferrals or unresolved security risks.
- Snapshot digest: `8c551086cbca0bb112e5f855c5de690608dd3d87cb95d2cb421bf19a59e62398`
