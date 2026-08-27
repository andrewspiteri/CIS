---
title: "CIS Security Suite Profile"
type: security-suite-profile
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on scanner, command, result adapter, target, or CI-tier change"
cis:
  stable_id: change-impact-studio:reference:security-suite-profile
---

# CIS security suite profile

| Suite ID | Component | Category | Tool | Command | Working directory | Result format | Result path | Target | Applies when | CI tier | Fail severities | Artifacts |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| cis-sast | repository | sast | semgrep | node tools/run-security-scan.mjs sast | . | semgrep-json | .cis/local/security/results/semgrep.json | src and tests | source changes | pr | critical,high | retain-on-failure |
| cis-secrets | repository | secret | gitleaks | node tools/run-security-scan.mjs secrets | . | gitleaks-json | .cis/local/security/results/gitleaks.json | complete revision | always | pr | critical,high,medium,low | retain-on-failure |
| cis-filesystem | repository | filesystem | trivy | node tools/run-security-scan.mjs filesystem | . | trivy-json | .cis/local/security/results/trivy-fs.json | dependency manifests | dependency changes | pr | critical,high | retain-on-failure |
