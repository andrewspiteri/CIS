---
name: example-replay
description: Discover, verify and diagnose the synthetic reading CLI using its native tests and an explicitly selected isolated database.
---

# Example reading replay

## Prerequisites

Use .NET 10 and the pinned manifests. Run from the example root. The automated business fixture requires Docker and supplies its own disposable database; it does not need manually shared credentials. Read the [example guide](../../../README.md).

## Discovery and verification

1. Run `dotnet run --project src/Example.Cli -- --help`, then `dotnet run --project src/Example.Cli -- apply --help`.
2. Build `NativeHarness.slnx`; execute the native unit and business projects using the guide's commands. Missing database/browser/scanner prerequisites never count as passing tests.
3. The business test demonstrates the complete unattended CLI workflow and checks versioned JSON, exact readback and duplicate behavior. Inspect its native TRX and retained workload summary.

## Explicit application use

To run separately, the operator must select an isolated example database through `EXAMPLE_DATABASE_CONNECTION`. Never write its connection value into source, logs, reports or prompts. Provide `--input <path>` with a JSON array of readings: `id` (nonempty UUID), `observedAt` (UTC timestamp or offset-free local timestamp), and `quantity` (nonnegative decimal with at most eight fractional places). The input limit is 1 MiB and 10,000 records.

Offset-free timestamps use the process's configured local timezone and the rules for that date, then convert to UTC. Daylight-saving gaps/overlaps require an explicit UTC instant (`Z` or `+00:00`); explicit nonzero offsets remain rejected. Retain explicit UTC when replaying across different machine timezones. Precision finer than a microsecond is rejected, including before conversion. This input conversion is confined to the CLI; it does not change the application's UTC storage contract.

Run `dotnet run --project src/Example.Cli -- apply --input <path>`. This creates the example table when needed and commits readings individually. Replaying unchanged identities is safe; reusing an identity with different content fails. The CLI shares application behavior with the tests; it does not bypass validation.

Success returns exit 0 and one JSON object with `schemaVersion`, `mode`, `accepted`, `stored` and `quantity`. Runtime/input/dependency failure returns 2 with a bounded stderr diagnostic; parser errors use the native parser's nonzero code. Cancellation returns 130, and already committed readings remain safe to replay. There is no live or production mode.

## Diagnosis

Preserve the first failing TRX/log and source/tool identities. Check declared input limits, database availability and identity conflicts. Use native test listeners for correlated logs, metrics and traces; do not add credentials to diagnostics. Run bounded mutation separately with the qualified runner and inspect survivors before changing assertions or policy. This skill is portable guidance, not evidence that either an agent or a test executed it.
