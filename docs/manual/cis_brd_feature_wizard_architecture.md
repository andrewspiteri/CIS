---
title: "cis brd feature wizard architecture"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-09-20"
review_cadence: "on command change"
cis:
  stable_id: change-impact-studio:manual:cis-brd-feature-wizard-architecture
---

# `cis brd feature wizard architecture`

Generate feature-specific C4 views inside the **Solution architecture and diagrams**
step of the feature definition wizard.

```text
cis brd feature wizard architecture status --slug <feature> --workspace <authority>
cis brd feature wizard architecture prepare --slug <feature>
  --expected-revision <wizard-revision> --workspace <authority>
  [--format human|json|agent]
```

In VS Code, choose **Save answers and generate C4 diagrams**. CIS saves the displayed
architecture answers, then generates system context, container and scoped component
views from the feature BRD, saved technical/architecture/integration direction and
the existing product architecture. The views appear in this step. Open each diagram
in a full-size tab or export its SVG. Edit the answers and regenerate to refine them.

Generation uses a local model for a bounded data proposal and CIS's validated C4
renderer for passive SVG. Existing identities and their evidence status come from
the product C4 model; new responsibilities are marked proposed. Component labels and
descriptions retain the selected BRD wording. Model-suggested integration endpoints
are shown as unresolved candidate contracts, with direction and transport to confirm.
The component view describes responsibilities and external contracts; it does not assert
an internal call sequence. A prepared product C4 model is required first.
A repository boundary alone does not establish a container or deployment. Proposed
host placement, runtime and transports remain subject to architecture review.

The command writes only derived previews in `.cis/local/feature-architecture/<slug>`.
It never changes or approves the canonical product architecture, feature answers or
delivery design. The wizard's explicit save action saves answers separately. Status
never calls the model and is queried only when viewing the architecture step. Reopening
unchanged diagrams uses the cache. BRD, saved relevant direction, ownership or product
architecture changes mark the gallery stale; unrelated page saves do not invalidate it.

Preparation requires the revision from [wizard status](cis_brd_feature_wizard_status.md).
Concurrent generations are blocked; source drift, invalid model output and failures
preserve the previous gallery. No remote model fallback is enabled.
