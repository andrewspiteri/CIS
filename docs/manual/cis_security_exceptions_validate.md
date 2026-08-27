---
title: "cis security exceptions validate"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-security-exceptions-validate
---

# `cis security exceptions validate`

Validate exact, expiring, human-governed accepted security findings.

```text
cis security exceptions validate [--strict] [--repo <path>] [--format <human|json|agent>]
```

Each row requires scanner, exact fingerprint, classification, reason, expiry, owner, approver, and approval reference. Wildcards and expired entries fail validation. Strict mode requires the canonical registry even when it has no accepted findings.
