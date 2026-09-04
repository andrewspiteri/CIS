param(
    [string]$Output = "artifacts/release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$extensionRoot = Join-Path $repoRoot "vscode-extension"
$package = Get-Content (Join-Path $extensionRoot "package.json") -Raw | ConvertFrom-Json
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repoRoot $Output))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
if (-not $resolvedOutput.Equals($allowedRoot, $comparison) -and
    -not $resolvedOutput.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, $comparison)) {
    throw "VSIX output must remain beneath $allowedRoot"
}
if ($package.version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$' -or
    $package.name -notmatch '^[a-z0-9][a-z0-9-]{0,99}$' -or
    $package.publisher -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$') {
    throw "VSIX identity contains an unsafe name, publisher, or version."
}

$stage = Join-Path $resolvedOutput ".vsix-stage"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage "extension") | Out-Null

$excluded = @("test", "node_modules", ".vscode", "*.vsix")
Get-ChildItem -LiteralPath $extensionRoot -Force | Where-Object {
    $name = $_.Name
    -not ($excluded | Where-Object { $name -like $_ })
} | Copy-Item -Destination (Join-Path $stage "extension") -Recurse -Force

$identityName = [Security.SecurityElement]::Escape([string]$package.name)
$identityVersion = [Security.SecurityElement]::Escape([string]$package.version)
$identityPublisher = [Security.SecurityElement]::Escape([string]$package.publisher)
$displayName = [Security.SecurityElement]::Escape([string]$package.displayName)
$description = [Security.SecurityElement]::Escape([string]$package.description)
$tags = [Security.SecurityElement]::Escape([string]::Join(',', $package.keywords))
$engine = [Security.SecurityElement]::Escape([string]$package.engines.vscode)
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011">
  <Metadata>
    <Identity Language="en-US" Id="$identityName" Version="$identityVersion" Publisher="$identityPublisher" />
    <DisplayName>$displayName</DisplayName>
    <Description xml:space="preserve">$description</Description>
    <Tags>$tags</Tags>
    <Categories>Other</Categories>
    <GalleryFlags>Public</GalleryFlags>
    <Properties>
      <Property Id="Microsoft.VisualStudio.Code.Engine" Value="$engine" />
      <Property Id="Microsoft.VisualStudio.Services.Links.Source" Value="https://github.com/andrewspiteri/CIS" />
    </Properties>
    <Icon>extension/media/cis.svg</Icon>
    <License>extension/LICENSE.md</License>
  </Metadata>
  <Installation><InstallationTarget Id="Microsoft.VisualStudio.Code" /></Installation>
  <Dependencies />
  <Assets><Asset Type="Microsoft.VisualStudio.Code.Manifest" Path="extension/package.json" Addressable="true" /></Assets>
</PackageManifest>
"@
$contentTypes = @"
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="json" ContentType="application/json" />
  <Default Extension="js" ContentType="application/javascript" />
  <Default Extension="md" ContentType="text/markdown" />
  <Default Extension="svg" ContentType="image/svg+xml" />
  <Default Extension="txt" ContentType="text/plain" />
  <Override PartName="/extension.vsixmanifest" ContentType="text/xml" />
</Types>
"@
[IO.File]::WriteAllText((Join-Path $stage "extension.vsixmanifest"), $manifest, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $stage "[Content_Types].xml"), $contentTypes, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE.md") -Destination (Join-Path (Join-Path $stage "extension") "LICENSE.md")

$vsix = Join-Path $resolvedOutput "$($package.name)-$($package.version).vsix"
if (Test-Path -LiteralPath $vsix) { Remove-Item -LiteralPath $vsix -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $vsix, [IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item -LiteralPath $stage -Recurse -Force
Write-Host "VSIX=$vsix"
