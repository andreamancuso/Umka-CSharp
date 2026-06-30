using System.Runtime.InteropServices;

namespace UmkaSharp.Tests;

internal static class NativeTestEnvironment
{
    public static void RequireNativeShim()
    {
        if (NativeLibrary.TryLoad(
            "umka_shim",
            typeof(UmkaRuntime).Assembly,
            DllImportSearchPath.AssemblyDirectory,
            out var handle))
        {
            NativeLibrary.Free(handle);
            return;
        }

        throw new InvalidOperationException(
            "umka_shim could not be loaded. Build the native shim before running tests.");
    }
}
