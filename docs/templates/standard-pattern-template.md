---
title: <Pattern title>
type: standard-inference-pattern
status: Draft
owner: <accountable human or team>
review_cadence: on compiler graph or architecture change
cis:
  stable_id: <repository-id>.pattern.<language>.<pattern-name>
pattern:
  version: 1
  description: <Graph convention recognized by this pattern.>
  languages:
    - csharp
  roles: []
  capabilities: []
  required_graph_capabilities:
    - compiler.symbols
    - compiler.calls
  subject:
    kind: symbol
    subtype: method
    facets:
      - compiler-bound
    property_equals: {}
    property_contains: {}
    path_prefixes:
      - src/
    excluded_path_prefixes: []
  requires:
    - direction: outgoing
      edge_type: calls
      target:
        kind: symbol
        facets:
          - compiler-bound
        property_equals: {}
        property_contains: {}
        path_prefixes:
          - src/
        excluded_path_prefixes: []
  forbids: []
  thresholds:
    minimum_occurrences: 5
    minimum_consistency: 0.80
  candidate:
    target: architecture
    proposed_level: SHOULD
    wording: <Complete proposed normative statement.>
    suggested_enforcement: architecture-test
    verification: <How a maintainer could verify the proposed rule.>
---

# <Pattern title>

## Intent

<Why this recurring graph shape may represent a repository standard.>

## Review cautions

<Known legacy patterns, generated-code exclusions, false positives, and architectural alternatives.>