using System.Runtime.InteropServices;

namespace UmkaSharp;

internal static partial class NativeMethods
{
    private const string LibraryName = "umka_shim";

    [StructLayout(LayoutKind.Sequential)]
    internal struct FunctionContext
    {
        public long EntryOffset;
        public IntPtr Parameters;
        public IntPtr Result;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ManagedCallback(IntPtr state, IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_create", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Create(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? source,
        int stackSize,
        int fileSystemEnabled,
        int implLibsEnabled,
        out IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Free(IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_compile", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Compile(IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_run", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Run(IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_alive", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Alive(IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_add_module", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int AddModule(
        IntPtr runtime,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string source);

    [DllImport(LibraryName, EntryPoint = "ushim_add_callback", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int AddCallback(
        IntPtr runtime,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        IntPtr callback,
        IntPtr state,
        out IntPtr slot);

    [DllImport(LibraryName, EntryPoint = "ushim_free_callback", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void FreeCallback(IntPtr slot);

    [DllImport(LibraryName, EntryPoint = "ushim_get_function", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetFunction(
        IntPtr runtime,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? moduleName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string functionName,
        out FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_call", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Call(IntPtr runtime, ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_int", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgInt(IntPtr runtime, ref FunctionContext function, int index, long value);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_uint", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgUInt(IntPtr runtime, ref FunctionContext function, int index, ulong value);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_real", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgReal(IntPtr runtime, ref FunctionContext function, int index, double value);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_ptr", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgPointer(IntPtr runtime, ref FunctionContext function, int index, IntPtr value);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_string", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgString(
        IntPtr runtime,
        ref FunctionContext function,
        int index,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? value);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_int", CallingConvention = CallingConvention.Cdecl)]
    internal static extern long ContextGetResultInt(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_uint", CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong ContextGetResultUInt(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_real", CallingConvention = CallingConvention.Cdecl)]
    internal static extern double ContextGetResultReal(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_ptr", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ContextGetResultPointer(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultSize(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_result_buffer", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetResultBuffer(ref FunctionContext function, IntPtr buffer);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_string", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ContextGetResultString(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_param_int", CallingConvention = CallingConvention.Cdecl)]
    internal static extern long CallbackGetParamInt(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_param_uint", CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong CallbackGetParamUInt(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_param_real", CallingConvention = CallingConvention.Cdecl)]
    internal static extern double CallbackGetParamReal(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_param_ptr", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CallbackGetParamPointer(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_param_string", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CallbackGetParamString(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_int", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CallbackSetResultInt(IntPtr parameters, IntPtr result, long value);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_uint", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CallbackSetResultUInt(IntPtr parameters, IntPtr result, ulong value);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_real", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CallbackSetResultReal(IntPtr parameters, IntPtr result, double value);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_ptr", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CallbackSetResultPointer(IntPtr parameters, IntPtr result, IntPtr value);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_string", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CallbackSetResultString(
        IntPtr parameters,
        IntPtr result,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? value);

    [DllImport(LibraryName, EntryPoint = "ushim_error_file_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ErrorFileName(IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_error_function_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ErrorFunctionName(IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_error_message", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ErrorMessage(IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_error_line", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ErrorLine(IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_error_position", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ErrorPosition(IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_error_code", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ErrorCode(IntPtr runtime);
}
