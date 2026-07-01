# Errors

UmkaSharp reports Umka compile-time and runtime failures by throwing `UmkaException`. Use `TryCompile(out error)` when native Umka compile errors on an existing runtime should return `false` with an `UmkaError` instead of throwing. Use `TryCompileSource()` or `TryCompileFile()` when the host wants UmkaSharp to create, optionally configure, compile, and either return an owned compiled runtime or dispose the transient runtime and return `false` with `UmkaError` data. Use `TryRun(out exception)` when `main()` runtime failures should return `false` with the full `UmkaException` object as data. `GetFunction()` also throws `UmkaException` for missing exported functions, with a managed message naming the requested function and module, `UmkaException.Error.FunctionName` set to the requested function, and `Error.FileName` set to the requested module file name for module lookups. Use `TryGetFunction()` when optional exported hooks should not throw when absent; a missing optional hook returns `false` without terminating the runtime. Once a module function is resolved, managed validation errors use the module-qualified function identity, such as `math.um::addFee`.

After a compile-time `UmkaException` or `TryCompile(out error)` returning `false`, the runtime is diagnostic-only. `State` reports `CompileAttempted`, `IsAlive` returns `false`, and `GetLastError()` / `TryGetLastError(out error)` remain available, but `Compile()`, `TryCompile(out error)`, `AddModule()`, `AddModuleFromFile()`, `Register()`, and `CreateHostHandle()` are rejected; create a new runtime to retry with different source or modules. Host handles created before the compile failure remain managed resources that can still be inspected, resolved, and disposed until runtime disposal.

Compile warnings are optional diagnostics. Set `UmkaRuntimeOptions.WarningHandler` to receive warnings as `UmkaError` values while public `Compile()` or `TryCompile(out error)` runs. Umka reports warning code `0`; warnings do not fail compilation unless the managed warning handler throws. Warning-handler exceptions are captured and rethrown after native code has returned, so they never cross the unmanaged callback boundary directly. If native compilation succeeded before the handler exception was rethrown on a manually created runtime, the runtime remains compiled and exported functions can still be resolved. Compiled factories dispose their transient runtime before rethrowing that warning-handler failure because no owned runtime is returned to the caller. Disposal disables warning callbacks before internal cleanup work.

## Exception Shape

`UmkaException.Error` contains the native error details available from Umka:

- `FileName`
- `FunctionName`
- `Line`
- `Position`
- `Code`
- `Message`

The exception message includes the Umka message and, when available, the source location and function name. Managed errors without source coordinates, such as missing exported functions, include the available file/module and function identity without adding a `:0:0` location. Umka may report `FileName` as an absolute path even when the runtime was created from a source string with a logical file name.

Publicly constructed `UmkaError` values preserve nullable native diagnostic fields, but reject embedded NUL characters in string fields and negative `Line` or `Position` values.

```csharp
try
{
    runtime.Compile();
}
catch (UmkaException ex)
{
    Console.WriteLine(ex.Error.Message);
    Console.WriteLine($"{ex.Error.FileName}:{ex.Error.Line}:{ex.Error.Position}");
}
```

## Managed API Errors

UmkaSharp throws normal .NET exceptions for managed misuse:

- `ArgumentNullException` for null source strings and callbacks
- `ArgumentException` for blank file/function/module/callback names, embedded NUL characters in strings passed across the native boundary, duplicate module or callback names, reserved callback names, void call arguments, wrong function argument counts, unsupported argument/result types, and argument/result kind mismatches
- `ArgumentOutOfRangeException` for `UmkaValue.From(char)` values, narrow integer and `char` call arguments, or callback results outside the Umka type range
- `InvalidOperationException` for pre-compile calls, post-compile mutation, double compilation, thread-affinity violations, invalid result readers, structured results read without `CallStruct<T>()`, and inactive or default `UmkaCallFrame` reads
- `InvalidOperationException` when `UmkaRuntimeOptions.WarningHandler` throws during native compilation
- `ObjectDisposedException` after the runtime has been disposed

## Callback Failures

Managed callback exceptions are captured on the returned `UmkaCallback.LastException`, mirrored on `UmkaRuntime.LastCallbackException`, and attached as `UmkaException.InnerException` when the Umka call reports the callback failure. `TryRun(out exception)` returns that same `UmkaException` object instead of throwing it. The failed call terminates the runtime just like other Umka runtime errors: `IsAlive` returns `false`, and later execution or function lookup attempts are rejected. This includes exceptions thrown by user callback code, wrong callback frame readers, invalid callback argument indexes, callback result kind mismatches, unsupported reference-bearing map or aggregate callback result types, and out-of-range narrow integer, `char`, or finite `real32` callback results.

`UmkaCallFrame` itself is valid only during the callback invocation. A default frame, or a frame copy read after the callback returns, throws `InvalidOperationException` before reading native callback slots.

## Runtime Termination

After an Umka runtime error, an interrupted Umka call, or `TryRun(out exception)` returning `false`, UmkaSharp treats the native runtime as terminated. `State` reports `Terminated`, `IsAlive` returns `false`, and further execution or function lookup attempts throw `InvalidOperationException`. `GetLastError()` can still be called to inspect the final native error before disposing the runtime, or use `TryGetLastError(out error)` when hosts want `false` for an empty native diagnostic. Pending interrupt state is reported by `IsInterruptRequested` and can be cleared with `ClearInterrupt()` for diagnostics after the failure.

## Current Limits

UmkaSharp prevalidates supported scalar, weak pointer, fixed-layout aggregate, copy-based dynamic-array, string-array, nested dynamic-array, retained native-value, built-in `any`, and copy-based map function argument/result readers and callback frame readers/results. Dynamic arrays are supported by copy APIs when the element type contains no Umka-managed references and the managed element layout exactly matches the Umka element layout; `[]str` uses dedicated string-array readers and writers; nested dynamic arrays are supported when the inner element type contains no Umka-managed references or is direct `str`. Maps are copied across supported function argument and callback result directions when key/value types are fixed-layout without Umka-managed references or direct `str`; function results and callback arguments also support dynamic-array map values with reference-free or direct-`str` elements. `UmkaAnyValue` can inspect/construct built-in `any` values for null, scalar/string, and retained same-runtime native payloads. `UmkaNativeValue` can retain supported native values and pass them back unchanged to the same runtime and equivalent Umka type; retained direct `fn` and closure values can be invoked through `AsCallable()`. It still does not expose array/map entries, declared interface dispatch, closure internals, or fiber ownership. UmkaSharp does not strengthen or validate weak pointer targets, and does not construct maps whose entries require unsupported heap ownership.
