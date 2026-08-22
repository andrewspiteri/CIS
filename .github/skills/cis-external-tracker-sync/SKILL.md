---
name: cis-external-tracker-sync
description: Preview, push, pull, inspect, and reconcile external issue mirrors for canonical CIS task documents without transferring approval or completion authority.
---

# CIS External Tracker Synchronization

1. Read `docs/specs/external-tracker-synchronization-spec.md`, the external tracker
   profile, repository delivery policy, plan, and affected task documents.
2. Run `cis tracker status`, then `cis tracker plan <change-id> --provider <key>`.
3. Confirm the provider is enabled, its assembly is loaded, the target is exact, and
   its credential source is least-privilege. Never print or persist credentials.
4. Run `cis tracker push` only with authority for the exact remote issue mutations.
5. Use `cis tracker pull` to detect drift; it must not rewrite canonical task state.
6. Surface every conflict. Resolve only with an explicit human reviewer and rationale.
7. Re-run plan/status and preserve durable external links in the canonical task.

Remote workflow state never approves CIS plans, designs, risks, deferrals, completion,
or final acceptance.
