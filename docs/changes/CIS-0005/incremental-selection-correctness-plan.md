---
title: "CIS-0005 compiler-backed incremental selection correction"
type: implementation-plan
status: Approved
change_id: CIS-0005
---

# Correct inheritance and interface dependency selection

The newly supplied incremental-testing review traces a real schema mismatch:
the compiler emits `inherits`, while the selector accepts only `inherits-from`.
It also shows that the existing interface projection regression inserts a reverse
edge that the compiler does not emit. These are selection/currentness issues;
they must be resolved before treating graph exclusion as the default workflow.

The reviewed published heads remain d449dcf / bd87587. Preserve the newer local
five-finding corrections and the completed CISLIB WORK-002 proof. That local proof
already demonstrates an actual unchanged Run with zero executed / one reused
batch, but it does not demonstrate safe automatic reduction of a broad suite.

## Bounded source work

| Work | Concrete result |
| --- | --- |
| Align relationship projection | Include compiler-emitted inheritance in the selector's forward/reverse dependency closure, retaining compatibility with the earlier inheritance spelling. Treat repository interface dispatch as a conservative candidate dependency, not a measured runtime binding. Preserve external-symbol and contextual-verification exclusions; unresolved relationships must not justify unsafe narrowing. |
| Regress actual compiler output | Extend the existing owning Testing/Graph fixtures through the real compiler/graph composition. Do not manually add expected inheritance or interface-dispatch edges. Cover direct and inherited consumers, a separately supplied interface implementation, independent consumers, and input closure/currentness consequences. |
| Exercise invalidation and reuse | Retain an actual extracted graph, generated filter and batch dependencies. Prove a base/implementation change invalidates relevant saved evidence and unchanged/independent evidence behaves conservatively. Use existing executable/native fixtures where needed; do not claim a real assertion was executed from a synthetic report. |
| Review and document | Run focused owning builds/tests and scoped whitespace checks sequentially. Independently review the relation directions, external-observation boundaries and regression setup. Document actual results and limitations in the existing source manual and verification record. |

Selector edits, compiler-fixture preparation and read-only review may run in
parallel with distinct file ownership. Shared builds and native tests remain
sequential. Reuse existing packages and test projects; add no framework or runner.

The fixture scope must include the review's inherited initializer example and an
interface-only consumer with a separately supplied implementation. A passing
direct-construction test alone is insufficient. Keep explicit eligible filters
broad enough for the fixture to demonstrate actual graph-derived selection.

Inspection also exposed two related barriers to that proof: property/indexer/event
reads had no compiler dependency relation, and partial types retained only their
first declaration path. The bounded correction therefore records actual static
member `references`, preserves every bound declaration, connects those declarations
in selection/currentness, and versions the compiler facts/cache so older snapshots
refresh. External interface metadata identifies unresolved dispatch for explicit
conservative fallback. This remains source dependency analysis; it does not infer
runtime instances, execute repository assemblies or create a DI resolution system.

## Approved resource amendment

Andrew Spiteri approved four additional shared active hours, making 40 total, retaining the
existing 24 logical attempts, 1,200 restoration seconds, source 16 GiB / 256 MiB
reserve and consumer 6 GiB / 64 MiB reserve. This includes already-performed
intake, all agents, code, focused native checks, documentation and independent
review. The controller may organize and redistribute within those same totals.
Preserve the preceding 36-hour allocation and every historical cost, failed run,
native report, completed consumer owner and remaining allowance; nothing resets.

The preceding correction grant has an estimated 1,283,924 shared milliseconds
remaining, including only 282,094 milliseconds in its source distribution.
Those estimates did not fund a new multi-hour correction. The actual new human
decision and delegated allocation were recorded through the existing CIS
outcome/allocation operations. Source receives 131,400 seconds / 22 attempts;
the completed consumer distribution remains 12,600 seconds / two attempts.
Together they remain 144,000 seconds / 24 attempts, with all protected restoration
capacity at the source and no reset of prior costs.

This stage excludes the maintained Git-to-profile route, the CISLIB coordinator
pilot and broad baseline comparison, default/CI adoption, new package, consumer
installation, full source qualification, commit, push, merge and release. Those
later review recommendations remain staged work after selection correctness is
proved. Coverage, security, native provenance and real-browser obligations from
the earlier source closeout remain open.

## Current verification

The bounded source correction is implemented. Twelve actual compiler-to-selector
cases passed, covering inheritance, interface and virtual dispatch, member
references, partial declarations, cache invalidation and independent reuse.
The executable protocol fixture validates saved-report/currentness behavior; it
does not execute the sample domain assertions. Fourteen projection and four
context cases also passed, including the retained non-adopted mutation-cache
regression. That diagnostic explicitly supplies no governed pilot readiness.

All 120 owning Graph cases passed across the full run and documented follow-ups.
The existing recovery case requires a shorter owned temporary path in this
Windows sandbox. The full run's failures and all development failures remain
retained. See [the verification record](verification.md#compiler-backed-incremental-selection-correction)
for the exact reports, review limits and outstanding source-closeout obligations.
No updated consumer installation or adoption/performance result is claimed.
