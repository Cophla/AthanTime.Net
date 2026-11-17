<#
.SYNOPSIS
Checks All users uninstall, graceful tray exit, settings removal and completion in Windows Sandbox only.
.DESCRIPTION
Uses a fresh disposable profile and the actual NSIS controls. It does not verify alternate
administrator credentials, signing or other real user sessions. Never run on the host.
#>
param(
    [string]$Installer = '',
    [string]$DefaultSettings = 'C:\Smoke\default-settings.json',
    [string]$ResultDirectory = 'C:\Smoke',
    [switch]$CloseSandbox
)

if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'Run this procedure only inside Windows Sandbox.' }
$ErrorActionPreference = 'Stop'
$checks = [Collections.Generic.List[string]]::new()
$app = $null
$status = 'Failed'
$failure = ''
$nativeMessages = @()

function Check([bool]$condition, [string]$description) {
    if (-not $condition) { throw $description }
    $checks.Add($description)
    $checks | Set-Content -LiteralPath (Join-Path $ResultDirectory 'uninstall-progress.txt') -Encoding UTF8
}

try {
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    Check (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) 'Procedure must start as an unelevated desktop user; setup requests UAC for installation'
    if (-not $Installer) {
        $setups = @(Get-ChildItem -LiteralPath 'C:\Smoke' -Filter 'AthanTime-*-Windows-x64-Setup.exe' -File)
        Check ($setups.Count -eq 1) 'Exactly one validation setup supplied'
        $Installer = $setups[0].FullName
    }
    $folder = Join-Path $env:ProgramFiles 'Athan Time uninstall smoke'
    $profile = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'AthanTime'
    Check (-not (Test-Path -LiteralPath $profile)) 'Fresh Sandbox profile has no prior Athan Time settings'
    $setup = Start-Process -FilePath $Installer -ArgumentList "/S /AllUsers /D=$folder" -WindowStyle Hidden -PassThru
    try {
        Check ($setup.WaitForExit(120000)) 'Silent All users install finished'
        Check ($setup.ExitCode -eq 0) 'All users install succeeded'
    } finally { $setup.Dispose() }
    $executable = Join-Path $folder 'AthanTime.Net.exe'
    Check (Test-Path -LiteralPath $executable) 'Installed application exists'
    New-Item -ItemType Directory -Force -Path (Join-Path $profile 'Logs') | Out-Null
    $settings = Get-Content -LiteralPath $DefaultSettings -Raw | ConvertFrom-Json
    $settings.notificationsEnabled = $false
    $settings.prayerAudioEnabled = $false
    $settings.startMinimized = $true
    $settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $profile 'settings.json') -Encoding UTF8
    Copy-Item -LiteralPath (Join-Path $profile 'settings.json') -Destination (Join-Path $profile 'settings.json.bak')
    Set-Content -LiteralPath (Join-Path $profile 'Logs\athan-smoke.log') -Value 'owned app log'
    Set-Content -LiteralPath (Join-Path $profile 'recording.ogg') -Value 'User file fixture, never played'
    Set-Content -LiteralPath (Join-Path $profile 'Logs\other.log') -Value 'Unrelated file fixture'
    $app = Start-Process -FilePath $executable -ArgumentList '--start-minimized' -WindowStyle Hidden -PassThru
    Check ($app.WaitForInputIdle(20000) -and -not $app.HasExited) 'Installed app initialized in minimized/tray mode'

    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class UninstallControls {
    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr parent, int identifier);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr value, IntPtr data);
    public static IntPtr[] Windows(IntPtr parent) {
        var result = new List<IntPtr>();
        EnumWindow callback = (window, parameter) => { result.Add(window); return true; };
        if (parent == IntPtr.Zero) EnumWindows(callback, IntPtr.Zero); else EnumChildWindows(parent, callback, IntPtr.Zero);
        return result.ToArray();
    }
    public static string Text(IntPtr window) { var text = new StringBuilder(1024); GetWindowText(window, text, text.Capacity); return text.ToString(); }
}
'@
    $uninstaller = Start-Process -FilePath $Installer -ArgumentList '/Uninstall /AllUsers' -WindowStyle Hidden -PassThru
    try {
        Check ($app.WaitForExit(40000)) 'Uninstaller startup gracefully closed the running app without confirmation or force-kill'
        $until = [DateTime]::UtcNow.AddSeconds(30)
        $window = [IntPtr]::Zero
        do {
            $window = [UninstallControls]::Windows([IntPtr]::Zero) | Where-Object { [UninstallControls]::Text($_) -like 'Athan Time * Uninstall*' } | Select-Object -First 1
            if ($window) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $until)
        Check ([bool]$window) 'Native uninstall confirmation page appeared'
        [void][UninstallControls]::SendMessage([UninstallControls]::GetDlgItem($window, 1), 0xF5, [IntPtr]::Zero, [IntPtr]::Zero)
        $until = [DateTime]::UtcNow.AddSeconds(15)
        $checkbox = [IntPtr]::Zero
        do {
            $checkbox = [UninstallControls]::Windows($window) | Where-Object { [UninstallControls]::Text($_) -like 'Remove my settings*' } | Select-Object -First 1
            if ($checkbox) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $until)
        Check ([bool]$checkbox) 'Native optional settings checkbox appeared'
        Check ([UninstallControls]::SendMessage($checkbox, 0xF0, [IntPtr]::Zero, [IntPtr]::Zero).ToInt64() -eq 0) 'Settings removal is unchecked by default'
        [void][UninstallControls]::SendMessage($checkbox, 0xF1, [IntPtr]1, [IntPtr]::Zero)
        [void][UninstallControls]::SendMessage([UninstallControls]::GetDlgItem($window, 1), 0xF5, [IntPtr]::Zero, [IntPtr]::Zero)
        $until = [DateTime]::UtcNow.AddSeconds(60)
        while ((Test-Path -LiteralPath $executable) -and [DateTime]::UtcNow -lt $until) { Start-Sleep -Milliseconds 200 }
        Check (-not (Test-Path -LiteralPath $executable)) 'Checked All users uninstall removed the application payload'
        Check (-not (Test-Path -LiteralPath (Join-Path $profile 'settings.json'))) 'Desktop-user settings removed'
        Check (-not (Test-Path -LiteralPath (Join-Path $profile 'settings.json.bak'))) 'Desktop-user backup removed'
        Check (-not (Test-Path -LiteralPath (Join-Path $profile 'Logs\athan-smoke.log'))) 'App-owned log removed'
        Check (Test-Path -LiteralPath (Join-Path $profile 'recording.ogg')) 'Custom recording preserved'
        Check (Test-Path -LiteralPath (Join-Path $profile 'Logs\other.log')) 'Unrelated log preserved'
        $until = [DateTime]::UtcNow.AddSeconds(30)
        $successWindow = [IntPtr]::Zero
        do {
            $successWindow = [UninstallControls]::Windows([IntPtr]::Zero) | Where-Object {
                @([UninstallControls]::Windows($_) | Where-Object {
                    [UninstallControls]::Text($_) -eq 'Athan Time was uninstalled successfully.'
                }).Count -gt 0
            } | Select-Object -First 1
            if ($successWindow) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $until)
        Check ([bool]$successWindow) 'Successful removal message appeared'
        [void][UninstallControls]::SendMessage([UninstallControls]::GetDlgItem($successWindow, 1), 0xF5, [IntPtr]::Zero, [IntPtr]::Zero)
        Check ($uninstaller.WaitForExit(30000) -and $uninstaller.ExitCode -eq 0) 'Successful removal returned zero after acknowledgement'
        $status = 'Passed'
    } finally { $uninstaller.Dispose() }
} catch {
    $failure = $_.Exception.Message
    if ('UninstallControls' -as [type]) {
        $nativeMessages = @([UninstallControls]::Windows([IntPtr]::Zero) |
            Where-Object { [UninstallControls]::Text($_) -like 'Athan Time * Uninstall*' } |
            ForEach-Object { [UninstallControls]::Windows($_) } |
            ForEach-Object { [UninstallControls]::Text($_) } | Where-Object { $_ })
    }
}
finally {
    if ($app) { $app.Dispose() }
    [ordered]@{ Status=$status; Date=[DateTime]::UtcNow.ToString('o'); OS=[Environment]::OSVersion.VersionString; Checks=@($checks); Failure=$failure; NativeMessages=$nativeMessages;
        Limits='Disposable Sandbox only; alternate administrator credentials, other real users, signing and macOS/Linux are not certified.' } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ResultDirectory 'uninstall-result.json') -Encoding UTF8
    if ($CloseSandbox) { shutdown.exe /s /t 0 }
}
