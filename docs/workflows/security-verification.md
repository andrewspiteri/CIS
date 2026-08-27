---
title: "CIS security verification workflow"
type: workflow-definition
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on scanner, target, policy, or evidence change"
cis:
  stable_id: change-impact-studio:workflow:security-verification
---

# Security verification workflow

| Step | Command | Working directory | Test suites | Depends on | Continue on failure | Timeout seconds |
|---|---|---|---|---|---|---:|
| sast-scan | semgrep scan --config auto --json --output .cis/local/security/results/semgrep.json . | . | cis-sast | - | no | 1800 |
| secret-scan | gitleaks detect --source . --redact --report-format json --report-path .cis/local/security/results/gitleaks.json --exit-code 0 | . | cis-secrets | - | no | 900 |
| filesystem-scan | trivy fs --format json --output .cis/local/security/results/trivy-fs.json --scanners vuln . | . | cis-filesystem | - | no | 1800 |
