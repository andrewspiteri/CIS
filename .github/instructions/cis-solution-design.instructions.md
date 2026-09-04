---
applyTo: "docs/{architecture/overall-solution-design.md,references/component-sheet.md}"
---

# CIS overall solution-design authority

- Treat the overall design and component sheet as one atomic review and approval bundle.
- Preserve stable identity, managed markers, technical-intent hash, component IDs, and approval metadata.
- Keep the architecture implementation-independent: components are logical ownership boundaries unless deployment is explicitly approved.
- Give every component one clear responsibility, owned concerns, exclusions, and BRD traceability. Cross-component access uses an explicit owned integration contract.
- Cover context, topology, data consistency, integrations, security, operations/recovery, verification, traceability, and the UI-design handoff.
- Do not add business requirements or contradict the Active technical intent. Promote durable changes through technical intent or an ADR first.
- Run `cis solution-design validate` before asking for one whole-bundle approval. Agents may not approve on the user's behalf.
- An upstream or bundle-content change makes both artifacts stale; rerun `cis solution-design init`, review the delta, and renew the one bundle approval.
- After approval, use `cis ui-direction questions init` and `cis ui-direction init` to establish shared experience direction before backlog or feature design.
