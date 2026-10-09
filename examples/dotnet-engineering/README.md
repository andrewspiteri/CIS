# Native .NET engineering example

This independent synthetic example shows cohesive responsibilities and native tooling. `Example.Core` owns reading validation and the batch use case; `Example.Persistence` implements transactional PostgreSQL idempotency; `Example.Cli` composes them through explicit constructor dependencies. No shared framework or DI container is required. No external service accounts or operational activation are involved.

The independent solution can be copied outside CIS. It requires .NET 10 SDK, PowerShell 7 (`pwsh`) for the diagnostic process checks, and a local Docker engine for business tests. Package/tool versions are pinned in the example manifests. Run from this directory:

```powershell
dotnet build NativeHarness.slnx --disable-build-servers -m:1
dotnet test tests/unit/Example.UnitTests.csproj --no-build --logger trx --results-directory .cis/local/results/unit --collect "XPlat Code Coverage"
dotnet test tests/business/Example.BusinessTests.csproj --no-build --logger trx --results-directory .cis/local/results/business
dotnet format NativeHarness.slnx --verify-no-changes --no-restore
dotnet tool restore
dotnet stryker --config-file stryker-config.json --output .cis/local/mutation/run-1 --skip-version-check
dotnet list NativeHarness.slnx package --vulnerable --include-transitive
```

Use a new result directory for each retained qualification attempt. Coverage is the actual collector-produced `coverage.cobertura.xml` beneath the unit result directory, not an invented fixed path. Native TRX files preserve discovered test identities. SDK/Sonar diagnostics and NuGet vulnerability warnings fail the build. The patched SSH.NET reference addresses Testcontainers' transitive dependency; do not suppress the audit instead.

