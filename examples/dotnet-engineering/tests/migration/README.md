# Synthetic migration and responsibility review

This newly authored fixture represents a project with a hand-maintained console-check catalogue. It is not a historical application or another test runner. The old check identities and assertions are preserved in `legacy-checks.txt`; xUnit owns discovery, assertions and TRX results for characterization and replacement.

Run from the example root:

```powershell
dotnet test MigrationHarness.slnx --logger trx --results-directory .cis/local/results/migration/attempt-1
dotnet format MigrationHarness.slnx --verify-no-changes --no-restore
```

Six named native cases map the old checks and demonstrate behavior parity. They also show why parity alone is insufficient: the simulated legacy checks omitted a shared interval boundary. The additional boundary case proves the old implementation assigns twice and the extracted interval policy assigns once. Changing the replacement's exclusive upper bound to inclusive makes that native case fail.

The responsibility improvement is concrete: `Window` owns membership, while `WindowRouting` receives the intervals it routes over. A reader can find and replace either concern directly. The caller test covers changed interval data. No container, interface per method or common runtime library is needed. The two small types stay together because their relationship is clearer in one file.

An over-fragmented alternative would add separate lower-bound, upper-bound, comparison, counting and factory interfaces for this one expression. More files would obscure the policy and its caller. Neither file counts nor passing analyzer thresholds establish useful responsibilities; review the code and caller.

The retained legacy implementation is test input only. Production migration should remove the obsolete runner once native discovery, case mapping, diagnostics and CI invocation are verified. Use real project requirements to add missing assertions; do not treat this eight-case fixture as broad application assurance.
