namespace UmkaSharp;

using System.Runtime.InteropServices;

internal static class UmkaMapNativeWriter
{
    public static int SetFunctionArgument(
        IntPtr runtime,
        ref NativeMethods.FunctionContext function,
        int index,
        UmkaValue value)
    {
        if (value.IsStringKeyMap && value.IsStringValueMap)
        {
            using var keys = new NativeUtf8StringArray(value.GetStringMapKeys());
            using var values = new NativeUtf8StringArray(value.GetStringMapValues());
            return NativeMethods.ContextSetArgStringMap(
                runtime,
                ref function,
                index,
                keys.Pointer,
                keys.Length,
                values.Pointer,
                values.Length);
        }

        if (value.IsStringKeyMap)
        {
            using var keys = new NativeUtf8StringArray(value.GetStringMapKeys());
            using var values = UnmanagedMapBuffer.CreateValues(value);
            return NativeMethods.ContextSetArgStringKeyMap(
                runtime,
                ref function,
                index,
                keys.Pointer,
                keys.Length,
                values.Pointer,
                values.ByteCount);
        }

        if (value.IsStringValueMap)
        {
            using var keys = UnmanagedMapBuffer.CreateKeys(value);
            using var values = new NativeUtf8StringArray(value.GetStringMapValues());
            return NativeMethods.ContextSetArgStringValueMap(
                runtime,
                ref function,
                index,
                keys.Pointer,
                keys.ByteCount,
                values.Pointer,
                values.Length);
        }

        using var fixedKeys = UnmanagedMapBuffer.CreateKeys(value);
        using var fixedValues = UnmanagedMapBuffer.CreateValues(value);
        return NativeMethods.ContextSetArgMap(
            runtime,
            ref function,
            index,
            fixedKeys.Pointer,
            fixedKeys.ByteCount,
            fixedValues.Pointer,
            fixedValues.ByteCount);
    }

    public static int SetCallbackResult(IntPtr parameters, IntPtr result, UmkaValue value)
    {
        if (value.IsStringKeyMap && value.IsStringValueMap)
        {
            using var keys = new NativeUtf8StringArray(value.GetStringMapKeys());
            using var values = new NativeUtf8StringArray(value.GetStringMapValues());
            return NativeMethods.CallbackSetResultStringMap(
                parameters,
                result,
                keys.Pointer,
                keys.Length,
                values.Pointer,
                values.Length);
        }

        if (value.IsStringKeyMap)
        {
            using var keys = new NativeUtf8StringArray(value.GetStringMapKeys());
            using var values = UnmanagedMapBuffer.CreateValues(value);
            return NativeMethods.CallbackSetResultStringKeyMap(
                parameters,
                result,
                keys.Pointer,
                keys.Length,
                values.Pointer,
                values.ByteCount);
        }

        if (value.IsStringValueMap)
        {
            using var keys = UnmanagedMapBuffer.CreateKeys(value);
            using var values = new NativeUtf8StringArray(value.GetStringMapValues());
            return NativeMethods.CallbackSetResultStringValueMap(
                parameters,
                result,
                keys.Pointer,
                keys.ByteCount,
                values.Pointer,
                values.Length);
        }

        using var fixedKeys = UnmanagedMapBuffer.CreateKeys(value);
        using var fixedValues = UnmanagedMapBuffer.CreateValues(value);
        return NativeMethods.CallbackSetResultMap(
            parameters,
            result,
            fixedKeys.Pointer,
            fixedKeys.ByteCount,
            fixedValues.Pointer,
            fixedValues.ByteCount);
    }

    private sealed class UnmanagedMapBuffer : IDisposable
    {
        private bool _disposed;

        private UnmanagedMapBuffer(IntPtr pointer, int byteCount)
        {
            Pointer = pointer;
            ByteCount = byteCount;
        }

        public IntPtr Pointer { get; private set; }

        public int ByteCount { get; }

        public static UnmanagedMapBuffer CreateKeys(UmkaValue value)
        {
            var byteCount = checked(value.MapCount * value.MapKeySize);
            var pointer = byteCount == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(byteCount);
            try
            {
                if (byteCount > 0)
                    value.CopyMapKeysTo(pointer);

                return new(pointer, byteCount);
            }
            catch
            {
                if (pointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(pointer);
                throw;
            }
        }

        public static UnmanagedMapBuffer CreateValues(UmkaValue value)
        {
            var byteCount = checked(value.MapCount * value.MapValueSize);
            var pointer = byteCount == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(byteCount);
            try
            {
                if (byteCount > 0)
                    value.CopyMapValuesTo(pointer);

                return new(pointer, byteCount);
            }
            catch
            {
                if (pointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(pointer);
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            if (Pointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Pointer);
                Pointer = IntPtr.Zero;
            }

            _disposed = true;
        }
    }
}
