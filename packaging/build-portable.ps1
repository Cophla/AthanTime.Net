param(
    [ValidateSet("win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")]
    [string]$Runtime,
    [string]$Version
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
# Delegate omitted values to the builder's shared version and current-host RID.
$arguments = @('run', '--project', (Join-Path $repository 'tools/InstallerBuilder'), '-c', 'Release', '--', '--format', 'portable')
if (-not [string]::IsNullOrWhiteSpace($Runtime)) { $arguments += @('--rid', $Runtime) }
if (-not [string]::IsNullOrWhiteSpace($Version)) { $arguments += @('--version', $Version) }
dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Packaging failed with exit code $LASTEXITCODE." }
