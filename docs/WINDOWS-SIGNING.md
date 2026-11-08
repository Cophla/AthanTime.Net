# Windows signing and Smart App Control

Smart App Control is blocking unsigned executable code, including DLLs and the
uninstaller's temporary executable. A reliable signing-based fix requires
Authenticode signatures from a trusted code-signing provider. Microsoft documents
support for both RSA and ECC certificates; self-signed certificates are not an
equivalent solution. See [Microsoft's signing guidance](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control)
and [Smart App Control behavior](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview).

## Diagnosis and validation (2026-10-30)

Read-only inspection found Code Integrity events 3077/3033 for
`InstallerBuilder.dll`, `SingleInstanceProbe.dll` and the installed uninstaller's
temporary `Un.exe` on this Windows host. No code-signing certificates were found
in the current-user or local-machine personal stores; the user confirmed they
have no certificate. No security policy, trust store or installed application was
modified. The existing 1.0.1 installer remains unsigned.

The initial signing-only change added optional build-time signing support. On Windows build
10.0.26300 x64 with SDK 10.0.403, Debug and Release builds passed with zero
warnings/errors and signing unconfigured. All 14 focused signing/recipe cases
passed in each configuration. The tests use fake certificates,
signature states and tools: no real keys, certificate stores, signing providers or
network services are used. They cover invalid credentials, unsigned/vendor
binaries, temporary uninstaller filenames, sign/verify errors, stage ordering and
preserving the previous package when final signing fails. These tests do not
establish actual certificate trust or Smart App Control acceptance. A real signed
build and native install/update/uninstall checks remain blocked on obtaining a
trusted certificate and signing tools. The required-signing preflight was also
run without credentials and correctly stopped before publishing. The previous
installer's SHA-256 was unchanged and Authenticode still reports `NotSigned`.
The full application suite was not rerun at that initial stage. The subsequent
[cross-platform review](PACKAGING.md#latest-verification-2026-10-30) records that
stage's full-suite checks; historical full-suite failures remain
recorded in [packaging evidence](PACKAGING.md#installer-verification-2026-10-29).
The [latest beta rebuild](PACKAGING.md#fresh-repository-preparation-2026-11-08) supersedes the old
setup outputs with the current source, but also reports `NotSigned`; its new
checksum supersedes the historical artifact checksums.
The Windows scope selector/elevation also uses NSIS's bundled nsDialogs,
UserInfo and System plug-ins. Include their extracted DLLs in Code Integrity
review on the real signed target host; fixture tests do not establish their
acceptance by Smart App Control.

## Prerequisites

- A current provider-issued certificate with the Code Signing enhanced-key usage
  and an accessible private key through the Windows certificate store/provider.
  Use the provider's enrollment procedure; hardware-token keys can remain
  non-exportable. This workflow does not import keys or accept PFX passwords.
- Microsoft's free Windows SDK signing tools (`signtool.exe`), discoverable on
  `PATH` or through the Windows Kits registry location, or supplied explicitly.
- Optionally the provider's RFC 3161 timestamp-service URL. Timestamping can use
  network access during the build; the application itself remains offline.
  Without a timestamp, signatures are limited by certificate validity.

Certificates are an external distribution prerequisite, not a runtime package.
For qualifying public open-source projects, the
[SignPath Foundation](https://signpath.org/terms.html) offers free signing subject
to its application/approval requirements. That is a hosted workflow requiring
separate integration; it does not provide a local private key for this script.
The current local/private repository has not been published or submitted anywhere.

## Configure an approved signing environment

After obtaining the certificate, configure the shell that builds the application
or launches the IDE:

```powershell
$env:ATHAN_SIGNING_CERTIFICATE_THUMBPRINT = 'YOUR_CERTIFICATE_THUMBPRINT'
$env:ATHAN_REQUIRE_SIGNING = '1'
# Optional: ATHAN_SIGNING_STORE = CurrentUser (default) or LocalMachine.
# Optional: ATHAN_SIGNTOOL = full path to the Windows SDK signtool.exe.
# Optional: ATHAN_TIMESTAMP_URL = the RFC 3161 URL supplied by your provider.
./packaging/windows/Sign-Code.ps1 -CheckOnly
dotnet build AthanTime.Net.slnx -c Release
./Create-Installers.cmd
```

`-CheckOnly` validates configuration and certificate availability; it does not
prove that a signing operation or trust verification will succeed. Missing,
expired, self-signed, wrong-purpose or inaccessible-key certificates stop signing.
`ATHAN_REQUIRE_SIGNING=1` also stops builds when no thumbprint is configured.
Keep private keys/passwords outside the repository; PFX/P12 files are ignored.

`Directory.Build.targets` signs Windows build-output EXE/DLL files when the
certificate environment variable is set. This includes the app, build helpers
and test helpers before they run. The IDE must inherit that environment. Building
a helper for `--dry-run` also signs its build output when signing is configured;
the dry run still does not generate an installer. Linux/macOS RID outputs and
builds on those OS hosts do not invoke Windows Authenticode signing.

The Windows recipe signs the complete publish payload, preserves already-valid
vendor signatures, and verifies every selected signature with SignTool. Invalid
existing signatures stop the build. The
[NSIS uninstaller-finalization hook](https://nsis.sourceforge.io/Docs/Chapter5.html#uninstfinalize)
signs/verifies the generated uninstaller before embedding it; the outer setup is
then signed/verified. Signing only the outer setup does not cover its uninstaller.
The signed package is staged privately and replaces the public output only after
successful final verification. A failed signed build leaves the previous package
unchanged; there is no automatic fallback to a new unsigned package.

The portable Windows ZIP path also preflights signing and signs/verifies the
final publish tree before archiving. Windows signing requests for a Windows ZIP
require a Windows host; a non-Windows target is not signed merely because the
Windows signing environment variables are present. Failed signing leaves an
existing archive untouched. The archive container itself has no Authenticode
signature; its executable contents carry their individual signatures.

On a host where the .NET helper cannot yet run, the existing direct recipe can
use an already-verified NSIS compiler after signing prerequisites are available:

```powershell
./packaging/windows/build-installer.ps1 -NsisCompiler ./artifacts/tools/nsis-3.13/Bin/makensis.exe
```

For an existing output directory, `Sign-Code.ps1 -Path <directory>` signs/verifies
its executable files and DLLs; it leaves source files and NuGet caches untouched.
After signing, use a disposable Smart App Control-enabled Windows host to verify
launch, install/update and uninstall, including the temporary uninstaller, then
review Code Integrity events. A valid signature is a prerequisite, not a blanket
guarantee against malware detection or other organizational policies.
