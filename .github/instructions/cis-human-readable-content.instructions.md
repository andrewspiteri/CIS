---
applyTo: "**"
---

# Human-readable content

Apply these rules when a task creates or changes prose that a person reads; they do not authorize unrelated code edits.

- Follow `docs/standards/human-readable-content-standard.md` (Draft policy; existing domain authority remains controlling), and retain any stronger domain-specific authority rule.
- Use `.github/skills/cis-technical-writing/SKILL.md` for documents and reports, and `.github/skills/cis-ux-writing/SKILL.md` for interface wording.
- Preserve requirements, conditions, exceptions, uncertainty, identifiers, protected blocks, and approval scope. Style does not override meaning.
- Explain the task or result using concrete actors and familiar words. Retain necessary technical detail and access to the source.
- Use `.github/skills/cis-content-review/SKILL.md` for an intent-preservation check proportionate to the change. Report material ambiguity; do not silently resolve it.
- Do not change JSON fields, reason codes, commands, permission checks, or lifecycle behavior to make a label friendlier.
- Preserve the canonical BRD's hidden evidence/comment contract. Do not create a competing canonical summary.

These file-scoped instructions are not proof of runtime prompt integration. Direct-generation paths and supported agent hosts require explicit routing and tests.
