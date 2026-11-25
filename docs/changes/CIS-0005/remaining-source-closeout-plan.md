---
title: "CIS-0005 remaining source closeout"
type: implementation-plan
status: Proposed
change_id: CIS-0005
---

# Finish the measured source closeout gaps

The final `94a78c097aa0` candidate passed all 1,324 actual native tests, with zero
skips. Its native merged Cobertura report measures **1,822 of 1,987 changed
executable C# lines covered: 91.6960 percent**. All changed files were instrumented.
The adopted 95 percent threshold needs at least 1,888 covered lines, so at least
66 of the 165 currently uncovered lines need meaningful execution. The exact
file/line inventory remains with the native closeout evidence.

The largest coherent test targets are existing context delivery and output
boundaries: 27 uncovered lines in `ContextPackService.TaskContext.cs`, 26 in
`ContextPackBudget.cs`, and 15 in `FeedbackModule.cs`. Exercise invalid advisory
roots, missing current context, participant-context disclosure, Unicode/UTF-8
truncation and detailed versus routine usage output through their existing public
entry points. Add the cases to the owning Context and Feedback test classes;
use the complete inventory to confirm other material gaps. These counts identify
missing checks, not permission to write assertions merely to increase coverage.

The exact inventory is
`.artifacts/outcome-workflow-correction/CIS-0005/WORK-007/source-closeout/native-gates-94a78c097aa0/coverage-gap-inventory.json`.

The existing unit, integration and compatibility selections passed 12, four and
three actual cases respectively. Their separate executions do not increase the
central run's unique-case count. The typed regression manifest has 1,344 case
records because the existing adapter expands multiple declared TC identifiers;
it does not represent 1,344 native executions.

Formal task closure also needs successful native implementation/adoption and
independent review evidence for each of the seven existing tasks, plus actual
current security reports from all three configured scanners. The genuine
WORK-006 read-only Codex pilot timed out at 180 seconds; it is retained as a
failure, not implementation evidence. The remaining ordinary time in the current
allocation cannot cover another measured native run and those obligations.

## Proposed bounded continuation

Request **four additional shared active hours**, making **36 total**, without
changing the existing 24 logical attempts, 1,200 restoration seconds, source
16 GiB / 256 MiB reserve, or consumer 6 GiB / 64 MiB reserve. Preserve all prior
costs, unknown historical effort, native failures, reports, approvals and open
attempt history. The controller may redistribute within the same containing
allowance through the normal CIS allocation operation.

| Work | Bounded result | Planning ceiling |
| --- | --- | ---: |
| Cover the measured gaps | Add coherent cases to existing owning test classes; fix production behavior only if a test exposes a real defect. Preserve the full changed-production scope and 95 percent minimum. Include scoped formatting/builds and the necessary current graph refresh. | 30 minutes |
| Requalify the candidate | Run the existing native collector workflow and reconcile its actual TRX and merged coverage report. Retain all failed and older evidence with its original identity. | 50 minutes |
| Native provenance | Perform seven genuine read-only implementation/adoption inspections and seven distinct independent read-only reviews through Codex. Use up to 480 seconds each; preserve the timed-out pilot and continue its logical investigation where valid. State historical bootstrap authorship accurately. | 112 minutes |
| Security | Admit and execute the three existing pinned scanners sequentially, using named read-only containers, declared writable results/cache, sampled bounds and exact drainage. Recount at phase boundaries; remove only newly owned images where preflight established ownership. Preserve previous reports. | 15 minutes |
| Checked closure | Resolve required findings within the remaining shared allowance, produce current integrity-bound receipts, complete through normal CIS transitions and update the source verification report. | 30 minutes |

These ceilings total 237 minutes; the remaining three minutes are ordinary
coordination margin. They are one shared envelope for all agents and commands,
not separate grants. Actual results may still require a hold; no pass or closure
is promised in advance.

The default graph validator reports a fresh structure without errors, with three
warnings for approved plan-document lifecycle values; strict validation exits 5.
Retain that qualification for assigned review. Do not remove actual human approval
statuses to hide warnings.

No new package, DI framework, Configuration module, test harness, generic tooling
campaign, external deployment, commit, push, merge or release is included. The
newly attached browser-evidence fix remains queued for review and integration
after the current source work reaches a checked handoff; it is not silently
included in this resource request.
