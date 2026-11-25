# Playwright browser smoke fixture

Use this small synthetic fixture to check that CIS launches a browser and imports
the native JSON report, including failed case identities. Each case interacts
with a real Chromium page and asserts its visible result. No application server,
external service, trace, video, or screenshot is required.

## Install and run

Run from this directory with Node.js and npm installed:

```sh
npm install
npx playwright install chromium
npm run test:browser
```

The default run passes both cases and exits with code `0`. The dependency is
pinned to Playwright `1.62.1`. The fixture imports `playwright/test`, the test
runner supplied by the `playwright` dependency of `@playwright/test`.

To produce the same native JSON format consumed by CIS, use an absolute report
path. For example, in Bash:

```sh
PLAYWRIGHT_JSON_OUTPUT_FILE="$PWD/reports/passing.json" npm run test:browser -- --reporter=line,json
```

In PowerShell:

```powershell
$env:PLAYWRIGHT_JSON_OUTPUT_FILE = Join-Path $PWD 'reports/passing.json'
npm run test:browser -- --reporter=line,json
```

The report contains two tests in the `chromium` project: `TC-SMOKE-001` and
`TC-SMOKE-002`. Each has one passing attempt. Native summary counts are
`expected: 2`, `unexpected: 0`, `flaky: 0`, and `skipped: 0`.

## Deliberate failure

Set `CIS_BROWSER_SMOKE_FAIL=1` to make `TC-SMOKE-002` assert an intentionally
incorrect saved value after completing its browser interactions. This is a
purposeful fixture mode for checking failed-run reconciliation. The ordinary
fixture run does not enable it.

In Bash:

```sh
CIS_BROWSER_SMOKE_FAIL=1 PLAYWRIGHT_JSON_OUTPUT_FILE="$PWD/reports/failing.json" npm run test:browser -- --reporter=line,json
```

In PowerShell:

```powershell
$env:CIS_BROWSER_SMOKE_FAIL = '1'
$env:PLAYWRIGHT_JSON_OUTPUT_FILE = Join-Path $PWD 'reports/failing.json'
npm run test:browser -- --reporter=line,json
Remove-Item Env:CIS_BROWSER_SMOKE_FAIL
```

This run exits with code `1`. The report must still be written, with one passing
case and one failed case. Native summary counts are `expected: 1`,
`unexpected: 1`, `flaky: 0`, and `skipped: 0`. The failed case must retain the
`TC-SMOKE-002` identity and assertion diagnostic. Use a fresh report path for each
run; an old passing report cannot establish the result of a failed launch.

## Existing local Chromium

The normal setup uses Playwright's matching downloaded browser. If the host
already provides Chromium, set `CIS_BROWSER_SMOKE_EXECUTABLE_PATH` to its absolute
executable path to use that browser for a local check. Record that override and
the browser version with the result; it does not verify the matching bundled
browser installation. The fixture does not contain a host-specific path.

## CIS integration

Point the generated CIS browser workflow at this package and let that workflow
provide the JSON reporter and destination. Run once in the normal mode and once
with `CIS_BROWSER_SMOKE_FAIL=1`. Check the current native report, process exit
code, and imported case outcomes together. These fixture runs check browser
execution and report handling. Completion-gate tests must separately check the
join between required journeys and their current native results.
