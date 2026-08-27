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
| sast-scan | node tools/run-security-scan.mjs sast | . | cis-sast | - | no | 1800 |
| secret-scan | node tools/run-security-scan.mjs secrets | . | cis-secrets | - | no | 900 |
| filesystem-scan | node tools/run-security-scan.mjs filesystem | . | cis-filesystem | - | no | 1800 |
