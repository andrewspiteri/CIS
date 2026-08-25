---
title: "cis design reconcile"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-23"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-design-reconcile
---

# `cis design reconcile`

Carries existing human authority through a provenance-only design refresh when no new
visual decision exists.

```text
cis design reconcile <change-id> [--repo <path>] [--format <human|json|agent>]
```

CIS releases the design barrier without requesting another human approval only when
all of these checks pass:

- the design pack is valid and `PausedForReview`;
- the delivery plan is `Approved` and pins the exact current feature file digest;
- exactly one feature-approval authority confirms that feature is Active, current,
  valid, and carries reviewer identity and rationale;
- an earlier approved design manifest exists; and
- the current PNG manifest digest is byte-for-byte identical to that earlier approved
  manifest.

The command records `Authority carried forward` in `wireframes.md` and `design.md`,
including the current hashes and the source feature authority. It never creates a new
human decision. Changed pixels, stale feature or plan evidence, missing prior approval,
or conflicting authorities leave the global pause in place and require
`cis design approve` or revision.
