# Third-Party Notices

UmkaSharp distributes a managed .NET library and RID-specific native bridge assets. The native assets are built from this repository's `native/` shim plus the Umka runtime sources selected by `UMKA_ROOT` during local builds or by the GitHub Actions checkout during CI builds.

## Runtime Components

### Umka

- Upstream: <https://github.com/vtereshkov/umka-lang>
- Local development checkout: `C:\dev\umka-lang`
- License: BSD 2-Clause License
- Package role: included in native runtime assets such as `runtimes/win-x64/native/umka_shim.dll` and `runtimes/linux-x64/native/libumka_shim.so`

```text
BSD 2-Clause License

Copyright (c) 2020-2026, Vasiliy Tereshkov
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## Build And Test Dependencies

The repository also uses development-time NuGet packages such as Microsoft Source Link, xUnit, Microsoft.NET.Test.Sdk, and BenchmarkDotNet. They are used to build, test, debug, or benchmark UmkaSharp and are not runtime dependencies of the `UmkaSharp` NuGet package.

Before publishing a release, confirm the generated `.nupkg` does not introduce additional runtime dependencies or native components that require notice updates.
