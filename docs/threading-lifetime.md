# Threading And Lifetime

`UmkaRuntime` is thread-affine. Use each runtime from the managed thread that created it.

## Thread Affinity

UmkaSharp records the creating thread ID and checks it on public runtime and function operations. Using a runtime from another thread throws `InvalidOperationException`.

`UmkaRuntime.Dispose()` is the cleanup exception: it may be called from another managed thread after all execution, lookup, callback, and host-handle use has stopped. This mirrors the finalizer fallback, which may also run native cleanup from the finalizer thread. `Dispose()` is not a synchronization primitive; do not call it concurrently with other runtime operations.

Runtime-owned host handles follow the same ownership rule for lifecycle and value-access operations. Create, read, convert, and dispose `UmkaHostHandle` instances on the runtime's owning thread.

Create a separate runtime per worker thread if you need concurrent execution.

## Runtime Ownership

`UmkaRuntime` owns:

- the native Umka instance
- registered callback slots
- managed delegates kept alive for native callback invocation
- host object handles created with `CreateHostHandle`

Dispose the runtime when finished:

```csharp
using var runtime = UmkaRuntime.FromSource(source);
runtime.Compile();
```

Calling `Dispose()` more than once is allowed. `UmkaRuntime.IsDisposed` reports the deterministic managed disposal state. Calling functions after disposal throws `ObjectDisposedException`. `UmkaRuntime` also has a finalizer fallback for missed disposal, but callers should still treat `Dispose()` as required because finalizers are nondeterministic and callbacks or host handles may otherwise stay alive longer than intended. Disposal releases any remaining callbacks and host handles even when cleanup is initiated from a non-owner thread after use has stopped. Disposal disables Umka compile warning callbacks before any internal cleanup work, so `WarningHandler` is not invoked from `Dispose()` or the finalizer.

`State` reports the observed runtime lifecycle as `Created`, `CompileAttempted`, `Compiled`, `Terminated`, or `Disposed`. `IsAlive` reports whether the runtime is still available for future code execution. It returns `false` after a native compile failure, runtime error, or script termination. A disposed runtime cannot be queried and throws `ObjectDisposedException`. Creation metadata such as `SourceFileName`, `StackSize`, `FileSystemEnabled`, `ImplementationLibrariesEnabled`, and `Arguments` is a managed snapshot and remains available for diagnostics after disposal. `RegisteredModuleNames` and `RegisteredCallbackNames` similarly remain available as managed registration snapshots.

Registered callbacks are disposed with their owning runtime. `UmkaCallback.IsDisposed` reports that lifecycle state; the callback handle is diagnostic-only after disposal. `GetCallback(name)` and `TryGetCallback(name, out callback)` can still recover those disposed handles for diagnostics from the runtime's managed registration table.

`UmkaCallFrame` is a transient view over native callback slots. It is valid only until the managed callback returns. Read or copy callback arguments, result metadata, and any host-handle targets inside the callback body; stored frame copies reject later reads. The `UmkaTypeInfo` values copied from `ParameterTypes` or `ResultType` are managed snapshots and remain safe to keep for diagnostics.

Host object handles are runtime-owned wrappers around managed `GCHandle` values. Create new handles only while the runtime is live and not in diagnostic-only compile-failure state. Existing handles can still be read, converted, resolved, or disposed on the runtime's owning thread after a compile failure or Umka runtime error, because they are managed resources rather than Umka heap values. Dispose an individual `UmkaHostHandle` when the script no longer needs it, or let the runtime dispose any remaining handles. `UmkaHostHandle.IsDisposed` exposes that lifecycle state, while `TryGetTarget<T>` gives direct handles a non-throwing type-mismatch path. A disposed handle cannot be passed with `UmkaValue.FromHostHandle`, and callback or runtime handle resolution rejects stale handle addresses.

## Umka Heap Values

UmkaSharp does not expose rooted managed wrappers for long-lived Umka heap values. Strings are copied into or out of native calls, fixed-layout structs and static arrays are copied through managed buffers, and unsupported heap-backed shapes such as dynamic arrays, maps, interfaces, closures, fibers, weak pointers, and `any` are metadata-only.

This is deliberate. A public heap wrapper would need clear rules for runtime ownership, thread affinity, native GC lifetime, disposal/finalization races, callback reentrancy, and package-safe native support. Until those rules are implemented and tested per Umka type, UmkaSharp keeps the managed boundary copy-based for supported values.

Runtime-owned host handles are separate from Umka heap values. They wrap managed objects in `GCHandle` instances owned by `UmkaRuntime` and travel through Umka as opaque pointer values. Resolve known handle pointers on the owning runtime with `GetHostObject<T>`, or use `TryGetHostObject<T>` when null, unknown, disposed, or wrong-type pointers should be handled as ordinary absence. For function pointer results that are known to carry host handles, use `CallHostObject<T>` for strict resolution or `TryCallHostObject<T>` for try-style resolution. Create separate handles per runtime instead of sharing handle addresses across runtimes.

Umka's C API has a separate heap-owned data path through `umkaAllocData(size, onFree)` plus `umkaIncRef` and `umkaDecRef`. That could support future Umka-owned wrappers whose native reference count calls an `onFree` hook. UmkaSharp does not use that mechanism for `UmkaHostHandle` today. Host handles are intentionally owned and disposed by the managed runtime instead, because an Umka-owned managed-object wrapper would need additional shim support plus clear rules for native finalizer ordering, managed `GCHandle` release, thread affinity, runtime shutdown, callback reentrancy, and error handling during cleanup.

## Compile Boundary

Before `Compile()` is called, the runtime can still be configured with modules and callbacks.

After `Compile()` is called, whether it succeeds or throws, the source graph is fixed. A native compile failure leaves the runtime in diagnostic-only `CompileAttempted` state, where `IsAlive` returns `false` and new host handles are also rejected. If native compilation succeeds but `WarningHandler` throws, the managed exception is rethrown after native compile returns and the runtime remains in `Compiled` state. Create a new `UmkaRuntime` to change source, modules, callbacks, or retry after a compile error.

After a successful `Compile()`:

- `Run()` may execute the compiled program
- `GetFunction()` may resolve exported functions
- `AddModule()`, `AddModuleFromFile()`, and `Register()` are rejected
- `Compile()` cannot be called again

`Run()` can be called repeatedly on the owning thread when the compiled program's `main()` is designed to be re-entered by the host.

After an Umka runtime error or script termination, `IsAlive` becomes `false` and `State` reports `Terminated` after the managed wrapper observes that state. Further `Run()`, `GetFunction()`, `TryGetFunction()`, and `UmkaFunction.Call*` attempts throw `InvalidOperationException`. `GetLastError()` remains available so the host can inspect the native error before disposing the runtime; `TryGetLastError(out error)` returns `false` instead when the native runtime has no meaningful diagnostic.

## Function Lifetime

`UmkaFunction` calls are valid only while the owning runtime is alive. The handle keeps a native function context owned by the runtime and should not be stored for later execution beyond the runtime lifetime. Name, module name, qualified name, parameter metadata, result metadata, and diagnostic `ToString()` output are managed snapshots captured at lookup time, so they can still be inspected for logging or assertions after disposal; all `Call*` methods continue to reject use after disposal or runtime termination.

## Native Assets

NuGet packages include native assets under `runtimes/{rid}/native/`. The current supported package RIDs are `win-x64` and `linux-x64`.

See [platforms.md](platforms.md) for candidate RIDs and the criteria for promoting another RID to supported.
