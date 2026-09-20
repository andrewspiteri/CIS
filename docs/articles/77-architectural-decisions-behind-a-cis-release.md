---
title: "Architectural Decisions Behind a CIS Release"
type: article
status: Active
series: "Product Evolution and Learning"
series_order: 7
owner: "Andrew Spiteri"
last_reviewed: "2026-09-10"
review_cadence: on release or ADR change
summary: "Use change-local decisions and promoted ADRs to explain why a release changed architecture, not only what files moved."
cis:
  stable_id: change-impact-studio:article:decisions-behind-release
---

# Architectural decisions behind a CIS release

Release notes explain outcomes. Architecture readers also need the choices that shaped
those outcomes.

## Begin in the change

Material questions are recorded with options, evidence, gates, and rationale in the
change dossier before implementation.

## Promote durable decisions

A decision that changes long-term module, storage, provider, compatibility, or trust
boundaries can be promoted to a catalogued ADR. The ADR retains alternatives and the
originating change.

## Link the release

Release communication can point to the ADR, affected technical intent, migration, and
verification evidence. Readers understand both consequence and reasoning.

## Preserve supersession

If a later release changes direction, a new decision supersedes the earlier one. History
shows when and why the architecture moved.

## Not every release choice is an ADR

An ADR is appropriate when a decision changes a durable module, storage, provider,
compatibility, trust, or integration boundary and will constrain future work. Selecting a
local variable name or one reversible test helper does not need architecture history.

The change dossier can retain narrower decisions without promoting all of them.

## Capture alternatives before implementation

Suppose CIS needs efficient graph queries. Options include parsing Markdown on every
request, storing canonical meaning in a database, or using a local derived SQLite
projection. Evidence covers authority, performance, rebuild, concurrency, portability,
and failure behavior. The reviewed choice keeps Markdown canonical and SQLite disposable.

Writing that ADR after implementation risks inventing a rationale that fits the code.
Capturing options and gates first makes the decision genuine authority.

## Promote with complete provenance

The promoted ADR should retain:

- stable identity and lifecycle;
- context and problem;
- considered options and consequences;
- selected direction and rationale;
- originating change and decision IDs;
- affected technical-intent and contract links;
- migration or compatibility effects; and
- reviewer and date.

The original decision remains in the dossier so readers can see how the release and
architecture history connect.

## Link decisions to product behavior

Release notes should explain the user-visible consequence. “Adopted local derived SQLite”
matters because queries become efficient without moving authority out of Git. “Kept the
VS Code extension thin” matters because terminal and editor clients share one lifecycle.

An ADR list without outcome context is useful to architects and opaque to users.

## Supersede rather than rewrite

If a later release replaces SQLite or changes the provider boundary, create a new
decision that supersedes the old one and explains changed evidence. Historical releases
continue to point to the architecture that governed them.

Do not edit the earlier ADR until it appears to recommend the new direction; that
destroys the explanation of past constraints and migrations.

## Verify architectural consequences

Tests and release evidence should demonstrate the properties promised by the decision:
deleting local state preserves meaning, target repositories cannot inject assemblies,
or an installed client uses the packaged CLI contract. The ADR establishes direction;
verification shows the implementation honored it.

## Use a release decision map

For a substantial release, a compact map can connect each user-visible change with its
governing decision:

| Product change | Durable decision | Verified consequence |
|---|---|---|
| One workspace per product | Separate product from ecosystem authority | Dependencies cannot receive owned writes |
| Direct provider execution | Controller-owned permissions and isolated worktrees | Candidate changes remain contained and evidence-only |
| Thin VS Code client | CLI owns domain behavior | Terminal and editor share lifecycle and validation |
| Local artifact retention | Retention does not promote authority | Archives remain integrity-checked and disposable |

The map helps readers navigate; the ADRs and change dossiers retain the full alternatives
and rationale.

## Takeaway

Use releases to communicate durable decisions, not recreate them retrospectively.
Capture options during planning, promote reviewed choices, and link them to outcomes.

## Canonical CIS sources

- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)
- [Architecture decisions index](../architecture/decisions/README.md)
- [`cis decision promote`](../manual/cis_decision_promote.md)
