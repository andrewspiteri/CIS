---
name: cis-graph-context
description: Build, validate, search, traverse, trace, and package the CIS repository graph. Use when locating components, contracts, references, symbols, callers, tests, dependencies, decisions, or bounded evidence before impact analysis, planning, implementation, or verification.
---

# CIS Graph and Context

1. Resolve the repository from `.cis/repository.yml`.
2. Run `cis graph status --format agent`. If the graph is missing or stale, run `cis graph build --format agent`, then check status again. Use `cis graph validate` only when deep structural assurance is required.
3. Locate exact roots with `cis graph find` or `cis context search`; disambiguate with full IDs and kinds.
4. Use `cis graph related` or `cis graph trace` for evidence paths.
5. Use focused `cis context contract|symbol|references|callers|tests-for` queries or `cis context pack` for bounded source evidence.
6. Treat C# invocation arguments as privacy-safe shapes, and `dataflow` as intraprocedural call ordering only. Inspect canonical source before making semantic or runtime claims.
7. Report graph schema/build, freshness, compiler capabilities, diagnostics, truncation, roots, and omitted evidence.

Use supported commands for local state; never hand-edit protected accounting or recovery records or present graph output as canonical authority. Do not imply complete semantic coverage or include proposed edges silently. Canonical outcome/task state and existing legacy authorities retain their roles; compatible graph evidence remains reusable while its material inputs match.
