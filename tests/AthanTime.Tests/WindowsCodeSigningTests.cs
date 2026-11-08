using System.Diagnostics;

namespace AthanTime.Tests;

/// <summary>Checks signing decisions with fake certificates, signatures and tools; no real key/store/network is used.</summary>
public sealed class WindowsCodeSigningTests
{
    /// <summary>The build-time signing script requires Windows PowerShell.</summary>
    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>Rejects unavailable/untrusted inputs and preserves vendor signatures while signing unsigned binaries.</summary>
    [Theory(Skip = "Requires Windows PowerShell", SkipUnless = nameof(IsWindows))]
    [InlineData("missing", false, "trusted code-signing certificate is required")]
    [InlineData("selfsigned", false, "Self-signed certificates")]
    [InlineData("privatekey", false, "no accessible private key")]
    [InlineData("expired", false, "not currently valid")]
    [InlineData("eku", false, "does not permit code signing")]
    [InlineData("valid", true, "Verified Authenticode signatures on 2 binaries")]
    [InlineData("uninstaller", true, "Verified Authenticode signatures on 1 binaries")]
    [InlineData("signerror", false, "signing failed")]
    [InlineData("verifyerror", false, "trust verification failed")]
    [InlineData("invalidsignature", false, "signature is not valid")]
    [InlineData("invalidvendor", false, "signature is not valid")]
    public async Task SigningRequiresUsableCredentialsAndVerifiedArtifacts(string mode, bool success, string expected)
    {
        var directory = Directory.CreateTempSubdirectory("athan signing tests ").FullName;
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "RecipeFiles", "Sign-Code.ps1"), Path.Combine(directory, "Sign-Code.ps1"));
            var input = Directory.CreateDirectory(Path.Combine(directory, "payload"));
            File.WriteAllText(Path.Combine(input.FullName, "app.dll"), "Unsigned test sentinel; never executed.");
            File.WriteAllText(Path.Combine(input.FullName, "vendor.dll"), "Vendor test sentinel; never executed.");
            File.WriteAllText(Path.Combine(directory, "uninstall.tmp"), "Generated uninstaller sentinel; never executed.");
            File.WriteAllText(Path.Combine(directory, "fake-signtool.ps1"), """
                $file = [IO.Path]::GetFullPath($args[-1])
                [IO.File]::AppendAllText((Join-Path $PSScriptRoot 'calls.txt'), (($args -join '|') + [Environment]::NewLine))
                $global:LASTEXITCODE = 0
                if (($args[0] -eq 'sign' -and $global:FakeMode -eq 'signerror') -or
                    ($args[0] -eq 'verify' -and $global:FakeMode -eq 'verifyerror')) { $global:LASTEXITCODE = 7; return }
                if ($args[0] -eq 'sign' -and $global:FakeMode -ne 'invalidsignature') { [void]$global:FakeSigned.Add($file) }
                """);
            var wrapper = Path.Combine(directory, "verify.ps1");
            File.WriteAllText(wrapper, """
                param([string]$Mode)
                $ErrorActionPreference = 'Stop'
                $global:FakeMode = $Mode
                $global:FakeSigned = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
                function Get-Item {
                    param([string]$LiteralPath)
                    if (-not $LiteralPath.StartsWith('Cert:')) { throw 'Unexpected real-store/file lookup in test.' }
                    [pscustomobject]@{
                        Subject = 'CN=Test publisher'; Issuer = $(if ($global:FakeMode -eq 'selfsigned') { 'CN=Test publisher' } else { 'CN=Test CA' })
                        HasPrivateKey = $global:FakeMode -ne 'privatekey'
                        NotBefore = [DateTime]::UtcNow.AddDays(-2)
                        NotAfter = $(if ($global:FakeMode -eq 'expired') { [DateTime]::UtcNow.AddDays(-1) } else { [DateTime]::UtcNow.AddDays(2) })
                        EnhancedKeyUsageList = @([pscustomobject]@{ ObjectId = $(if ($global:FakeMode -eq 'eku') { '1.2.3' } else { '1.3.6.1.5.5.7.3.3' }) })
                    }
                }
                function Get-AuthenticodeSignature {
                    param([string]$LiteralPath)
                    $status = 'NotSigned'
                    if ([IO.Path]::GetFileName($LiteralPath) -eq 'vendor.dll') {
                        $status = if ($global:FakeMode -eq 'invalidvendor') { 'HashMismatch' } else { 'Valid' }
                    } elseif ($global:FakeSigned.Contains([IO.Path]::GetFullPath($LiteralPath))) { $status = 'Valid' }
                    [pscustomobject]@{ Status = $status }
                }
                $thumbprint = if ($Mode -eq 'missing') { '' } else { 'A' * 40 }
                $inputPath = Join-Path $PSScriptRoot $(if ($Mode -eq 'uninstaller') { 'uninstall.tmp' } else { 'payload' })
                & (Join-Path $PSScriptRoot 'Sign-Code.ps1') -Path $inputPath -CertificateThumbprint $thumbprint `
                    -CertificateStore CurrentUser -SignTool (Join-Path $PSScriptRoot 'fake-signtool.ps1') `
                    -TimestampUrl 'https://timestamp.example.test'
                """);
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", wrapper, mode })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
                var result = await output + await error;
                Assert.Equal(success, process.ExitCode == 0);
                Assert.Contains(expected, result);
            }
            finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }

            var callsPath = Path.Combine(directory, "calls.txt");
            var calls = File.Exists(callsPath) ? File.ReadAllLines(callsPath) : [];
            Assert.DoesNotContain(calls, call => call.StartsWith("sign|", StringComparison.Ordinal) && call.EndsWith("vendor.dll", StringComparison.Ordinal));
            if (success)
            {
                Assert.Single(calls, call => call.StartsWith("sign|", StringComparison.Ordinal));
                Assert.Equal(mode == "uninstaller" ? 1 : 2, calls.Count(call => call.StartsWith("verify|/pa|/all|", StringComparison.Ordinal)));
                Assert.Contains("/fd|SHA256|/sha1|" + new string('A', 40), string.Join('\n', calls));
                Assert.Contains("/tr|https://timestamp.example.test|/td|SHA256", string.Join('\n', calls));
            }
            else if (mode is "missing" or "selfsigned" or "privatekey" or "expired" or "eku") Assert.Empty(calls);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
