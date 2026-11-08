<#
.SYNOPSIS
Checks Windows installation/update/removal only inside Windows Sandbox.
.DESCRIPTION
Uses a disposable profile. Missing previous setup simulates legacy registration;
this does not certify an actual old-package upgrade or interactive UAC behavior.
The current release is read from the versioned InstallerBuilder filename; pass
-Installer explicitly or map exactly one current setup into C:\Smoke.
.PARAMETER TestAllUsers
Adds machine-scope/coexistence checks from an elevated Sandbox PowerShell.
UAC approval, cancellation and alternate credentials need separate manual checks.
#>
param(
    [string]$PreviousInstaller = 'C:\Smoke\previous-1.0.0.exe',
    [string]$Installer = '',
    [string]$DefaultSettings = 'C:\Smoke\default-settings.json',
    [string]$ResultDirectory = 'C:\Smoke',
    [switch]$TestAllUsers,
    [switch]$CloseSandbox
)

# Native integration procedure, deliberately excluded from unit tests and the host profile.
if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'Run this procedure only inside Windows Sandbox.' }
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $Installer) {
    $candidates = @(Get-ChildItem -LiteralPath 'C:\Smoke' -Filter 'AthanTime-*-win-x64-setup.exe' -File)
    if ($candidates.Count -ne 1) { throw 'Supply -Installer or map exactly one current setup into C:\Smoke.' }
    $Installer = $candidates[0].FullName
}
if ([IO.Path]::GetFileName($Installer) -notmatch '^AthanTime-(?<release>[0-9]+(?:\.[0-9]+){1,3}(?:-[A-Za-z0-9.-]+)?)-win-x64-setup\.exe$') {
    throw 'Use the versioned filename produced by InstallerBuilder.'
}
$expectedVersion = $Matches.release
if ($TestAllUsers -and -not ([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'All-users smoke checks require an elevated PowerShell in Windows Sandbox. Test interactive UAC approval/cancellation separately.'
}
$installation = Join-Path $env:USERPROFILE 'Athan Time upgrade smoke'
$profile = Join-Path $env:APPDATA 'AthanTime'
$uninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\AthanTime'
$runKey = 'Software\Microsoft\Windows\CurrentVersion\Run'
$checks = [Collections.Generic.List[string]]::new()
$application = $null
$registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
    [Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry32)
$machineRegistry = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
    [Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)

function Check([bool]$Condition, [string]$Description) {
    if (-not $Condition) { throw $Description }
    $checks.Add($Description)
    $checks | Set-Content -LiteralPath (Join-Path $ResultDirectory 'progress.txt') -Encoding UTF8
}

function RegistryValue([string]$Path, [string]$Name) {
    $key = $registry.OpenSubKey($Path)
    try { if ($null -ne $key) { $key.GetValue($Name) } } finally { if ($key) { $key.Dispose() } }
}

function MachineValue([string]$Name) {
    $key = $machineRegistry.OpenSubKey($uninstallKey)
    try { if ($null -ne $key) { $key.GetValue($Name) } } finally { if ($key) { $key.Dispose() } }
}

function RunSetup([string]$Path, [string]$Arguments = '/S', [int]$ExpectedExit = 0) {
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(120000)) { $process.Kill(); throw "Setup timed out: $Path" }
        if ($process.ExitCode -ne $ExpectedExit) { throw "Setup returned $($process.ExitCode), expected $ExpectedExit : $Path" }
    } finally { $process.Dispose() }
}

