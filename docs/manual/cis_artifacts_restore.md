---
title: "cis artifacts restore"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-artifacts-restore
---

# `cis artifacts restore`

Verify and restore an archive to its original local paths.

```text
cis artifacts restore --archive <id> --yes [--repo <path>] [--format <human|json|agent>]
```

Different existing content is a conflict and is never overwritten. Missing confirmation, digest mismatch, or a conflict returns exit code `4`.

Governed restoration inherits the owner or accepts `--change`, `--task`, `--authority`, `--parent-execution` and `--actor`. The actor needs cleanup/restoration delegation. Temporary staging and final copies remain inside the same finite storage ceiling; successful moves reconcile surviving bytes. Archive restoration uses ordinary finite operation time. It does not claim the special time reserve or authority for genuine native baseline recovery. If capacity or protected evidence prevents restoration, inspect the plan and return to the original allocation authority; do not delete accounting or alter archived evidence to bypass the hold.

Every original destination must still belong to the current owned roots and be clear of protected current evidence. An older archive grants no ownership of a path. Restoration into Testing output also takes its existing native output lease, so it holds while native work owns that output.
