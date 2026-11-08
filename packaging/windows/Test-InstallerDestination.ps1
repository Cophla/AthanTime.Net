<#
.SYNOPSIS
Reads real installer release captions/destination controls in a fresh Windows Sandbox; never clicks Install.
.DESCRIPTION
Use a tiny compile fixture or the final package. Checks missing destination folders,
native /D= overrides and registered update paths. Machine cases require an elevated
Sandbox token. Registry/profile changes are confined to this disposable guest.
#>
param(
    [Parameter(Mandatory=$true)][string]$Installer,
    [string]$ResultFile = 'C:\Smoke\destination-result.json',
    [switch]$ExpectRegression
)

if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'Run only in a fresh Windows Sandbox.' }
$ErrorActionPreference = 'Stop'
$release = [regex]::Match([IO.Path]::GetFileName($Installer), '^AthanTime-(?<version>[0-9]+(?:\.[0-9]+){1,3}(?:-[A-Za-z0-9.-]+)?)-win-x64-setup\.exe$')
if (-not $release.Success) { throw 'Use the versioned filename produced by InstallerBuilder.' }
$expectedVersion = $release.Groups['version'].Value
if (-not ('DestinationControls' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class DestinationControls {
    private delegate bool WindowCallback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, WindowCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr window);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr count, StringBuilder text, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll", EntryPoint="GetWindowLongW")] public static extern int GetStyle(IntPtr window, int index);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr first, IntPtr second);
    public static IntPtr Root(int process) {
        IntPtr found=IntPtr.Zero;
        EnumWindows((window,state) => { uint owner; GetWindowThreadProcessId(window,out owner);
            if (owner==process && GetDlgItem(window,1)!=IntPtr.Zero) { found=window; return false; } return true; },IntPtr.Zero);
        return found;
    }
    public static IntPtr Edit(IntPtr root) {
        if (root==IntPtr.Zero) return IntPtr.Zero;
        IntPtr found=IntPtr.Zero;
        EnumChildWindows(root,(window,state) => { if(GetDlgCtrlID(window)==1019) { found=window; return false; } return true; },IntPtr.Zero);
        return found;
    }
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr window);
    // GetWindowText cannot read another process's edit control; WM_GETTEXT can.
    public static string Text(IntPtr window) {
        var text=new StringBuilder(2048); IntPtr result;
        if(SendMessageTimeout(window,0xD,(IntPtr)text.Capacity,text,2,2000,out result)==IntPtr.Zero)
            throw new InvalidOperationException("Installer edit control did not answer WM_GETTEXT.");
        return text.ToString();
    }
}
'@
}
$admin = ([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$results = [Collections.Generic.List[object]]::new()
$guestRoot = Join-Path $env:TEMP ('athan-destination-' + [Guid]::NewGuid().ToString('N'))
$keyPath = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\AthanTime'

function Probe([string]$Name, [string]$Arguments, [string]$Expected, [bool]$ReadOnly = $false) {
    if (-not $ReadOnly -and (Test-Path -LiteralPath $Expected)) { throw "Fresh-path case must use an absent folder: $Name" }
    $process = Start-Process -FilePath $Installer -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    $root = [IntPtr]::Zero
    try {
        $timeout = [Diagnostics.Stopwatch]::StartNew()
        $edit = [IntPtr]::Zero
        while (-not $process.HasExited -and $timeout.Elapsed.TotalSeconds -lt 15) {
            $root = [DestinationControls]::Root($process.Id)
            $edit = [DestinationControls]::Edit($root)
            if ($edit -ne [IntPtr]::Zero) { break }
            Start-Sleep -Milliseconds 100
        }
        if ($edit -eq [IntPtr]::Zero) { throw "Destination page missing: $Name" }
        # Allow normal dialog initialization/validation messages to finish before reading.
        Start-Sleep -Milliseconds 300
        $text = [DestinationControls]::Text($edit)
        $caption = [DestinationControls]::Text($root)
        $browse = [DestinationControls]::GetDlgItem([DestinationControls]::GetParent($edit), 1001)
        $actualReadOnly = ([DestinationControls]::GetStyle($edit, -16) -band 0x800) -ne 0
        $browseEnabled = [DestinationControls]::IsWindowEnabled($browse)
        $installEnabled = [DestinationControls]::IsWindowEnabled([DestinationControls]::GetDlgItem($root, 1))
        $passed = $text.TrimEnd('\') -eq $Expected.TrimEnd('\') -and $actualReadOnly -eq $ReadOnly -and $browseEnabled -ne $ReadOnly -and $installEnabled -and $caption.Contains($expectedVersion)
        $results.Add([pscustomobject]@{ Name=$Name; Passed=$passed; Expected=$Expected; Actual=$text; Caption=$caption; ReadOnly=$actualReadOnly; BrowseEnabled=$browseEnabled; InstallEnabled=$installEnabled })
    } finally {
        if (-not $process.HasExited) {
            [void][DestinationControls]::PostMessage($root, 0x111, [IntPtr]2, [IntPtr]::Zero)
            if (-not $process.WaitForExit(3000)) { $process.Kill(); $process.WaitForExit() }
        }
        $process.Dispose()
    }
}

try {
    Probe 'Only me default, folder absent' '/CurrentUser' (Join-Path $env:LOCALAPPDATA 'Programs\Athan Time')
    Probe 'Only me /D override, folder absent' "/CurrentUser /D=$guestRoot\Custom destination with spaces" "$guestRoot\Custom destination with spaces"
    if ($admin) {
        Probe 'All users default, folder absent' '/AllUsers' (Join-Path $env:ProgramW6432 'Athan Time')
        Probe 'All users /D override, folder absent' "/AllUsers /D=$guestRoot\Shared destination with spaces" "$guestRoot\Shared destination with spaces"
    }
    # Simulate only the guest's current-user registration; never touch a host profile.
    $registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry32)
    try {
        $existing = $registry.OpenSubKey($keyPath)
        if ($existing) { $existing.Dispose(); throw 'Use a fresh Sandbox without an existing Athan Time registration.' }
        $updatePath = Join-Path $guestRoot 'Registered update folder'
        New-Item -ItemType Directory -Path $updatePath -Force | Out-Null
        $key = $registry.CreateSubKey($keyPath)
        try { $key.SetValue('InstallLocation', $updatePath); $key.SetValue('UninstallString', ('"' + $updatePath + '\Uninstall.exe"')) } finally { $key.Dispose() }
        try { Probe 'Registered update path' "/CurrentUser /D=$guestRoot\Ignored override" $updatePath $true }
        finally { $registry.DeleteSubKeyTree($keyPath) }
    } finally { $registry.Dispose() }
    $failures = @($results | Where-Object { -not $_.Passed })
    $passed = if ($ExpectRegression) { $failures.Count -gt 0 } else { $failures.Count -eq 0 }
    @{ Passed=$passed; ExpectedRegression=[bool]$ExpectRegression; Administrator=$admin; MachineCasesSkipped=(-not $admin); Checks=@($results.ToArray()); OS=[Environment]::OSVersion.VersionString } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ResultFile -Encoding UTF8
    if (-not $passed) { throw 'Destination probe did not match its expected outcome; see the result JSON.' }
} catch {
    @{ Passed=$false; Error=$_.Exception.Message; Checks=@($results.ToArray()) } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ResultFile -Encoding UTF8
    throw
}
