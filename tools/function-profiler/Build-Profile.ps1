param(
    [string]$OutputRoot = 'artifacts/function-profile/build',
    [string]$Node = 'node'
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$destination = [IO.Path]::GetFullPath((Join-Path $repository $OutputRoot))
if (Test-Path -LiteralPath $destination) { throw "Output already exists: $destination. Choose a new OutputRoot." }
Push-Location $repository
try {
    # Sequential: these projects share the probe runtime output.
    dotnet build src/Cis.Host/Cis.Host.csproj -c Release -p:CisFunctionProfiling=true --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Host build failed.' }
    dotnet build tools/function-profiler/Weaver/Weaver.csproj -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Weaver build failed.' }
    dotnet build tools/function-profiler/Fixture/Fixture.csproj -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed.' }
    $baseline = New-Item -ItemType Directory -Path (Join-Path $destination 'baseline')
    $instrumented = New-Item -ItemType Directory -Path (Join-Path $destination 'instrumented')
    $fixture = New-Item -ItemType Directory -Path (Join-Path $destination 'fixture')
    foreach ($entry in Get-ChildItem -LiteralPath 'src/Cis.Host/bin/Release/net10.0') {
        Copy-Item -LiteralPath $entry.FullName -Destination $baseline.FullName -Recurse
        Copy-Item -LiteralPath $entry.FullName -Destination $instrumented.FullName -Recurse
    }
    foreach ($entry in Get-ChildItem -LiteralPath 'tools/function-profiler/Fixture/bin/Release/net10.0') {
        Copy-Item -LiteralPath $entry.FullName -Destination $fixture.FullName -Recurse
    }
    $weaver = 'tools/function-profiler/Weaver/bin/Release/net10.0/Cis.FunctionProfiler.Weaver.dll'
    dotnet $weaver $fixture.FullName
    if ($LASTEXITCODE -ne 0) { throw 'Fixture instrumentation failed.' }
    & $Node tools/function-profiler/verify-fixture.cjs $fixture.FullName
    if ($LASTEXITCODE -ne 0) { throw 'Instrumentation behavior verification failed.' }
    dotnet $weaver $instrumented.FullName
    if ($LASTEXITCODE -ne 0) { throw 'CIS instrumentation failed.' }
    & (Join-Path $instrumented.FullName 'cis.exe') --help | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Instrumented host smoke test failed.' }
    Write-Output "Profile build ready: $destination"
} finally { Pop-Location }
