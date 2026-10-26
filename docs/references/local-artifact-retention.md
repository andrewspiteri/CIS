---
title: "Change Impact Studio Local Artifact Retention"
type: local-artifact-retention
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-26"
review_cadence: "on evidence, storage, privacy, or recovery-policy change"
cis:
  stable_id: change-impact-studio:reference:local-artifact-retention
---

# Change Impact Studio local artifact retention

Rules apply only to immediate entries beneath the declared `.cis/local/` path. Canonical Markdown and user-managed repository files are never eligible.

| Family | Path | Retain days | Keep latest | Action | Maximum bytes |
|---|---|---:|---:|---|---:|
| graph | .cis/local/graph | 0 | 0 | preserve | |
| api | .cis/local/api | 0 | 0 | preserve | |
| references | .cis/local/references | 0 | 0 | preserve | |
| frontend | .cis/local/frontend | 0 | 0 | preserve | |
| policy-impact | .cis/local/impact | 0 | 0 | preserve | |
| index-cards | .cis/local/index-cards | 0 | 0 | preserve | |
| brd-question-suggestions | .cis/local/brd/questions | 0 | 0 | preserve | |
| context | .cis/local/context | 30 | 20 | archive | |
| workflows | .cis/local/workflows | 30 | 20 | archive | |
| testing | .cis/local/testing/runs | 30 | 10 | archive | |
| mutation-runs | .cis/local/testing/mutation/cis-safety/runs | 1 | 2 | delete | 536870912 |
| security | .cis/local/security/runs | 30 | 10 | archive | |
| ci | .cis/local/ci | 14 | 20 | archive | |
| diagnostics | .cis/local/diagnostics | 30 | 20 | archive | |
| agents | .cis/local/agents | 30 | 20 | archive | |
| generation | .cis/local/generate | 30 | 20 | archive | |
| ai-cache | .cis/local/ai/cache | 7 | 50 | delete | |
| ai-qualification | .cis/local/ai/qualification | 90 | 20 | archive | |

`Maximum bytes` is optional. A missing or empty cell means no byte budget; existing five-column profiles remain valid. Values are integer bytes, so 536870912 means 512MiB. Newest entries protected by `Keep latest` remain protected under byte pressure. Outside that allowance, age makes an entry eligible; if projected live usage is still over budget, the oldest remaining unprotected entries also become eligible before reaching that age. `preserve` never removes entries.

Review `cis artifacts plan --family mutation-runs --format agent` after run writers finish. It reports the bytes eligible for removal and the projected live size. If the newest two runs alone exceed 512MiB, planning reports the unmet budget and blocks cleanup; review the protected allowance or budget explicitly. The profile does not cap writes or schedule deletion. Cleanup requires a separate `cis artifacts clean --family mutation-runs --yes` after plan review. A deletion leaves hashes, not recoverable content.

Diagnostic and iteration artifacts are transient. Keep a native failing report and
only the context needed to investigate an open issue. Delete successful canary
captures, duplicate reports and superseded attempts when they are no longer useful;
delete the remaining diagnostic material when the issue is fixed. Keep a brief
outcome and commit reference where useful. A current qualification or release
record follows its explicit retention requirement; ordinary iterations do not
become permanent release evidence.

These lifecycle decisions require a reviewed cleanup after writers finish. The
planner uses age, count and bytes; it does not infer whether an issue is resolved.
Review the profile before removing a protected entry, and preserve evidence still
needed by an open issue or current gate. Do not create archive bundles merely to
retain completed diagnostics. Full source/runtime snapshots and verbose logs are
reserved for bounded investigations.

Archive families remain reversible, but their ZIPs, manifests and retrieval copies consume disk. Output reports configured live bytes and artifact-management bytes separately; its total excludes unconfigured paths and scratch trees elsewhere. The byte budget applies only to the configured live family, not to the combined archive footprint.
