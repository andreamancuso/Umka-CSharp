# Publishing UmkaSharp

UmkaSharp targets .NET 9 and publishes one managed package with RID-specific native assets.

## Package Identity

The intended NuGet package ID is `UmkaSharp`.

## Practical Local Check

For ordinary release preparation, this is enough:

```powershell
dotnet --version
.\native\build_windows_msvc.ps1 -Configuration Release
dotnet test UmkaSharp.sln --configuration Release --verbosity minimal
.\scripts\pack-local.ps1 -Version 0.1.0-local
```

CI runs `.\scripts\verify-samples.ps1` on Windows and Linux after building and testing the solution. Run it locally when sample projects changed. Run `.\scripts\verify-docs.ps1` when README, docs, or sample documentation changed. Run `.\scripts\verify-package-local.ps1` when packaging or native asset layout changed. Run `.\scripts\verify-linux-docker.ps1` when you have changed native build code or want to refresh and test the local `linux-x64` asset; the Docker script verifies third-party notices against the selected Umka checkout, packs a Linux-local package, runs the package-consumer sample from that package, and checks its expected output lines.

The package should contain:

```text
lib/net9.0/UmkaSharp.dll
runtimes/win-x64/native/umka_shim.dll
runtimes/linux-x64/native/libumka_shim.so
README.md
LICENSE
THIRD-PARTY-NOTICES.md
```

Use `.\scripts\verify-package-layout.ps1 -PackagePath .\artifacts\packages\UmkaSharp.0.1.0-local.nupkg -ExpectedVersion 0.1.0-local -RequireSymbols` to check the package file layout and nuspec metadata directly, including that build-only packages such as Source Link do not leak into runtime dependencies, that repository metadata includes a concrete git commit for traceability, that the symbol PDB contains Source Link data for that commit, that unverified runtime RID assets are not present, that required package entries and native assets are not empty, that packaged README, license, and third-party notice text matches the repository files, that the README avoids NuGet-hostile repository-relative documentation links, that representative public runtime/function/callback/value/host-handle APIs are present in the packaged XML documentation without unresolved `<inheritdoc />` tags, and that the `.nupkg` / `.snupkg` do not contain unexpected generated files.
`.\scripts\pack-local.ps1` uses portable path handling, verifies that `THIRD-PARTY-NOTICES.md` includes the full Umka license from `UMKA_ROOT` or `-UmkaRoot`, and includes native runtime assets that are present locally; on Windows it can build the Windows shim automatically when needed.
`.\scripts\verify-package-local.ps1` packs the project when needed, verifies third-party notices against `UMKA_ROOT` or `-UmkaRoot`, infers the supported local RID assets present under `runtimes/`, always requires the current platform RID, verifies package layout, and runs the package-consumer sample from the produced `.nupkg`.
Use `.\scripts\verify-ci-publishing-policy.ps1` to check that CI keeps `dotnet nuget push` restricted to the GitHub release-published publish job.
Use `.\scripts\verify-platform-support.ps1` to check that the documented supported RIDs, CI native build matrix, package layout verifier, local pack script, and .NET 9 SDK/target-framework settings stay aligned.
Use `.\scripts\verify-third-party-notices.ps1 -UmkaRoot C:\dev\umka-lang` to check that `THIRD-PARTY-NOTICES.md` includes the full Umka license text from the selected source checkout.

## GitHub Release Setup

1. Create a NuGet.org API key with permission to push `UmkaSharp`.
2. Add it to the GitHub repository secrets as `NUGET_API_KEY`.
3. Push normal commits to `main` and confirm CI builds native assets, runs Windows and Linux tests, verifies project-reference samples, packs the NuGet artifact, uploads it, and runs the package-consumer sample from the uploaded package on Windows and Linux with expected output.
4. Create a GitHub pre-release with a version tag such as `v0.1.0-alpha.1`.
5. The release workflow verifies `NUGET_API_KEY` is configured, then builds, tests, packs, and publishes the `.nupkg` and `.snupkg` to NuGet.org.

Publishing to NuGet.org is limited to the GitHub `release.published` event. Pushes to `main`, pull requests, and manual workflow runs build, test, pack, and consume package artifacts, but they do not publish to NuGet.

## Publishing Policy

Do not publish from a local shell. Local commands may build, test, pack, and smoke-test package artifacts, but NuGet publication must happen only from the GitHub `release.published` workflow.

## Remaining Manual Checks

- Confirm the NuGet.org package page metadata, README rendering, license, and symbols after the first publish.
- Confirm that the first GitHub release used the intended version tag before publishing, because the release tag becomes the NuGet package version.
- Decide whether to add more RIDs, such as `osx-x64`, `osx-arm64`, or `linux-arm64`, using the checklist in [platforms.md](platforms.md).
- Decide whether the Umka source should become a submodule or pinned download instead of the current CI checkout.
