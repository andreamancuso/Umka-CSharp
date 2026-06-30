# Platforms

UmkaSharp publishes one managed `net9.0` package with RID-specific native assets.

## Supported Package RIDs

| RID | Native asset | Verification |
| --- | --- | --- |
| `win-x64` | `runtimes/win-x64/native/umka_shim.dll` | GitHub Actions builds the native shim on Windows, runs the test suite with the downloaded asset, packs the asset, and runs the package-consumer sample on Windows. |
| `linux-x64` | `runtimes/linux-x64/native/libumka_shim.so` | GitHub Actions builds the native shim on Ubuntu, runs the test suite with the downloaded asset, packs the asset, and runs the package-consumer sample on Ubuntu. The local Docker verifier can refresh and test this asset from Windows. |

Do not list another RID as package-supported until its native asset is built, packaged, and consumed successfully on that RID.

## Candidate RID Checklist

Each candidate RID needs the same evidence before it can become package-supported:

- Native build succeeds in CI or a documented reproducible local environment.
- The native asset uses the correct platform name and package path under `runtimes/{rid}/native/`.
- `verify-package-layout.ps1` requires the new RID when the asset is expected and rejects unverified runtime native assets.
- A package-consumer smoke test restores the produced `.nupkg` and runs successfully on that RID.
- Platform-specific setup, compiler requirements, and limitations are documented.
- GitHub Actions artifacts and package assembly include the new native asset.
- The published package metadata and docs are updated only after the verification path exists.

## Candidate RIDs

### `linux-arm64`

Native asset path: `runtimes/linux-arm64/native/libumka_shim.so`

Open work:

- Decide whether to build on native ARM64 Linux infrastructure or cross-compile from x64.
- Confirm the Umka C sources and shim build cleanly for ARM64 Linux.
- Add a CI or documented local build path that produces `libumka_shim.so`.
- Run the package-consumer sample from a locally packed package on Linux ARM64.
- Add `linux-arm64` to package layout verification only after the smoke path exists.

### `osx-x64`

Native asset path: `runtimes/osx-x64/native/libumka_shim.dylib`

Open work:

- Confirm the Umka C sources and shim build cleanly for Intel macOS.
- Document required Xcode command line tools and CMake invocation.
- Add a CI or documented local build path that produces `libumka_shim.dylib`.
- Run the package-consumer sample from a locally packed package on Intel macOS.
- Add `osx-x64` to package layout verification only after the smoke path exists.

### `osx-arm64`

Native asset path: `runtimes/osx-arm64/native/libumka_shim.dylib`

Open work:

- Confirm the Umka C sources and shim build cleanly for Apple Silicon.
- Document required Xcode command line tools and CMake invocation.
- Add a CI or documented local build path that produces `libumka_shim.dylib`.
- Run the package-consumer sample from a locally packed package on Apple Silicon.
- Add `osx-arm64` to package layout verification only after the smoke path exists.

## Policy

Adding a RID is a packaging promise. The package should not include a RID-specific asset, README claim, or package-layout requirement until the matching consumer smoke has run on that platform. The package layout verifier intentionally rejects unexpected `runtimes/{rid}/native/` assets so candidate RIDs do not enter the package accidentally.
