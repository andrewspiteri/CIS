---
title: "The Friends Todo Golden Path"
type: article
status: Draft
series: "CIS in Practice"
series_order: 4
owner: "Andrew Spiteri"
last_reviewed: "2026-08-26"
review_cadence: on golden-path change
summary: "Why a small multi-repository product is a demanding end-to-end test of intent, classification, contracts, execution, and assurance."
cis:
  stable_id: change-impact-studio:article:friends-todo-golden-path
---

# The Friends Todo golden path

Friends Todo is intentionally ordinary: a web application, API, and infrastructure for
lists shared with friends. Its value is the number of engineering boundaries hidden by
the simple domain.

## What the replay exercises

The golden path covers workspace authority, participant initialization, BRD and technical
intent, graph federation, feature derivation, public/customer/backoffice classification,
API governance, authentication protocol routes, cache policy, provider-owned dynamic
routes, task execution, browser and integration tests, and final verification.

## Why complete replay matters

Focused tests can prove a command in a fixture. The replay exposes disagreements between
source and packaged tools, framework-generated files, real package behavior, multi-repository
paths, runtime providers, and documentation starters.

## Gaps become product evidence

When the replay finds a missing Express route extractor, unsafe protocol classification,
or stale packaged executable, the result is not patched only in the sample. It produces
a CIS specification, implementation, manual, and regression-test change.

## The golden path is not a demo script

A demo can work around product gaps. A golden path records them, fixes the owning product
contract, and reruns from governed baselines.

## Takeaway

Use a small but complete product to test the whole governance chain. The best golden path
does not prove the tool is perfect; it repeatedly reveals where the tool's model of real
delivery is incomplete.

## Canonical CIS sources

- [Implementation roadmap](../specs/implementation-roadmap.md)
- [Delivery and assurance](../specs/delivery-and-assurance-spec.md)
- [Change impact and bounded planning](../specs/change-impact-and-planning-spec.md)

