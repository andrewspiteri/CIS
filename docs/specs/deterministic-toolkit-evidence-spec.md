---
title: "Deterministic Toolkit Evidence and Policy Impact"
type: technical-specification
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-08-27"
review_cadence: "on tooling evidence, generation, diagnostics, or policy-impact change"
cis:
  stable_id: change-impact-studio:spec:deterministic-toolkit-evidence
---

# Deterministic toolkit evidence and policy impact

This specification closes the reusable PARR-to-CIS gaps around template applicability,
agent tooling evidence, policy-impact routing, and structured diagnostics. CIS provides
provider-neutral repository behavior rather than importing PARR application assumptions.

## Template decision

`cis generate applicable` deterministically ranks repository-owned templates from task
and changed-path terms. It reports confidence, reason, and required model fields without
calling an LLM. A handoff records used, not applicable with a specific reason, or
unavailable with a specific reason. Applicability never grants overwrite authority.

## Tooling evidence

Changed-file classification selects evidence obligations. Source work requires an exact
context, graph, or frontend routing command and a reconciled test run with its run ID.
Every changed workstream records the generation decision and local usage-ledger evidence
or a bounded bypass. Strict validation rejects warnings and placeholder text.

## Policy impact

Policy roots declare `targets:` from backend, frontend, documentation, infrastructure,
security, api, testing, verification, release, or repository-governance. Analysis produces bounded derived JSON and Markdown candidate
sets beneath `.cis/local/impact/policy/`. Missing targets may be inferred for migration
but strict mode fails the warning. Candidates route review; they do not authorize or
prove remediation.

## Diagnostics

Diagnostics profiles support bounded text, JSONL, workflow, browser, and container logs.
CIS validates profiles, refuses enabled sensitive input, preserves original line evidence,
normalizes severity, redacts credential patterns, and calculates stable fingerprints.
Filters narrow source, level, content, and time. Analysis and normalized exports remain
derived and never replace raw test artifacts.
