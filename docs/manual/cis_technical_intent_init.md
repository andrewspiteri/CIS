---
title: "cis technical-intent init"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-03"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-technical-intent-init
---

# `cis technical-intent init`

Initializes or reconciles the authority repository's workspace-scoped technical intent
against the Active BRD semantic digest, registered repository classifications, Active
standards, and exact participant graph builds.

```text
cis technical-intent init [--workspace <path>] [--format <human|json|agent>]
```

The command requires exactly one workspace authority, an Active/current BRD and fresh
participant graphs. When generating a new intent, first complete the governed technical
questionnaire with `cis technical-intent questions init`. A supplied intent selected through
**Load existing documents** can instead be imported and reviewed as one baseline, without
first transcribing its facts into the questionnaire. Unreadable input and detected conflicts in
explicit direction still block adoption. For a new or
recognizably untouched starter document, it creates a schema-4
review scaffold containing:

- the BRD semantic source, requirement IDs, executive context, and outcome drivers;
- every registered repository/component classification and ownership role;
- Active standard identities, paths, targets/stacks, stable rule IDs, and content digests;
- the human-selected product surfaces, frontend/backend technologies, architecture style,
  repository topology, data stores, contracts, identity, hosting, operations, quality, AI,
  constraints, and exclusions;
- a logical component-responsibility map and primary interaction map;
- a BRD-derived product-module tree with stable `TI-MOD-*` identities, ownership rules,
  and detailed purpose, input/output, data/state, security, recovery, and verification profiles;
- a stable `TI-INT-*` integration-point catalog covering module entry points, cross-module
  flows, and detected external data, rule-engine, incumbent-system, transaction, evidence,
  model, notification, or search boundaries;
- classification-selected draft architecture guidelines;
- draft runtime, application, integration, security, operations, quality, and delivery direction;
- accepted `TI-DEC-*` high-level decisions bound to the exact questionnaire answers.

The generated content is a starting point, not an approved architecture. The resulting
intent still requires technical review and approval. After adoption, explicit facts in
the current accepted BRD and Technical Intent resolve matching questionnaire items as
source-derived answers, with source references and without invented human answer records.
During wizard setup, explicit facts in selected documents appear as source proposals
awaiting final baseline adoption. They can prepare the baseline without per-field
transcription. Outside that setup scope, draft directions remain suggestions.
Material source changes invalidate their derived
authority until the changed baseline is adopted; unresolved choices remain questions.
An explicit primary-store override that contradicts supplied direction stays visible
as a conflict and blocks readiness. Correct the recorded override or reconcile and
adopt the changed source; saving an answer does not approve a source amendment.
Optional supporting stores, asynchronous work and AI capabilities absent from an accepted
supplied scope remain **Not requested**. This disposition records no answer or exclusion;
adding a current requirement brings back the relevant unresolved choices.
CIS replaces only recognized
starter-placeholder sections; any human-authored section is preserved. Repeating the command
with unchanged evidence is idempotent.

CIS owns the marked baseline, business-evidence, questionnaire-evidence, component-map,
product-module architecture, integration-point catalog, technical-surface-evidence,
standards-evidence, and questionnaire-derived decision blocks plus
lifecycle metadata. These managed fact blocks refresh from their
canonical sources; architecture decisions and all other human-authored direction remain outside
them. Schema-4 baselines include the BRD, questionnaire digest, participant graph builds, and Active standard digests.
A semantic BRD or standard change, or a change to the registered participant/standard set,
requires renewed review. A participant graph build-version refresh updates provenance but does
not by itself change the approved direction.
Legacy reviewed schema-1 through schema-3 documents remain valid; generated Draft schema-2 and
managed Draft schema-3 scaffolds are upgraded after the questionnaire is complete. A legacy full-file BRD baseline is migrated once without
clearing an otherwise current approval. Run workspace graph build and strict validation afterward.

Exit `0` means initialized or unchanged; repository/workspace structural errors exit `2`;
and an unmet BRD, questionnaire, or participant-graph readiness gate exits `5` as a workflow block.
