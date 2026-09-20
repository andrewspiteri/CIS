# CIS function profiler

This development tool instruments **every method with a managed body in the built CIS
assemblies** (including `cis.dll`), using [Mono.Cecil 0.11.6](https://www.nuget.org/packages/Mono.Cecil/0.11.6).
It changes disposable build copies, not product source methods or the installed extension.
Ordinary builds do not reference the probe runtime. This is CLI instrumentation; VS Code
JavaScript, .NET/framework internals, native code and external providers are outside its scope.

## Build and verify

From the repository root, on Windows with .NET 10 and Node installed:

```powershell
./tools/function-profiler/Build-Profile.ps1 -Node C:/nodejs/node.exe
```

This builds sequentially, creates `artifacts/function-profile/build/baseline` and
`instrumented`, instruments a behavioral fixture, checks exact counts and exit behavior,
and smoke-tests CIS registration. It refuses to overwrite existing output. Use a new
`-OutputRoot artifacts/function-profile/build-2` when rebuilding.

The fixture covers constructors (including throwing constructors), init setters, record
structs, ref returns, spans, generic methods, recursion, nested calls, exception filters,
finally blocks, iterators, async success/failure, lambdas and concurrent threads.

## Measure an authority

```powershell
& C:/nodejs/node.exe tools/function-profiler/measure.cjs `
  artifacts/function-profile/build C:/miscwork/bridgelink/bridgelink-docs `
  artifacts/function-profile/measurements-1
& C:/nodejs/node.exe tools/function-profiler/report.cjs artifacts/function-profile/measurements-1
```

The harness runs definition status, reference validation, graph status and Doctor,
sequentially. For each command it records ordinary/instrumented/instrumented/ordinary
runs, with a fresh CLI process each time. Existing caches remain in place. No cache is
cleared and no retention cleanup is performed. Builds and other heavy work should finish
before measurements. An optional final argument supplies a JSON array of `{name,args}`
objects for other commands; choose read-only commands when preserving canonical documents.
Commands can still update disposable CIS caches and feedback. The default authority
document check hashes every file under `docs/cis` before and after the run.

Open `report.html` for sortable timings, search and caller relationships. Each run also
has an all-method CSV (including zero-call methods), a caller/callee CSV, raw profile JSON,
stdout and stderr. `manifest.json` enumerates every included/excluded method and assembly
hashes. An absent execution means zero observed calls, not absence of instrumentation.
Methods without managed bodies (interfaces, abstract methods, externals) are listed as
not instrumentable. The profiler runtime and third-party assemblies are excluded.

For a single future CLI invocation:

```powershell
$env:CIS_PROFILE_OUTPUT = 'C:/absolute/output/definition-{pid}.json'
& ./artifacts/function-profile/build/instrumented/cis.exe definition status `
  --workspace C:/miscwork/bridgelink/bridgelink-docs --format json
Remove-Item Env:CIS_PROFILE_OUTPUT
```

Keep the matching manifest with each report. Do not install the instrumented build as
the normal CIS executable. It cannot instrument a process that was already running.

## Reading the numbers

- **Calls** counts entries; **completed** counts normal and exceptional exits.
- **Total/inclusive time** includes nested calls. It equals completed calls multiplied
  by average duration; do not multiply total by the count a second time or add parent
  and child totals together. Recursion also creates overlapping inclusive totals.
- **Self time** excludes instrumented child invocations and their measured probe
  bookkeeping. It still includes uninstrumented framework calls, disk I/O, waiting,
  scheduling and JIT effects. It is elapsed time, not a CPU sample.
- **Async and iterators** have both their entry wrapper and generated state-machine
  methods. `MoveNext` counts physical execution segments/resumptions. Its sum is not
  end-to-end async latency. Call edges follow physical thread stacks; no logical async
  ancestry is invented across awaits.
- **Overhead** changes JIT/inlining and adds probes, allocations and report output.
  `probeBookkeepingMs` accounts for probe entry/exit bookkeeping only, not every
  instrumentation effect. Use ordinary wall times for user-visible latency and
  instrumented profiles to locate costs. Timing totals across active threads overlap.
- **Failure and shutdown**: structured nonzero CLI exits still produce reports.
  Unhandled-exception snapshots include active calls, whose unfinished duration is not
  fabricated. Forced termination, a native crash or a timeout may prevent output.

The weaver adds entry probes and an outer `finally` exit probe. Original return targets,
exception regions, portable PDBs and init-only `modreq(void)` returns are preserved.
Unsupported `jmp` methods fail the build instead of silently reducing coverage.
No method arguments, source content or credentials are recorded. Assembly/type/method
names and source locations appear in the separate build manifest.