Stryker uses the unit-only solution and its MTP runner for xUnit v3; normal TRX/Coverlet execution continues through VSTest. MTP in the selected Stryker version is preview and needs explicit qualification. Earlier VSTest mutation attempts incorrectly left every mutant surviving; their reports are retained locally. The MTP attempt exposed three missing assertions about useful validation errors, which were corrected. Do not infer compatibility merely because native test execution succeeds. See the [Stryker runner documentation](https://stryker-mutator.io/blog/stryker-net-mtp-runner/) and [xUnit platform documentation](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform).

`TestSignals` enables native metrics/tracing listeners and Debug logging only in test composition. Each fixture uses its own source identity. Business/authentication behavior is unchanged. Tests inspect cancellation evidence and dispose listeners. Bounded signal summaries go through xUnit output into native TRX, including on assertion failure. Capture records disabled collectors and truncation explicitly; known credential fields are redacted before retention. The bounded journal under `.cis/local/diagnostics/` flushes sanitized log, metric and trace events while the test runs. A collector-open record without collector-closed is partial evidence, never a passing test result. Native xUnit remains responsible for results. The intentional crash qualification retained all preceding signals and returned a failed native run; this does not promise general secret detection or durability through power loss.

The business tests start disposable PostgreSQL 16.8 containers and clean them up through xUnit's async fixture lifecycle. Each declared workload is 500 readings within 60 seconds. One case covers application-service/connection restart, duplicate/out-of-order replay, identity-conflict rollback, exact complete readback and CLI parity. A separate case starts a real CLI process to persist 250 readings, waits for it to exit, then starts a new process to replay all 500 identities with duplicates and reversed order. It verifies that exactly the remaining 250 are accepted and reads every persisted identity and value back through an independent connection. Its timer includes both process lifetimes and final readback; Docker startup, input preparation and final report writing are excluded. This is orderly process restart with the database retained, not forced termination or database crash recovery.

UTC event times have microsecond precision and quantities fit PostgreSQL `numeric(28,8)` without rounding. Unsupported precision/range is rejected before persistence. These workloads are representative of this synthetic fixture only; they do not establish a future product's workload or a compatible performance baseline. Each business attempt retains its input and diagnostic summary beneath `.cis/local/business/<fixture-id>/`; native TRX reports the directory. Earlier attempts are preserved.

For CLI input, a timestamp without an offset is local time in the process's configured operating-system timezone. The CLI uses that timezone's rules for the supplied date and converts the instant to UTC before validation and persistence. `Z` and `+00:00` remain UTC; explicit nonzero offsets remain rejected by the existing UTC-only contract. Local times in a daylight-saving gap or overlap are rejected because they do not identify one instant; supply explicit UTC for those values. Conversion outside the supported UTC range and sub-microsecond precision are also rejected. For replay across machines with different timezones, retain the explicit UTC instant; the same offset-free text can identify different instants on those machines. This policy applies to the CLI file boundary; application and HTTP contracts still require UTC.

The CLI requires every input reading to supply `id`, `observedAt` and `quantity`; omitted constructor fields are rejected instead of silently receiving zero/default values. Native CLI negative cases first prove the database and a valid input work, then omit each field with a fresh identity and verify failure with unchanged persistence.

For agent use, follow the canonical [replay skill](.github/skills/example-replay/SKILL.md). The CLI's only external dependency is an explicitly selected isolated PostgreSQL database. Never point this example at an existing application database. Full scanner composition, operational failure scenarios and actual independent-provider qualification remain separate release requirements.

## API and browser layers

The optional `WebHarness.slnx` uses real loopback Kestrel and disposable PostgreSQL. Native ASP.NET Core JWT authentication checks issuer, audience, expiry, signing algorithm and read/write permissions. Its key and short-lived identities are generated inside the fixture; this is a local qualification host, not a production identity-provider recipe. The public page serves static content; data endpoints require authorization.

```powershell
dotnet build WebHarness.slnx --disable-build-servers -m:1
$toolDirectory = Join-Path (Get-Location) '.cis/local/tools/oasdiff-1.32.1'
New-Item -ItemType Directory -Force -Path $toolDirectory | Out-Null
$archive = Join-Path $toolDirectory 'oasdiff.tar.gz'
Invoke-WebRequest 'https://github.com/oasdiff/oasdiff/releases/download/v1.32.1/oasdiff_1.32.1_windows_amd64.tar.gz' -OutFile $archive
if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne '4D0758B32D454E6011E59DB93884AF1CA27AE2B212D990F36738EA5EFB5D7F28') { throw 'oasdiff archive integrity mismatch' }
tar -xzf $archive -C $toolDirectory
$env:EXAMPLE_OASDIFF_PATH = Join-Path $toolDirectory 'oasdiff.exe'
& $env:EXAMPLE_OASDIFF_PATH --version
$env:PLAYWRIGHT_BROWSERS_PATH = Join-Path (Get-Location) '.cis/local/browsers'
& tests/web/bin/Debug/net10.0/playwright.ps1 install chromium --only-shell
dotnet test tests/web/Example.WebTests.csproj --no-build --logger trx --results-directory .cis/local/results/web
dotnet format WebHarness.slnx --verify-no-changes --no-restore
dotnet list WebHarness.slnx package --vulnerable --include-transitive
```

Layer traits separate `integration`, `regression`, `api-compatibility`, `security` and `browser` (`--filter Layer=browser`, for example). Bind each applicable filtered command to its own CIS suite and native report directory. A single browser-labelled run does not establish that every API/security obligation was assessed.

Tests verify precise persisted readback, replay/conflict behavior, anonymous/expired/insufficient-permission rejection, native OpenAPI operations/error contracts and the browser's save/reload/denied journey. Browser locators check accessible roles, labels and live status; this is limited accessibility evidence, not a full accessibility audit. Native OpenAPI, Playwright traces and screenshots are retained beneath a unique `.cis/local/web/<fixture-id>/` directory, including when assertions fail. The native TRX identifies that directory; retries retain earlier attempts. Traces contain disposable synthetic authentication headers: keep them local and never substitute a real identity or database. Missing Docker/browser prerequisites fail the native tests.

The setup above qualifies Windows x64 using the official [oasdiff 1.32.1 release](https://github.com/oasdiff/oasdiff/releases/tag/v1.32.1). Native compatibility tests compare the generated contract with `tests/web/contracts/reading-v1.json` and prove that removing POST is rejected. CIS's own API comparison reports partial coverage for this contract's format/pattern semantics; do not relabel that partial result as a pass. Other operating systems require their matching release asset and checksum.

The pinned Microsoft.OpenApi 2.7.5 dependency addresses the [maintainer's parsing advisory](https://github.com/microsoft/OpenAPI.NET/security/advisories/GHSA-v5pm-xwqc-g5wc). Do not suppress NuGet audit to retain a vulnerable transitive version.

## Performance and native coverage selection

The independent BenchmarkDotNet 0.15.8 project measures validation of 500 readings with tracing disabled and enabled. It uses a native short job, memory diagnostics and the full JSON exporter:

```powershell
dotnet run --project tests/performance/Example.Performance.csproj -c Release -- --filter '*' --artifacts .cis/local/performance/attempt-1
```

This component benchmark does not replace the PostgreSQL business workload. A short local run can be noisy; inspect sample count and standard error before adopting a baseline. Keep the baseline report immutable and review the workload, resource conditions, budgets and allowed regression separately. CIS's completion gate reads `.cis/performance-policy.json` and native reports; it never creates or relaxes a baseline automatically. See the CIS completion-context manual for the policy contract.

For Coverlet's native collector, a CIS suite may select `.cis/local/results/<run>/unit/*/coverage.cobertura.xml`. The single-directory selector avoids VSTest's additional nested deployment copy. Exactly one report must match; missing or multiple reports fail reconciliation. A `**` descendant selector is also supported where the selected native layout needs it. Preserve separate directories for attempts and target frameworks. The original Cobertura file is hashed and retained unchanged. Adopt `.cis/coverage-scope.json` with a real base commit and reviewed production paths before claiming changed-production coverage.

## Native quality and security checks

The separate [synthetic migration fixture](tests/migration/README.md) maps a simulated legacy check catalogue to native xUnit cases, demonstrates a missed boundary defect, and compares a concentrated implementation with an explicit interval responsibility. It includes an over-fragmentation counterexample. Run it through `MigrationHarness.slnx`; it introduces no custom test runner.

SDK code-metrics analyzers supplement Sonar: CA1502 measures method complexity and CA1506 measures aggregate type coupling, including partial declarations. `CodeMetricsConfig.txt` declares the example's thresholds (20 method complexity, 40 type/method coupling). These are steering defaults, not universal proof of readability. A native negative fixture with 25 references in each of two partial declarations correctly reports one type coupled to 50 types. Splitting files does not evade the check. Architecture tests and source review remain required. See Microsoft's [CA1506 documentation](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1506).

The browser journey has one named CA1506 exception because a single readable end-to-end test necessarily combines native hosting, locators, assertions and evidence APIs. It does not suppress production coupling checks. Authentication composition and disposable test identity ownership are separate classes.

The qualified secret scanner is [Gitleaks 8.30.1](https://github.com/gitleaks/gitleaks/releases/tag/v8.30.1), Windows x64 archive SHA-256 `d29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e`. Download the matching official release archive and verify its checksum before extraction into a local tools directory. Run `gitleaks dir <clean-source-export> --redact --report-format json --report-path <unique-local-report> --exit-code 1`. A clean export excludes generated outputs and retained credentials from disposable test traces; separately review Git history before publication. A seeded random nonfunctional token was detected and its removal restored a clean result. Known demonstration strings can be allowlisted by native scanner rules and are not suitable negative fixtures.

Secret scanning, NuGet audit, analyzers and the denied API/browser scenarios cover different risks. Their combination does not claim penetration testing, a full accessibility audit or qualification of every supported scanner. Bind relevant native security reports through CIS security reconciliation; its source identity and artifact hashes are accepted by the completion gate. Keep scanner prerequisites and unqualified layers explicit.
