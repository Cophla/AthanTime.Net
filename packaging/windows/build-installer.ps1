param([string]$Version = "", [string]$NsisCompiler = "")

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = [string]([xml](Get-Content -LiteralPath (Join-Path $repository "Directory.Build.props") -Raw)).Project.PropertyGroup.Version
}
$publishParent = [IO.Path]::GetFullPath((Join-Path $repository "artifacts/publish"))
$publishDirectory = [IO.Path]::GetFullPath((Join-Path $publishParent ("setup-" + [Guid]::NewGuid().ToString("N"))))
if (-not $publishDirectory.StartsWith($publishParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Invalid packaging staging path."
}
$outputDirectory = Join-Path $repository "artifacts/packages"
$uninstallInclude = Join-Path $publishParent ("uninstall-" + [Guid]::NewGuid().ToString("N") + ".nsh")
$signScript = Join-Path $PSScriptRoot 'Sign-Code.ps1'
$signing = -not [string]::IsNullOrWhiteSpace($env:ATHAN_SIGNING_CERTIFICATE_THUMBPRINT)
if ($signing -or $env:ATHAN_REQUIRE_SIGNING -eq '1') { & $signScript -CheckOnly }
$signedOutput = [IO.Path]::GetFullPath((Join-Path $publishParent ("signed-" + [Guid]::NewGuid().ToString("N"))))
if (-not $signedOutput.StartsWith($publishParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Invalid signed-package staging path.'
}

if ([string]::IsNullOrWhiteSpace($NsisCompiler)) {
    # Direct recipe calls share the same discovery/bootstrap path as the root launcher.
    dotnet run --project (Join-Path $repository "tools/InstallerBuilder/InstallerBuilder.csproj") `
        -c Release -- --version $Version
    if ($LASTEXITCODE -ne 0) { throw "InstallerBuilder failed with exit code $LASTEXITCODE." }
    return
}
if (-not (Test-Path -LiteralPath $NsisCompiler -PathType Leaf)) { throw "NSIS compiler was not found: $NsisCompiler" }

try {
dotnet publish (Join-Path $repository "AthanTime.Net/AthanTime.Net.csproj") `
    -c Release -r win-x64 --self-contained true -p:Version=$Version `
    -p:PublishSingleFile=false -p:DebugType=None -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
if ($signing) { & $signScript -Path $publishDirectory }

# Compile an exact payload list into the uninstaller; preserve user-added files.
$instructions = [Collections.Generic.List[string]]::new()
$instructions.Add('!macro RemovePublishedFiles')
foreach ($file in Get-ChildItem -LiteralPath $publishDirectory -Recurse -File) {
    $relative = $file.FullName.Substring($publishDirectory.Length + 1).Replace('$', '$$')
    $instructions.Add('  Delete "$INSTDIR\' + $relative + '"')
}
foreach ($directory in (Get-ChildItem -LiteralPath $publishDirectory -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending)) {
    $relative = $directory.FullName.Substring($publishDirectory.Length + 1).Replace('$', '$$')
    $instructions.Add('  RMDir "$INSTDIR\' + $relative + '"')
}
$instructions.Add('!macroend')
[IO.File]::WriteAllLines($uninstallInclude, $instructions, [Text.UTF8Encoding]::new($false))

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$compilerOutput = $outputDirectory
if ($signing) {
    New-Item -ItemType Directory -Force -Path $signedOutput | Out-Null
    $compilerOutput = $signedOutput
}
$compilerArguments = @("/DAPP_VERSION=$Version", "/DPUBLISH_DIR=$publishDirectory",
    "/DOUTPUT_DIR=$compilerOutput", "/DUNINSTALL_INCLUDE=$uninstallInclude")
if ($signing) { $compilerArguments += "/DSIGN_SCRIPT=$signScript" }
& $NsisCompiler @compilerArguments (Join-Path $PSScriptRoot "AthanTime.nsi")
if ($LASTEXITCODE -ne 0) { throw "NSIS failed with exit code $LASTEXITCODE." }
if ($signing) {
    $package = Join-Path $signedOutput "AthanTime-$Version-win-x64-setup.exe"
    & $signScript -Path $package
    Move-Item -LiteralPath $package -Destination (Join-Path $outputDirectory "AthanTime-$Version-win-x64-setup.exe") -Force
}
} finally {
    if (Test-Path -LiteralPath $publishDirectory) {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }
    if (Test-Path -LiteralPath $uninstallInclude) { Remove-Item -LiteralPath $uninstallInclude -Force }
    if (Test-Path -LiteralPath $signedOutput) { Remove-Item -LiteralPath $signedOutput -Recurse -Force }
}
