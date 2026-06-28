# Publishing UmkaSharp

UmkaSharp targets .NET 9 and publishes one managed package with RID-specific native assets.

## Package Name

The intended NuGet package ID is `UmkaSharp`. On 2026-06-28, NuGet's flat-container endpoint for `umkasharp` returned `404`, so the package name appears unclaimed.

## Local Verification

Run these before cutting a release:

```powershell
dotnet --version
.\native\build_windows_msvc.ps1 -Configuration Release
dotnet test UmkaSharp.sln --configuration Release --verbosity minimal
.\scripts\verify-linux-docker.ps1
.\scripts\pack-local.ps1 -Version 0.1.0-local
tar -tf artifacts\packages\UmkaSharp.0.1.0-local.nupkg
.\scripts\verify-package-local.ps1 -Version 0.1.0-local
```

The package should contain:

```text
lib/net9.0/UmkaSharp.dll
runtimes/win-x64/native/umka_shim.dll
runtimes/linux-x64/native/libumka_shim.so
README.md
LICENSE
THIRD-PARTY-NOTICES.md
```

## GitHub Release Setup

1. Create a NuGet.org API key with permission to push `UmkaSharp`.
2. Add it to the GitHub repository secrets as `NUGET_API_KEY`.
3. Push normal commits to `main` and confirm CI builds native assets, runs Windows and Linux tests, smoke-tests the package artifact, and uploads a CI package artifact.
4. Create a GitHub release with a version tag such as `v0.1.0`.
5. The release workflow builds, tests, packs, smoke-tests, and publishes the `.nupkg` and `.snupkg` to NuGet.org.

Publishing to NuGet.org is gated to the GitHub `release.published` event. Pushes to `main`, pull requests, manual workflow runs, and tag pushes can build/test/pack artifacts, but they do not publish to NuGet.

## Manual NuGet Push

If the release workflow cannot be used, publish a reviewed package artifact from a local shell where `NUGET_API_KEY` is set:

```powershell
dotnet nuget push .\UmkaSharp.0.1.0.nupkg --api-key $env:NUGET_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
dotnet nuget push .\UmkaSharp.0.1.0.snupkg --api-key $env:NUGET_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
```

## Remaining Manual Checks

- Confirm the NuGet.org package page metadata, README rendering, license, and symbols after the first publish.
- Confirm that the first GitHub release used the intended version tag before publishing, because the release tag becomes the NuGet package version.
- Decide whether to add more RIDs, such as `osx-x64`, `osx-arm64`, or `linux-arm64`.
- Decide whether the Umka source should become a submodule or pinned download instead of the current CI checkout.
