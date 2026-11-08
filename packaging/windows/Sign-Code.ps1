param(
    [string[]]$Path = @(),
    [string]$CertificateThumbprint = $env:ATHAN_SIGNING_CERTIFICATE_THUMBPRINT,
    [ValidateSet('CurrentUser', 'LocalMachine')][string]$CertificateStore = $(if ($env:ATHAN_SIGNING_STORE) { $env:ATHAN_SIGNING_STORE } else { 'CurrentUser' }),
    [string]$SignTool = $env:ATHAN_SIGNTOOL,
    [string]$TimestampUrl = $env:ATHAN_TIMESTAMP_URL,
    [switch]$CheckOnly
)

# Build-time Authenticode signing only; never import certificates or change Windows policy.
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw 'A trusted code-signing certificate is required. Set ATHAN_SIGNING_CERTIFICATE_THUMBPRINT to its certificate-store thumbprint. Self-signed certificates do not resolve Smart App Control.'
}
$CertificateThumbprint = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
if ($CertificateThumbprint -notmatch '\A[0-9A-F]{40}\z') { throw 'The certificate thumbprint must contain 40 hexadecimal characters.' }
$certificate = Get-Item -LiteralPath "Cert:\$CertificateStore\My\$CertificateThumbprint"
if (-not $certificate.HasPrivateKey) { throw 'The selected certificate has no accessible private key.' }
$now = [DateTime]::UtcNow
if ($now -lt $certificate.NotBefore.ToUniversalTime() -or $now -gt $certificate.NotAfter.ToUniversalTime()) {
    throw 'The selected code-signing certificate is not currently valid.'
}
if ($certificate.Subject.Equals($certificate.Issuer, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Self-signed certificates do not resolve Smart App Control. Use a certificate issued by a trusted code-signing provider.'
}
if (-not ($certificate.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' })) {
    throw 'The selected certificate does not permit code signing.'
}

if ([string]::IsNullOrWhiteSpace($SignTool)) {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { $SignTool = $command.Source }
    else {
        $kits = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' -Name KitsRoot10 -ErrorAction SilentlyContinue
        if ($kits) {
            $SignTool = Get-ChildItem -LiteralPath (Join-Path $kits.KitsRoot10 'bin') -Directory |
                Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
                Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
        }
    }
}
if ([string]::IsNullOrWhiteSpace($SignTool) -or -not (Test-Path -LiteralPath $SignTool -PathType Leaf)) {
    throw 'SignTool was not found. Install the free Windows SDK signing tools or set ATHAN_SIGNTOOL to signtool.exe.'
}
$SignTool = [IO.Path]::GetFullPath($SignTool)
if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
    $uri = $null
    if (-not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -notin @('http', 'https')) {
        throw 'TimestampUrl must be an absolute HTTP(S) RFC 3161 timestamp-service URL.'
    }
}
if ($CheckOnly) { Write-Host 'Code-signing certificate and tool configuration are available; signing and trust verification are still required.'; return }
if (-not $Path.Count) { throw 'Specify a binary file or output directory to sign.' }

$files = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $Path) {
    if (Test-Path -LiteralPath $entry -PathType Container) {
        foreach ($file in Get-ChildItem -LiteralPath $entry -Recurse -File) {
            if ($file.Extension.Equals('.exe', [StringComparison]::OrdinalIgnoreCase) -or
                $file.Extension.Equals('.dll', [StringComparison]::OrdinalIgnoreCase)) {
                [void]$files.Add($file.FullName)
            }
        }
    } elseif (Test-Path -LiteralPath $entry -PathType Leaf) {
        # NSIS may give its generated uninstaller a temporary filename rather than .exe.
        [void]$files.Add([IO.Path]::GetFullPath($entry))
    } else { throw "Signing input does not exist: $entry" }
}
if (-not $files.Count) { throw 'No executable files or DLLs were found to sign.' }

foreach ($file in $files) {
    $signature = Get-AuthenticodeSignature -LiteralPath $file
    if ($signature.Status -eq 'NotSigned') {
        $signArguments = @('sign', '/fd', 'SHA256', '/sha1', $CertificateThumbprint, '/s', 'My')
        if ($CertificateStore -eq 'LocalMachine') { $signArguments += '/sm' }
        if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) { $signArguments += @('/tr', $TimestampUrl, '/td', 'SHA256') }
        & $SignTool @signArguments $file
        if ($LASTEXITCODE -ne 0) { throw "SignTool signing failed for $file (exit $LASTEXITCODE)." }
        $signature = Get-AuthenticodeSignature -LiteralPath $file
    }
    # Preserve valid vendor signatures; never hide invalid/untrusted signatures by replacing them.
    if ($signature.Status -ne 'Valid') { throw "Authenticode signature is not valid for $file : $($signature.Status)" }
    & $SignTool verify /pa /all $file
    if ($LASTEXITCODE -ne 0) { throw "SignTool trust verification failed for $file (exit $LASTEXITCODE)." }
}
Write-Host "Verified Authenticode signatures on $($files.Count) binaries. Smart App Control acceptance still needs target-OS testing."