function StartApp {
    $process = Start-Process -FilePath (Join-Path $installation 'AthanTime.Net.exe') `
        -ArgumentList '--start-minimized' -WindowStyle Hidden -PassThru
    if (-not $process.WaitForInputIdle(20000) -or $process.HasExited) { throw 'Installed app did not initialize.' }
    return $process
}

try {
    if ((Test-Path -LiteralPath $installation) -or (Test-Path -LiteralPath $profile)) {
        throw 'Use a fresh Windows Sandbox session.'
    }
    if (Test-Path -LiteralPath $PreviousInstaller -PathType Leaf) {
        RunSetup $PreviousInstaller "/S /D=$installation"
    } else {
        # The old distributable may have been removed. Simulate its registry shape
        # without claiming an actual old-package upgrade was exercised.
        RunSetup $Installer "/S /CurrentUser /D=$installation"
        $key = $registry.OpenSubKey($uninstallKey, $true)
        try { $key.DeleteValue('InstallLocation', $false); $key.SetValue('DisplayVersion', '1.0.0') } finally { $key.Dispose() }
        Remove-Item -LiteralPath (Join-Path $installation 'athan-install.ini')
        $checks.Add('Legacy registration simulated; no previous installer was supplied')
    }
    Check ((RegistryValue $uninstallKey 'DisplayVersion') -eq '1.0.0') 'Legacy 1.0.0 registration prepared in a folder with spaces'
    Check (Test-Path -LiteralPath (Join-Path $installation 'AthanTime.Net.exe')) 'Original application executable exists'

    New-Item -ItemType Directory -Path $profile | Out-Null
    $settings = Get-Content -LiteralPath $DefaultSettings -Raw | ConvertFrom-Json
    $settings.audioVolume = 37
    $settings.loggingEnabled = $false
    $settings.notificationsEnabled = $false
    $settings.prayerAudioEnabled = $false
    $settings.startMinimized = $true
    $settings.startOnLogin = $true
    $settingsPath = Join-Path $profile 'settings.json'
    $settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
    $settingsHash = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash
    $recording = Join-Path $installation 'User recording.ogg'
    Set-Content -LiteralPath $recording -Value 'User-owned test recording sentinel' -Encoding UTF8
    $recordingHash = (Get-FileHash -LiteralPath $recording -Algorithm SHA256).Hash
    $startup = '"' + (Join-Path $installation 'AthanTime.Net.exe') + '" --start-minimized'
    $key = $registry.CreateSubKey($runKey)
    try { $key.SetValue('AthanTime', $startup) } finally { $key.Dispose() }

    RunSetup $Installer "/S /D=$(Join-Path $env:USERPROFILE 'Ignored update folder')"
    Check ((RegistryValue $uninstallKey 'DisplayVersion') -eq $expectedVersion) "Existing installation upgraded to $expectedVersion"
    Check ((RegistryValue $uninstallKey 'DisplayName') -eq "Athan Time $expectedVersion") 'Updated registration displays the full release label'
    Check ((RegistryValue $uninstallKey 'InstallLocation') -eq $installation) 'Upgrade recovered the original custom folder'
    Check (-not (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'Programs\Athan Time'))) 'Upgrade did not create a second installation'
    Check (((Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash) -eq $settingsHash) 'Upgrade preserved settings byte for byte'
    Check (((Get-FileHash -LiteralPath $recording -Algorithm SHA256).Hash) -eq $recordingHash) 'Upgrade preserved user recordings'
    Check ((RegistryValue $runKey 'AthanTime') -eq $startup) 'Upgrade preserved login startup registration'

    RunSetup $Installer
    Check ((RegistryValue $uninstallKey 'InstallLocation') -eq $installation) 'Same-version reinstall reused the installation'
    Check (((Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash) -eq $settingsHash) 'Same-version reinstall preserved settings'

    $executable = Join-Path $installation 'AthanTime.Net.exe'
    $binaryHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
    $locked = [IO.File]::Open($executable, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try { RunSetup $Installer '/S' 2 } finally { $locked.Dispose() }
    Check (((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash) -eq $binaryHash) 'Locked-file update failed before changing the executable'

    $application = StartApp
    RunSetup $Installer '/S' 2
    Check (-not $application.HasExited) 'Running application blocked update without being terminated'
    $duplicate = Start-Process -FilePath $executable -ArgumentList '--start-minimized' -WindowStyle Hidden -PassThru
    try {
        Check ($duplicate.WaitForExit(15000) -and $duplicate.ExitCode -eq 0) 'Duplicate launch handed activation to the installed application'
        $until = [DateTime]::UtcNow.AddSeconds(15)
        do { $application.Refresh(); if ($application.MainWindowHandle -ne [IntPtr]::Zero) { break }; Start-Sleep -Milliseconds 100 }
        while ([DateTime]::UtcNow -lt $until)
        Check ($application.MainWindowHandle -ne [IntPtr]::Zero) 'Duplicate launch restored the existing native window'
    } finally { if (-not $duplicate.HasExited) { $duplicate.Kill() }; $duplicate.Dispose() }
    $application.Kill()
    $application.WaitForExit()
    $application.Dispose()
    $application = StartApp
    Check (-not $application.HasExited) 'Installed application restarted after forced termination'
    $application.Kill()
    $application.WaitForExit()
    $application.Dispose()
    $application = $null

    RunSetup (Join-Path $installation 'Uninstall.exe') "/S _?=$installation"
    Check (-not (Test-Path -LiteralPath $executable)) 'Uninstall removed the bundled executable'
    Check (Test-Path -LiteralPath $settingsPath) 'Uninstall preserved the user profile'
    Check (((Get-FileHash -LiteralPath $recording -Algorithm SHA256).Hash) -eq $recordingHash) 'Uninstall preserved the user-added recording'
    Check ($null -eq (RegistryValue $runKey 'AthanTime')) 'Uninstall removed its matching login startup entry'
    Check ($null -eq (RegistryValue $uninstallKey 'DisplayVersion')) 'Uninstall removed its registration'

    $freshInstallation = Join-Path $env:USERPROFILE 'Athan Time fresh smoke'
    RunSetup $Installer "/S /D=$freshInstallation"
    Check ((RegistryValue $uninstallKey 'InstallLocation') -eq $freshInstallation) 'Fresh install used the requested folder'
    Check ((RegistryValue $uninstallKey 'DisplayVersion') -eq $expectedVersion) "Fresh $expectedVersion install registered its version"
    # A leftover uninstaller must not remove a newer installation's shared registry/shortcut.
    Copy-Item -LiteralPath (Join-Path $freshInstallation 'Uninstall.exe') -Destination (Join-Path $installation 'Uninstall.exe') -Force
    RunSetup (Join-Path $installation 'Uninstall.exe') "/S _?=$installation"
    Check ((RegistryValue $uninstallKey 'InstallLocation') -eq $freshInstallation) 'Stale uninstaller preserved the active installation registration'
    $shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Athan Time\Athan Time.lnk'
    Check (Test-Path -LiteralPath $shortcut) 'Stale uninstaller preserved the active installation shortcut'
    $otherStartup = '"C:\Another location\AthanTime.Net.exe" --start-minimized'
    $key = $registry.CreateSubKey($runKey)
    try { $key.SetValue('AthanTime', $otherStartup) } finally { $key.Dispose() }
    RunSetup (Join-Path $freshInstallation 'Uninstall.exe') "/S _?=$freshInstallation"
    Check ((RegistryValue $runKey 'AthanTime') -eq $otherStartup) 'Uninstall preserved a startup entry for a different installation'
    Check (Test-Path -LiteralPath $settingsPath) 'Fresh-install uninstall preserved the existing profile'

    if ($TestAllUsers) {
        $machineSettingsHash = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash
        $machineInstallation = Join-Path $env:ProgramW6432 'Athan Time'
        $userInstallation = Join-Path $env:USERPROFILE 'Athan Time alongside shared smoke'
        if ((Test-Path -LiteralPath $machineInstallation) -or (MachineValue 'UninstallString')) { throw 'All-users checks require a fresh Sandbox machine scope.' }
        RunSetup $Installer '/S /AllUsers'
        Check ((MachineValue 'InstallLocation') -eq $machineInstallation) 'All-users install used 64-bit Program Files and HKLM registration'
        Check ((MachineValue 'DisplayVersion') -eq $expectedVersion) 'Machine registration retains the full release version'
        Check ((MachineValue 'DisplayName') -eq "Athan Time $expectedVersion") 'Machine registration displays the full release label'
        Check ((RegistryValue $uninstallKey 'DisplayVersion') -eq $null) 'All-users install did not create HKCU uninstall registration'
        $commonShortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Athan Time\Athan Time.lnk'
        Check (Test-Path -LiteralPath $commonShortcut) 'All-users install created the shared Start Menu shortcut'
        Check ((Get-Content -LiteralPath (Join-Path $machineInstallation 'athan-install.ini') -Raw) -match 'Scope=AllUsers') 'Machine directory retained its uninstall scope'
        RunSetup $Installer "/S /CurrentUser /D=$machineInstallation" 2
        Check ((MachineValue 'InstallLocation') -eq $machineInstallation) 'Cross-scope directory reuse was rejected without replacing machine registration'
        $machineRecording = Join-Path $machineInstallation 'User recording.ogg'
        Set-Content -LiteralPath $machineRecording -Value 'Machine-scope user-owned recording sentinel'
        RunSetup $Installer "/S /AllUsers /D=$(Join-Path $env:USERPROFILE 'Ignored machine update folder')"
        Check ((MachineValue 'InstallLocation') -eq $machineInstallation) 'Machine update retained its registered folder'
        RunSetup $Installer "/S /CurrentUser /D=$userInstallation"
        Check ((RegistryValue $uninstallKey 'InstallLocation') -eq $userInstallation) 'A separate user copy retained its own registration'
        $userShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Athan Time\Athan Time.lnk'
        $userStartup = '"' + (Join-Path $userInstallation 'AthanTime.Net.exe') + '" --start-minimized'
        $key = $registry.CreateSubKey($runKey)
        try { $key.SetValue('AthanTime', $userStartup) } finally { $key.Dispose() }
        RunSetup (Join-Path $machineInstallation 'Uninstall.exe') "/S _?=$machineInstallation"
        Check ($null -eq (MachineValue 'UninstallString')) 'Machine uninstall removed only its HKLM registration'
        Check (-not (Test-Path -LiteralPath $commonShortcut)) 'Machine uninstall removed its shared shortcut'
        Check (Test-Path -LiteralPath $userShortcut) 'Machine uninstall preserved the separate user shortcut'
        Check ((RegistryValue $uninstallKey 'InstallLocation') -eq $userInstallation) 'Machine uninstall preserved user installation registration'
        Check ((RegistryValue $runKey 'AthanTime') -eq $userStartup) 'Machine uninstall preserved per-user login startup'
        Check (Test-Path -LiteralPath $machineRecording) 'Machine uninstall preserved unrecognized recordings'
        Check (((Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash) -eq $machineSettingsHash) 'Machine operations preserved the existing settings byte for byte'
        RunSetup (Join-Path $userInstallation 'Uninstall.exe') "/S _?=$userInstallation"
        Check ($null -eq (RegistryValue $runKey 'AthanTime')) 'User uninstall still removed its own matching startup entry'
    }
    @{ Passed = $true; Checks = @($checks.ToArray()); OS = [Environment]::OSVersion.VersionString } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ResultDirectory 'result.json') -Encoding UTF8
} catch {
    @{ Passed = $false; Checks = @($checks.ToArray()); Error = $_.Exception.Message } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ResultDirectory 'result.json') -Encoding UTF8
    throw
} finally {
    if ($application) { if (-not $application.HasExited) { $application.Kill() }; $application.Dispose() }
    $registry.Dispose()
    $machineRegistry.Dispose()
    if ($CloseSandbox) {
        Start-Process -FilePath "$env:WINDIR\System32\shutdown.exe" -ArgumentList '/s /t 0' -WindowStyle Hidden
    }
}
