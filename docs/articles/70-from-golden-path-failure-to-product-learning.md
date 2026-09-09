---
title: "Turning a Golden-Path Failure into Reviewed Product Learning"
type: article
status: Draft
series: "CIS in Practice"
series_order: 10
owner: "Andrew Spiteri"
last_reviewed: "2026-09-09"
review_cadence: on learning or golden-path change
summary: "A disciplined route from real integration failure to specification, implementation, regression evidence, and reviewed learning."
cis:
  stable_id: change-impact-studio:article:golden-path-failure-product-learning
---

# Turning a golden-path failure into reviewed product learning

A golden path reveals that the packaged CIS executable rejects a valid provider-owned
dynamic API surface even though current source supports it.

## Separate the failure layers

Evidence distinguishes sample configuration, CIS specification, source implementation,
test coverage, build artifact freshness, and packaging. The sample should not be weakened
to match a stale executable.

## Identify the owning contract

If source behavior is correct and tested, the release process or artifact freshness owns
the gap. If source also lacks the capability, update the API specification and governance
model before implementation.

## Deliver the fix completely

The change includes specification, source, focused regression, manual, package build,
installed-tool smoke test, and golden-path replay. Local artifact retention keeps the
diagnostic bundle, logs, and test outputs available long enough to reproduce the failure
without making those artifacts durable product authority.

## Record reviewed learning

The durable lesson may be: every golden-path replay must verify representative behavior
against the packaged artifact, not only source builds. A human reviews that recommendation
before it enters learning history or release policy.

## Reproduce the failing boundary

Record the golden-path source revision, packaged CIS version and checksum, installation
location, provider or framework version, exact command, configuration, and observed error.
Then run the smallest focused check that can distinguish sample misuse, unsupported
behavior, stale packaging, and source regression.

Do not update the sample until the owning layer is known. A workaround can erase the only
representative failure evidence.

## Compare source and distributed behavior

Suppose source tests accept a reviewed provider-owned dynamic route declaration, while
the installed tool rejects it. Running the same fixture against source and package shows
that specification and source agree and the distributed artifact is stale or incomplete.

If both reject it, inspect the product contract before implementing support. If the
package alone fails, rebuild, inspect package contents, explicit module registration, and
release inputs. The diagnosis determines the governed change.

## Deliver every affected layer

A complete fix may include:

- clarified specification and decision where meaning changed;
- source implementation;
- focused passing and failing regression fixtures;
- module and cross-module tests;
- command manual and article reconciliation;
- package content and version updates;
- installed-tool smoke tests;
- checksums and release notes; and
- golden-path replay from a clean state.

Passing only the new unit test leaves the original product boundary unverified.

## Preserve evidence without promoting raw logs

Retain bounded diagnostics, package manifests, logs, and replay artifacts under local
retention policy. The canonical change and learning record summarize exact versions,
checks, result, and rationale. Expiring a local artifact must not erase the lesson.

## Formulate a bounded learning proposal

The evidence may support a rule such as: every release that changes route extraction must
run the representative provider-owned dynamic-route fixture against the installed package.
That is more actionable than “improve packaging tests.” It names the observed gap,
expected control, and evidence source.

A human reviews whether the recommendation belongs in release policy, a workflow, a test
profile, or only the sample. Application records approved learning; it does not modify
source or standards autonomously.

## Replay to close the loop

After the fix, start from the governed sample baseline, install the new package, and run
the complete path. Record other failures separately rather than weakening assertions until
the demonstration turns green.

## Prevent source-only confidence

Add the representative behavior to the release gate that installs and invokes the
packaged artifact. Keep focused source tests because they diagnose failures quickly, and
keep package smoke coverage because it proves assembly registration, content inclusion,
dependency resolution, and installed command behavior. Neither layer replaces the other.

Release evidence should name the package checksum and tested version so a later replay
cannot accidentally use a different local build.

## Takeaway

Do not hide integration failures in demos. Trace them to the owning contract, fix every
affected layer, verify the distributed artifact, and promote only the reviewed lesson.

## Canonical CIS sources

- [Implementation roadmap](../specs/implementation-roadmap.md)
- [Versioning and release](../standards/versioning-and-release.md)
- [Execution, assurance, diagnostics, and learning](../specs/execution-assurance-and-learning-spec.md)
- [Local artifact retention](../references/local-artifact-retention.md)
