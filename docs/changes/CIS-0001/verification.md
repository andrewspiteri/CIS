---
title: "CIS-0001 verification evidence"
type: verification-record
status: Draft
change_id: CIS-0001
authority: human-reviewed
test_run_id: cis-0001-vscode-release-20260828-r9
---

# Verification evidence

Record exact commands, artifacts, results, blockers, deferrals, coverage, and
independent-assurance findings as agent tasks are completed.

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-030 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1216; failed=97; possibleTokenSavings=524157; ledgerDigest=sha256:d6c9b0534a7c6921fe12d87c699478aa40a6b50a3d3f692221998e01772d5798 |
| WORK-040 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1224; failed=97; possibleTokenSavings=524157; ledgerDigest=sha256:d80acf5cbe623738a8df3c24cc614be8b3b5af8c7d2f6abc18dfc5766c0e2cbc |
| WORK-080 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1226; failed=97; possibleTokenSavings=524157; ledgerDigest=sha256:b54452ad0b8549b4adea8199db344281ac7899f73bfdbbb1397e5079cb055bf2 |
| WORK-100-BACKOFFICE | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1251; failed=97; possibleTokenSavings=524157; ledgerDigest=sha256:230fb4daf3309d9f8a56149bcfa542798ee46ac78d87f41685d40016c10127fa |
| WORK-160 | Clean Linux release assurance | `.cis/local/testing/runs/cis-0001-vscode-release-20260828-r9/manifest.json` | Passed | Release build; 455 reconciled .NET executions; 60 reconciled extension executions; all 23 `TC-VSC-*` identities passed; independent mechanical provenance is recorded. |
| WORK-160 | Coverage | `.cis/local/testing/coverage/summary.json` and `.cis/local/testing/coverage/vscode-extension-summary.json` | Passed | Repository ratchet 78.68% against 75%; new VS Code extension production scope 95.51% against 95%; no extension exclusions. |
| WORK-160 | VSIX clean install and activation | `artifacts/vscode-cis-0001-final/change-impact-studio-0.3.0.vsix` and `artifacts/vscode-cis-0001-final/activation-smoke.json` | Passed | SHA-256 `d70d897fab715a95e51ceba846d1f9c443cf284432effc2e3b48ad5049afd650`; isolated VS Code profile reported `andrewspiteri.change-impact-studio` 0.3.0 active and executed `cis.refresh`. |
| WORK-170 | Mutation assurance | `.cis/local/testing/mutation/cis-abstractions.json` | Passed | 81.54% against the established 80% break threshold; zero no-coverage mutants; the 12-survivor set is unchanged from the accepted CIS-0002 baseline. |
| WORK-170 | Revision-bound security assurance | `.cis/local/security/runs/cis-0001-vscode-security-windows-20260828-r15/manifest.json` | Passed | Semgrep SAST, Gitleaks secret scanning, and Trivy filesystem scanning were repeated against the exact final source after the traversal, Doctor, portability, initialization, and permission-reference fixes; each reported zero critical, high, medium, or low findings. |
| WORK-170 | Cross-platform race regression | `.cis/local/workflows/cis-0001-vscode-linux-20260828-r5/dotnet-tests.log` | Passed | The cancellation/completion evidence race that failed r3 was fixed with serialized terminal evidence finalization; the exact Linux regression passed in r5 and the complete release suite passed in r9. |
| WORK-170 | Cross-platform frontend graph regression | `/data/cis-0001-vscode-20260828-r2` on `192.168.88.161` | Passed | The focused Release suite passed 3/3 on Linux after bounded traversal began pruning generated artifact trees before descent; the local strict graph then rebuilt 10,550 nodes and 49,013 edges with zero diagnostics. |
| WORK-170 | Final hardening regressions | `dotnet test ChangeImpactStudio.slnx -c Release --no-restore`; focused Linux suites under `/data/cis-0001-vscode-20260828-r2` | Passed | The definitive local Release solution run passed with zero failed tests. References passed 7/7 and Repository passed 80/80 on both Windows and Linux; the differently named Linux checkout then completed Repository Doctor with zero errors. |
| WORK-170 | Idempotent repository reconciliation | `cis repo init --root docs --repo . --yes` | Passed | Reinitialization preserved human-managed content, recognized 63 classified components, seeded the applicable secure-feature skill, permissions reference/spec, and secure-feature standard, and eliminated unapplied-init and VS Code component-alias findings locally. |
| WORK-170 | Preserved failure evidence | `.cis/local/workflows/cis-0001-vscode-mutation-20260828-r6/` and `.cis/local/workflows/cis-0001-vscode-security-windows-20260828-r8/` | Recorded | Missing restored tool and stale Docker runtime sockets remain classified failed attempts; neither result was relabeled or overwritten by its successful rerun. |
| WORK-180 | Reproducible Linux evidence archive | `artifacts/cis-0001-vscode-release-evidence-r9.tgz` | Passed | SHA-256 `7e6e4d694f1f5ecc338bdae723c4d2dbdf02aa4032e3107f77f1c29df59c0814`; contains bounded workflow, result, coverage, mutation, and provenance manifests without raw source or credentials. |
| WORK-160 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1306; failed=100; possibleTokenSavings=530657; ledgerDigest=sha256:d14bcd388d4598e4ddb987d9d3c5704b1567e3e5760886987e1e1bac4a67c5fe |
| WORK-170 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1308; failed=100; possibleTokenSavings=530657; ledgerDigest=sha256:64f2414d3422062ed6259a879bcffbbdadb658567e108f9e520b944efdccd762 |
| WORK-180 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1384; failed=107; possibleTokenSavings=589673; ledgerDigest=sha256:b31abb48ef53f1da2a329d5945153b71c9469d0952ff64db308408245f4ac3d1 |
