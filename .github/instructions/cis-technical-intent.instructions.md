---
applyTo: "docs/specs/technical-intent-spec.md"
---

# CIS technical intent authority

- The workspace authority owns one canonical workspace-scoped technical intent; participant repositories retain repository-scoped supporting intent documents.
- An Active, current BRD and fresh participant graphs are required before initialization or approval.
- Preserve `cis:technical-intent-baseline` markers, the BRD hash, participant build IDs, stable identity, schema, and approval digest.
- Complete every required technical section and keep unresolved choices in the structured `TI-DEC-*` table.
- Agents may draft technical direction and options. Only explicit human authority may resolve or defer decisions and run `cis technical-intent approve` with the exact reviewer and rationale.
- `cis change create`, `cis plan build`, and `cis plan import-spec` are blocked in a workspace authority unless technical intent is Active and current.
- Rebuild the workspace graph after canonical edits or approval. BRD, participant-baseline, or approved-content drift makes the intent non-current.
