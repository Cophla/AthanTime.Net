# Starting a new GitHub repository

The beta.6 source snapshot is prepared for a **new, empty repository**. Use the
reviewed clean source ZIP or fresh local repository supplied with the handoff.
It includes current source, tests, installers/build recipes, documentation,
compressed catalogs, media and required third-party notices. Source ZIPs omit
Git metadata, local profiles, signing material, logs and generated outputs.
[AGENTS.md](../AGENTS.md) remains the canonical engineering guide; publication
checks are in the [release checklist](RELEASE-CHECKLIST.md#before-github-publication).

## Keep old history separate

The original local repository is preserved. **Do not copy its .git folder or
push its old branch into the empty remote.** That history retains the oversized
raw catalog and earlier commit email found in the November 8 audit. A new empty
remote does not remove history from a repository you push into it. Start from
the clean snapshot or supplied fresh local repository instead.

The catalog source is now AthanTime.Net/Data/locations.db.zip (about 32.8 MiB),
alongside locations.json.br for regeneration/parity tests. MSBuild's shared
LocationCatalog.targets extracts prebuilt SQLite incrementally into
configuration/RID-local obj and copies it as Data/locations.db to build and
publish outputs. No runtime catalog import/extraction, Git LFS, external unzip
program or catalog download is required. Unchanged builds reuse the extraction;
changed/missing/invalid archives have regression coverage. The raw builder
output stays ignored and out of the new repository.

GitHub's regular-Git limit is 100 MiB per file; its browser upload limit is
25 MiB. Use Git/desktop Git tooling for this source tree, rather than browser
file uploads. See [GitHub's file-size guidance](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github).

## Initialize an extracted source ZIP

Run from the extracted project folder. If using the supplied fresh Git repository,
its initial commit and chosen public project identity are already configured;
inspect them rather than creating another initial commit.

~~~text
git init -b main
git config --local user.name "YOUR_PUBLIC_PROJECT_NAME"
git config --local user.email "YOUR_PUBLIC_PROJECT_EMAIL"
git add .
git diff --cached --stat
git diff --cached --check
git update-index --chmod=+x Create-Installers.command Create-Installers.sh packaging/linux/build-deb.sh packaging/macos/build-app.sh
git commit -m "Initial beta release of offline cross-platform Athan Time"
~~~

The executable-bit command preserves macOS/Linux launchers when importing loose
files on Windows; reviewed Git archives also retain those modes. Commit
author/committer identity is public metadata. Choose an intentional public
project email or your actual GitHub-provided no-reply address; do not assume Git
will anonymize your global identity. The handoff's selected email is kept in
local Git configuration, not embedded in application settings or documentation.
Required public copyright/provenance credits remain intact.

Verify the new identity/history and file inventory before adding the empty remote
and pushing main. Do not mirror-push local internal/agent refs.
No remote or push is performed by the preparation workflow. Source and native
installer archives have different purposes; keep generated installers out of Git.

## Build and verify

Install the .NET 10 SDK; normal dependency restore may need package access once.
From a clean checkout, run the Debug/Release commands in
[verification](../AGENTS.md#verification). The catalog is already prebuilt in the
versioned ZIP; no GeoNames regeneration is needed for normal builds. Offline
regeneration is:
~~~text
dotnet run --project tools/LocationDataBuilder -c Release -- --database-only
~~~
It produces raw SQLite and its versioned ZIP from the archived JSON.

The Windows/macOS/Linux matrix remains win-x64, linux-x64, linux-arm64,
osx-x64, osx-arm64. Shared MSBuild extraction and .NET ZIP creation add no new
native dependency. Native installers still require matching hosts. Native
desktop/signing/resource validation remains separate from source readiness;
see [dated packaging evidence](PACKAGING.md#fresh-repository-preparation-2026-11-08).

The November 8 clean-repository builds passed all 425 tests in each configuration
with zero warnings/errors. The fresh single-commit source history has 135 files,
no oversized blobs or detected checked secret/home-path patterns. Shared data
archive and payload hashes, five packaging plans and executable launcher modes
were reviewed. Native platform/signing limits remain in the packaging evidence.

## Privacy boundary

The original audit inspected 132 working files, 282 reachable historical blobs
and five commits, plus public catalog and audio metadata. It detected no checked
private-key/provider-secret/credential/home-path patterns; source contacts used
the placeholder support address. Those checks did identify the earlier public
commit email and oversized historical catalog. The clean snapshot excludes that
old history, and the final handoff records its own file/metadata/size checks.

Environment files, copied root profiles, log/settings exports, private signing/SSH
keys, IDE outputs and local agent caches remain ignored. Explicit environment
examples stay visible for review. Public city coordinates, dated calculation
references and required upstream credits are retained; no saved user profile is
shipped. Pattern/manual inspection is not an exhaustive guarantee against every
possible secret or personal identifier. Review future staged changes before
publication. Raw local audit/build evidence under artifacts is not source.
