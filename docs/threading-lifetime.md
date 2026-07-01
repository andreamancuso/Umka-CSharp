# Threading And Lifetime

`UmkaRuntime` is thread-affine. Use each runtime from the managed thread that created it.

## Thread Affinity

UmkaSharp records the creating thread ID and checks it on public runtime and function operations. Using a runtime from another thread throws `InvalidOperationException`.

`UmkaRuntime.RequestInterrupt(message)` is the execution-control exception: it may be called from another managed thread while the owning thread is executing Umka code. The caller must still keep the runtime alive; `RequestInterrupt` is not safe to race with `Dispose()`.

`UmkaRuntime.Dispose()` is the cleanup exception: it may be called from another managed thread after all execution, lookup, callback, and host-handle use has stopped. This mirrors the finalizer fallback, which may also run native cleanup from the finalizer thread. `Dispose()` is not a synchronization primitive; do not call it concurrently with other runtime operations.

Runtime-owned host handles and retained native values follow the same ownership rule for lifecycle and value-access operations. Create, read, convert, and dispose `UmkaHostHandle` and `UmkaNativeValue` instances on the runtime's owning thread.

Create a separate runtime per worker thread if you need concurrent execution.

## Runtime Ownership

`UmkaRuntime` owns:

- the native Umka instance
- registered callback slots
- managed delegates kept alive for native callback invocation
- host object handles created with `CreateHostHandle`
- retained native Umka values created with `CallNativeValue` or `GetNativeValue`

Dispose the runtime when finished:

```csharp
using var runtime = UmkaRuntime.FromSource(source);
runtime.Compile();
```

Calling `Dispose()` more than once is allowed. `UmkaRuntime.IsDisposed` reports the deterministic managed disposal state. Calling functions after disposal throws `ObjectDisposedException`. `UmkaRuntime` also has a finalizer fallback for missed disposal, but callers should still treat `Dispose()` as required because finalizers are nondeterministic and callbacks, host handles, or retained native values may otherwise stay alive longer than intended. Disposal releases any remaining retained native values, callbacks, and host handles even when cleanup is initiated from a non-owner thread after use has stopped. Disposal disables Umka compile warning callbacks before any internal cleanup work, so `WarningHandler` is not invoked from `Dispose()` or the finalizer.

`State` reports the observed runtime lifecycle as `Created`, `CompileAttempted`, `Compiled`, `Terminated`, or `Disposed`. `IsAlive` reports whether the runtime is still available for future code execution. It returns `false` after a native compile failure, runtime error, interruption, or script termination. `IsInterruptRequested` reports a pending native interrupt request, and `ClearInterrupt()` clears one before execution when cancellation is no longer wanted. A disposed runtime cannot be queried and throws `ObjectDisposedException`. Creation metadata such as `SourceFileName`, `StackSize`, `FileSystemEnabled`, `ImplementationLibrariesEnabled`, and `Arguments` is a managed snapshot and remains available for diagnostics after disposal. `RegisteredModuleNames` and `RegisteredCallbackNames` similarly remain available as managed registration snapshots.

Registered callbacks are disposed with their owning runtime. `UmkaCallback.IsDisposed` reports that lifecycle state; the callback handle is diagnostic-only after disposal. `GetCallback(name)` and `TryGetCallback(name, out callback)` can still recover those disposed handles for diagnostics from the runtime's managed registration table.

`UmkaCallFrame` is a transient view over native callback slots. It is valid only until the managed callback returns. Read or copy callback arguments, result metadata, and any host-handle targets inside the callback body; stored frame copies reject later reads. The `UmkaTypeInfo` values copied from `ParameterTypes` or `ResultType` are managed snapshots and remain safe to keep for diagnostics.

Host object handles are runtime-owned wrappers around managed `GCHandle` values. Create new handles only while the runtime is live and not in diagnostic-only compile-failure state. Existing handles can still be read, converted, resolved, or disposed on the runtime's owning thread after a compile failure or Umka runtime error, because they are managed resources rather than Umka heap values. Dispose an individual `UmkaHostHandle` when the script no longer needs it, or let the runtime dispose any remaining handles. `UmkaHostHandle.IsDisposed` exposes that lifecycle state, while `TryGetTarget<T>` gives direct handles a non-throwing type-mismatch path. A disposed handle cannot be passed with `UmkaValue.FromHostHandle`, and callback or runtime handle resolution rejects stale handle addresses.

`UmkaNativeValue` is a runtime-owned retained Umka value. Create one with `UmkaFunction.CallNativeValue()` for supported function results or `UmkaCallFrame.GetNativeValue(index)` inside a callback for supported callback arguments. Pass it back to the same runtime with `nativeValue.ToValue()` or `UmkaValue.FromNativeValue(nativeValue)`. When the retained value is a direct `fn` or closure, convert it to an `UmkaFunction` with `nativeValue.AsCallable()` or `TryAsCallable(out function)` and invoke it with the normal `Call*` methods. The target parameter or callback result type must be equivalent to the retained Umka type; UmkaSharp does not coerce retained values across different Umka types or runtimes. Dispose an individual `UmkaNativeValue` when the host no longer needs it, or let the runtime dispose remaining retained values. A disposed retained value cannot be converted back to `UmkaValue` or invoked through a retained callable wrapper.

## Umka Heap Values

UmkaSharp has two heap-value models. Copy APIs copy supported strings, `[]str`, reference-free dynamic arrays, supported nested dynamic arrays including `[][]str`, fixed-layout maps, direct-string maps, supported dynamic-array map values, built-in `any` values with scalar/string/null payloads, fixed-layout structs, and static arrays across the boundary. Retained-value APIs keep supported Umka heap values alive as opaque `UmkaNativeValue` handles and can pass those exact values back to the same runtime and equivalent Umka type. Built-in `any` values with heap payloads expose the payload as a retained native value.

`UmkaNativeValue` is not a general managed heap wrapper. It does not expose map entries, array elements, declared interface method tables, closure internals, or fiber resume/status. Direct `fn` and closure values are callable through a retained callable `UmkaFunction` wrapper while the runtime and retained value remain alive on the owning thread. Retained concrete Umka structs can also be assigned to non-empty interface parameters or callback results when Umka can construct the method table. Weak pointer handles are still copied only as opaque 64-bit handles without strengthening, target validation, rooting, or ownership transfer. Unsupported heap-backed copy shapes such as maps with reference-bearing keys or values beyond direct `str` or supported dynamic-array values, direct boxing of managed aggregate snapshots into `any`, and dynamic arrays whose element type contains Umka-managed references other than `str` or supported nested arrays remain rejected until wrapper rules exist for those entries.

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

`UmkaFunction` calls are valid only while the owning runtime is alive. Exported-function handles keep a native function context owned by the runtime. Retained callable handles additionally require the source `UmkaNativeValue` to remain undisposed. Neither form should be stored for later execution beyond the runtime lifetime. Name, module name, qualified name, parameter metadata, result metadata, and diagnostic `ToString()` output are managed snapshots captured at lookup time, so they can still be inspected for logging or assertions after disposal; all `Call*` methods continue to reject use after disposal or runtime termination.

## Native Assets

NuGet packages include native assets under `runtimes/{rid}/native/`. The current supported package RIDs are `win-x64` and `linux-x64`.

See [platforms.md](platforms.md) for candidate RIDs and the criteria for promoting another RID to supported.
