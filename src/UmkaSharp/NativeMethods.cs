using System.Runtime.InteropServices;

namespace UmkaSharp;

#pragma warning disable CA2101 // String parameters use explicit LPUTF8Str marshalling for Umka's UTF-8 C ABI.
internal static partial class NativeMethods
{
    private const string LibraryName = "umka_shim";
    internal const int NotFoundStatus = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct FunctionContext
    {
        public long EntryOffset;
        public IntPtr Parameters;
        public IntPtr Result;
        public IntPtr FunctionType;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ManagedCallback(IntPtr state, IntPtr parameters, IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ManagedWarningCallback(IntPtr warning);

    [DllImport(LibraryName, EntryPoint = "ushim_create", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int Create(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? source,
        int stackSize,
        int argumentCount,
        IntPtr arguments,
        int fileSystemEnabled,
        int implLibsEnabled,
        IntPtr warningCallback,
        out IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Free(IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_compile", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Compile(IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_clear_warning_callback", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ClearWarningCallback(IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_run", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Run(IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_alive", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Alive(IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_request_interrupt", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void RequestInterrupt(
        IntPtr runtime,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? message);

    [DllImport(LibraryName, EntryPoint = "ushim_clear_interrupt", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ClearInterrupt(IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_interrupt_requested", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int InterruptRequested(IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "ushim_add_module", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int AddModule(
        IntPtr runtime,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string source);

    [DllImport(LibraryName, EntryPoint = "ushim_add_callback", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int AddCallback(
        IntPtr runtime,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        IntPtr callback,
        IntPtr state,
        out IntPtr slot);

    [DllImport(LibraryName, EntryPoint = "ushim_free_callback", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void FreeCallback(IntPtr slot);

    [DllImport(LibraryName, EntryPoint = "ushim_get_function", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int GetFunction(
        IntPtr runtime,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? moduleName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string functionName,
        out FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_call", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Call(IntPtr runtime, ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_call_retain_result", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextCallRetainResult(IntPtr runtime, ref FunctionContext function, out IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_callable_valid", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueCallableValid(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_make_callable_context", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueMakeCallableContext(IntPtr runtime, IntPtr handle, out FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_callable_call", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallableCall(IntPtr runtime, IntPtr handle, ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_callable_call_retain_result", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallableCallRetainResult(IntPtr runtime, IntPtr callable, ref FunctionContext function, out IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_native_value", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgNativeValue(IntPtr runtime, ref FunctionContext function, int index, IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_release", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void NativeValueRelease(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_retain_host_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueRetainHostData(IntPtr runtime, IntPtr pointer, out IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_state", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyState(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_self_state", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnySelfState(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind NativeValueAnyGetPayloadKind(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyGetPayloadSize(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_item_count", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyGetPayloadItemCount(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyGetPayloadHasReferences(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr NativeValueAnyGetPayloadTypeName(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_element_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind NativeValueAnyGetPayloadElementKind(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_element_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyGetPayloadElementSize(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_element_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyGetPayloadElementHasReferences(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_element_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr NativeValueAnyGetPayloadElementTypeName(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_nested_element_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind NativeValueAnyGetPayloadNestedElementKind(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_nested_element_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyGetPayloadNestedElementSize(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_nested_element_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyGetPayloadNestedElementHasReferences(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_nested_element_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr NativeValueAnyGetPayloadNestedElementTypeName(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_map_key_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind NativeValueAnyGetPayloadMapKeyKind(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_map_key_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyGetPayloadMapKeySize(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_map_key_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyGetPayloadMapKeyHasReferences(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_map_key_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr NativeValueAnyGetPayloadMapKeyTypeName(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_map_value_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind NativeValueAnyGetPayloadMapValueKind(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_map_value_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyGetPayloadMapValueSize(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_map_value_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyGetPayloadMapValueHasReferences(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_map_value_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr NativeValueAnyGetPayloadMapValueTypeName(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_map_value_element_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind NativeValueAnyGetPayloadMapValueElementKind(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_map_value_element_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyGetPayloadMapValueElementSize(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_map_value_element_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyGetPayloadMapValueElementHasReferences(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_map_value_element_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr NativeValueAnyGetPayloadMapValueElementTypeName(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_is_variadic_parameter_list", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyGetPayloadIsVariadicParameterList(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_is_enum", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyGetPayloadIsEnum(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_enum_member_count", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyGetPayloadEnumMemberCount(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_enum_member_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr NativeValueAnyGetPayloadEnumMemberName(IntPtr handle, int memberIndex);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_enum_member_signed_value", CallingConvention = CallingConvention.Cdecl)]
    internal static extern long NativeValueAnyGetPayloadEnumMemberSignedValue(IntPtr handle, int memberIndex);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_enum_member_unsigned_value", CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong NativeValueAnyGetPayloadEnumMemberUnsignedValue(IntPtr handle, int memberIndex);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_int", CallingConvention = CallingConvention.Cdecl)]
    internal static extern long NativeValueAnyGetPayloadInt(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_uint", CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong NativeValueAnyGetPayloadUInt(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_real", CallingConvention = CallingConvention.Cdecl)]
    internal static extern double NativeValueAnyGetPayloadReal(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_ptr", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr NativeValueAnyGetPayloadPointer(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_get_payload_string", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr NativeValueAnyGetPayloadString(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_native_value_any_retain_payload", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NativeValueAnyRetainPayload(IntPtr runtime, IntPtr handle, out IntPtr payloadHandle);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_int", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgInt(IntPtr runtime, ref FunctionContext function, int index, long value);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_uint", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgUInt(IntPtr runtime, ref FunctionContext function, int index, ulong value);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_real", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgReal(IntPtr runtime, ref FunctionContext function, int index, double value);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_ptr", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgPointer(IntPtr runtime, ref FunctionContext function, int index, IntPtr value);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_string", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int ContextSetArgString(
        IntPtr runtime,
        ref FunctionContext function,
        int index,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? value);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_any_null", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgAnyNull(IntPtr runtime, ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_any_int", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgAnyInt(IntPtr runtime, ref FunctionContext function, int index, long value);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_any_uint", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgAnyUInt(IntPtr runtime, ref FunctionContext function, int index, ulong value);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_any_char", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgAnyChar(IntPtr runtime, ref FunctionContext function, int index, ulong value);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_any_real", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgAnyReal(IntPtr runtime, ref FunctionContext function, int index, double value);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_any_bool", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgAnyBool(IntPtr runtime, ref FunctionContext function, int index, int value);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_any_string", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int ContextSetArgAnyString(
        IntPtr runtime,
        ref FunctionContext function,
        int index,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? value);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_any_native_value", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgAnyNativeValue(IntPtr runtime, ref FunctionContext function, int index, IntPtr handle);

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

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_item_count", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultItemCount(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_argument_count", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetArgumentCount(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_required_argument_count", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetRequiredArgumentCount(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_default_argument_count", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetDefaultArgumentCount(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_default_arguments", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetDefaultArguments(IntPtr runtime, ref FunctionContext function, int providedCount);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind ContextGetParameterKind(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetParameterSize(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_item_count", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetParameterItemCount(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_element_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind ContextGetParameterElementKind(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_element_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetParameterElementSize(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_element_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetParameterElementHasReferences(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_element_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ContextGetParameterElementTypeName(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_nested_element_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind ContextGetParameterNestedElementKind(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_nested_element_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetParameterNestedElementSize(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_nested_element_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetParameterNestedElementHasReferences(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_nested_element_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ContextGetParameterNestedElementTypeName(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_map_key_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind ContextGetParameterMapKeyKind(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_map_key_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetParameterMapKeySize(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_map_key_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetParameterMapKeyHasReferences(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_map_key_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ContextGetParameterMapKeyTypeName(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_map_value_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind ContextGetParameterMapValueKind(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_map_value_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetParameterMapValueSize(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_map_value_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetParameterMapValueHasReferences(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_map_value_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ContextGetParameterMapValueTypeName(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_map_value_element_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind ContextGetParameterMapValueElementKind(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_map_value_element_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetParameterMapValueElementSize(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_map_value_element_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetParameterMapValueElementHasReferences(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_map_value_element_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ContextGetParameterMapValueElementTypeName(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_is_variadic_parameter_list", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetParameterIsVariadicParameterList(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetParameterHasReferences(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ContextGetParameterTypeName(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_is_enum", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetParameterIsEnum(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_enum_member_count", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetParameterEnumMemberCount(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_enum_member_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ContextGetParameterEnumMemberName(ref FunctionContext function, int parameterIndex, int memberIndex);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_enum_member_signed_value", CallingConvention = CallingConvention.Cdecl)]
    internal static extern long ContextGetParameterEnumMemberSignedValue(ref FunctionContext function, int parameterIndex, int memberIndex);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_parameter_enum_member_unsigned_value", CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong ContextGetParameterEnumMemberUnsignedValue(ref FunctionContext function, int parameterIndex, int memberIndex);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind ContextGetResultKind(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_element_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind ContextGetResultElementKind(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_element_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultElementSize(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_element_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultElementHasReferences(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_element_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ContextGetResultElementTypeName(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_nested_element_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind ContextGetResultNestedElementKind(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_nested_element_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultNestedElementSize(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_nested_element_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultNestedElementHasReferences(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_nested_element_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ContextGetResultNestedElementTypeName(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_map_key_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind ContextGetResultMapKeyKind(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_map_key_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultMapKeySize(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_map_key_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultMapKeyHasReferences(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_map_key_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ContextGetResultMapKeyTypeName(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_map_value_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind ContextGetResultMapValueKind(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_map_value_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultMapValueSize(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_map_value_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultMapValueHasReferences(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_map_value_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ContextGetResultMapValueTypeName(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_map_value_element_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind ContextGetResultMapValueElementKind(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_map_value_element_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultMapValueElementSize(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_map_value_element_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultMapValueElementHasReferences(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_map_value_element_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ContextGetResultMapValueElementTypeName(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_is_variadic_parameter_list", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultIsVariadicParameterList(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultHasReferences(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ContextGetResultTypeName(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_is_enum", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultIsEnum(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_enum_member_count", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultEnumMemberCount(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_enum_member_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ContextGetResultEnumMemberName(ref FunctionContext function, int memberIndex);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_enum_member_signed_value", CallingConvention = CallingConvention.Cdecl)]
    internal static extern long ContextGetResultEnumMemberSignedValue(ref FunctionContext function, int memberIndex);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_enum_member_unsigned_value", CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong ContextGetResultEnumMemberUnsignedValue(ref FunctionContext function, int memberIndex);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_result_buffer", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetResultBuffer(ref FunctionContext function, IntPtr buffer);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgData(
        IntPtr runtime,
        ref FunctionContext function,
        int index,
        IntPtr value,
        int size);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_dynarray", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgDynamicArray(
        IntPtr runtime,
        ref FunctionContext function,
        int index,
        IntPtr value,
        int length,
        int elementSize);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_dynarray_strings", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgStringDynamicArray(
        IntPtr runtime,
        ref FunctionContext function,
        int index,
        IntPtr values,
        int length);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_nested_dynarray", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgNestedDynamicArray(
        IntPtr runtime,
        ref FunctionContext function,
        int index,
        IntPtr lengths,
        int lengthCount,
        IntPtr values,
        int valueByteCount,
        int elementSize);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_nested_dynarray_strings", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgNestedStringArray(
        IntPtr runtime,
        ref FunctionContext function,
        int index,
        IntPtr lengths,
        int lengthCount,
        IntPtr values,
        int valueCount);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_map", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgMap(
        IntPtr runtime,
        ref FunctionContext function,
        int index,
        IntPtr keys,
        int keyBytes,
        IntPtr values,
        int valueBytes);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_string_key_map", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgStringKeyMap(
        IntPtr runtime,
        ref FunctionContext function,
        int index,
        IntPtr keys,
        int keyCount,
        IntPtr values,
        int valueBytes);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_string_value_map", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgStringValueMap(
        IntPtr runtime,
        ref FunctionContext function,
        int index,
        IntPtr keys,
        int keyBytes,
        IntPtr values,
        int valueCount);

    [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_string_map", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextSetArgStringMap(
        IntPtr runtime,
        ref FunctionContext function,
        int index,
        IntPtr keys,
        int keyCount,
        IntPtr values,
        int valueCount);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_dynarray_length", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultDynamicArrayLength(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_copy_result_dynarray_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextCopyResultDynamicArrayData(ref FunctionContext function, IntPtr buffer, int size);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_dynarray_string", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultDynamicArrayString(ref FunctionContext function, int index, out IntPtr value);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_nested_dynarray_length", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultNestedDynamicArrayLength(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_copy_result_nested_dynarray_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextCopyResultNestedDynamicArrayData(
        ref FunctionContext function,
        int index,
        IntPtr buffer,
        int size,
        int elementSize);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_nested_string_array_length", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultNestedStringArrayLength(ref FunctionContext function, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_context_copy_result_nested_string_array_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextCopyResultNestedStringArrayData(
        ref FunctionContext function,
        int index,
        IntPtr values,
        int valueCount);

    [DllImport(LibraryName, EntryPoint = "ushim_context_release_result_dynarray", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextReleaseResultDynamicArray(IntPtr runtime, ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_map_count", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextGetResultMapCount(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_context_copy_result_map_entries", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextCopyResultMapEntries(
        ref FunctionContext function,
        IntPtr keys,
        int keyBytes,
        IntPtr values,
        int valueBytes);

    [DllImport(LibraryName, EntryPoint = "ushim_context_copy_result_string_key_map_entries", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextCopyResultStringKeyMapEntries(
        ref FunctionContext function,
        IntPtr keys,
        int keyCount,
        IntPtr values,
        int valueBytes);

    [DllImport(LibraryName, EntryPoint = "ushim_context_copy_result_string_value_map_entries", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextCopyResultStringValueMapEntries(
        ref FunctionContext function,
        IntPtr keys,
        int keyBytes,
        IntPtr values,
        int valueCount);

    [DllImport(LibraryName, EntryPoint = "ushim_context_copy_result_string_map_entries", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextCopyResultStringMapEntries(
        ref FunctionContext function,
        IntPtr keys,
        int keyCount,
        IntPtr values,
        int valueCount);

    [DllImport(LibraryName, EntryPoint = "ushim_context_copy_result_map_dynarray_value_entries", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextCopyResultMapDynamicArrayValueEntries(
        ref FunctionContext function,
        IntPtr keys,
        int keyBytes,
        IntPtr lengths,
        int lengthCount,
        int elementSize);

    [DllImport(LibraryName, EntryPoint = "ushim_context_copy_result_map_dynarray_value_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextCopyResultMapDynamicArrayValueData(
        ref FunctionContext function,
        int entryIndex,
        IntPtr buffer,
        int size,
        int elementSize);

    [DllImport(LibraryName, EntryPoint = "ushim_context_copy_result_map_string_array_value_entries", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextCopyResultMapStringArrayValueEntries(
        ref FunctionContext function,
        IntPtr keys,
        int keyBytes,
        IntPtr lengths,
        int lengthCount);

    [DllImport(LibraryName, EntryPoint = "ushim_context_copy_result_map_string_array_value_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextCopyResultMapStringArrayValueData(
        ref FunctionContext function,
        int entryIndex,
        IntPtr values,
        int valueCount);

    [DllImport(LibraryName, EntryPoint = "ushim_context_copy_result_string_key_map_dynarray_value_entries", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextCopyResultStringKeyMapDynamicArrayValueEntries(
        ref FunctionContext function,
        IntPtr keys,
        int keyCount,
        IntPtr lengths,
        int lengthCount,
        int elementSize);

    [DllImport(LibraryName, EntryPoint = "ushim_context_copy_result_string_key_map_dynarray_value_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextCopyResultStringKeyMapDynamicArrayValueData(
        ref FunctionContext function,
        int entryIndex,
        IntPtr buffer,
        int size,
        int elementSize);

    [DllImport(LibraryName, EntryPoint = "ushim_context_copy_result_string_key_map_string_array_value_entries", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextCopyResultStringKeyMapStringArrayValueEntries(
        ref FunctionContext function,
        IntPtr keys,
        int keyCount,
        IntPtr lengths,
        int lengthCount);

    [DllImport(LibraryName, EntryPoint = "ushim_context_copy_result_string_key_map_string_array_value_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ContextCopyResultStringKeyMapStringArrayValueData(
        ref FunctionContext function,
        int entryIndex,
        IntPtr values,
        int valueCount);

    [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_string", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ContextGetResultString(ref FunctionContext function);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_argument_count", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetArgumentCount(IntPtr parameters);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind CallbackGetParameterKind(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParameterSize(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_item_count", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParameterItemCount(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_element_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind CallbackGetParameterElementKind(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_element_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParameterElementSize(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_element_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParameterElementHasReferences(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_element_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CallbackGetParameterElementTypeName(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_nested_element_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind CallbackGetParameterNestedElementKind(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_nested_element_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParameterNestedElementSize(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_nested_element_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParameterNestedElementHasReferences(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_nested_element_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CallbackGetParameterNestedElementTypeName(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_map_key_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind CallbackGetParameterMapKeyKind(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_map_key_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParameterMapKeySize(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_map_key_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParameterMapKeyHasReferences(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_map_key_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CallbackGetParameterMapKeyTypeName(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_map_value_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind CallbackGetParameterMapValueKind(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_map_value_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParameterMapValueSize(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_map_value_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParameterMapValueHasReferences(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_map_value_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CallbackGetParameterMapValueTypeName(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_map_value_element_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind CallbackGetParameterMapValueElementKind(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_map_value_element_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParameterMapValueElementSize(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_map_value_element_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParameterMapValueElementHasReferences(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_map_value_element_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CallbackGetParameterMapValueElementTypeName(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_is_variadic_parameter_list", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParameterIsVariadicParameterList(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParameterHasReferences(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CallbackGetParameterTypeName(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_is_enum", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParameterIsEnum(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_enum_member_count", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParameterEnumMemberCount(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_enum_member_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CallbackGetParameterEnumMemberName(IntPtr parameters, int parameterIndex, int memberIndex);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_enum_member_signed_value", CallingConvention = CallingConvention.Cdecl)]
    internal static extern long CallbackGetParameterEnumMemberSignedValue(IntPtr parameters, int parameterIndex, int memberIndex);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_parameter_enum_member_unsigned_value", CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong CallbackGetParameterEnumMemberUnsignedValue(IntPtr parameters, int parameterIndex, int memberIndex);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind CallbackGetResultKind(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetResultSize(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_item_count", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetResultItemCount(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_element_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind CallbackGetResultElementKind(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_element_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetResultElementSize(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_element_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetResultElementHasReferences(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_element_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CallbackGetResultElementTypeName(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_nested_element_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind CallbackGetResultNestedElementKind(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_nested_element_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetResultNestedElementSize(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_nested_element_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetResultNestedElementHasReferences(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_nested_element_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CallbackGetResultNestedElementTypeName(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_map_key_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind CallbackGetResultMapKeyKind(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_map_key_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetResultMapKeySize(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_map_key_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetResultMapKeyHasReferences(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_map_key_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CallbackGetResultMapKeyTypeName(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_map_value_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind CallbackGetResultMapValueKind(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_map_value_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetResultMapValueSize(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_map_value_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetResultMapValueHasReferences(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_map_value_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CallbackGetResultMapValueTypeName(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_map_value_element_kind", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeUmkaTypeKind CallbackGetResultMapValueElementKind(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_map_value_element_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetResultMapValueElementSize(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_map_value_element_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetResultMapValueElementHasReferences(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_map_value_element_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CallbackGetResultMapValueElementTypeName(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_is_variadic_parameter_list", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetResultIsVariadicParameterList(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_has_references", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetResultHasReferences(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_type_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CallbackGetResultTypeName(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_is_enum", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetResultIsEnum(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_enum_member_count", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetResultEnumMemberCount(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_enum_member_name", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CallbackGetResultEnumMemberName(IntPtr parameters, IntPtr result, int memberIndex);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_enum_member_signed_value", CallingConvention = CallingConvention.Cdecl)]
    internal static extern long CallbackGetResultEnumMemberSignedValue(IntPtr parameters, IntPtr result, int memberIndex);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_result_enum_member_unsigned_value", CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong CallbackGetResultEnumMemberUnsignedValue(IntPtr parameters, IntPtr result, int memberIndex);

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

    [DllImport(LibraryName, EntryPoint = "ushim_callback_retain_param", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackRetainParam(IntPtr parameters, IntPtr result, int index, out IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_param_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParamData(IntPtr parameters, int index, IntPtr buffer, int size);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_param_dynarray_length", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParamDynamicArrayLength(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_copy_param_dynarray_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackCopyParamDynamicArrayData(IntPtr parameters, int index, IntPtr buffer, int size);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_param_dynarray_string", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParamDynamicArrayString(
        IntPtr parameters,
        int index,
        int elementIndex,
        out IntPtr value);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_param_nested_dynarray_length", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParamNestedDynamicArrayLength(IntPtr parameters, int index, int elementIndex);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_copy_param_nested_dynarray_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackCopyParamNestedDynamicArrayData(
        IntPtr parameters,
        int index,
        int elementIndex,
        IntPtr buffer,
        int size,
        int elementSize);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_param_nested_string_array_length", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParamNestedStringArrayLength(IntPtr parameters, int index, int elementIndex);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_copy_param_nested_string_array_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackCopyParamNestedStringArrayData(
        IntPtr parameters,
        int index,
        int elementIndex,
        IntPtr values,
        int valueCount);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_get_param_map_count", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackGetParamMapCount(IntPtr parameters, int index);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_copy_param_map_entries", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackCopyParamMapEntries(
        IntPtr parameters,
        int index,
        IntPtr keys,
        int keyBytes,
        IntPtr values,
        int valueBytes);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_copy_param_string_key_map_entries", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackCopyParamStringKeyMapEntries(
        IntPtr parameters,
        int index,
        IntPtr keys,
        int keyCount,
        IntPtr values,
        int valueBytes);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_copy_param_string_value_map_entries", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackCopyParamStringValueMapEntries(
        IntPtr parameters,
        int index,
        IntPtr keys,
        int keyBytes,
        IntPtr values,
        int valueCount);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_copy_param_string_map_entries", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackCopyParamStringMapEntries(
        IntPtr parameters,
        int index,
        IntPtr keys,
        int keyCount,
        IntPtr values,
        int valueCount);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_copy_param_map_dynarray_value_entries", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackCopyParamMapDynamicArrayValueEntries(
        IntPtr parameters,
        int index,
        IntPtr keys,
        int keyBytes,
        IntPtr lengths,
        int lengthCount,
        int elementSize);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_copy_param_map_dynarray_value_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackCopyParamMapDynamicArrayValueData(
        IntPtr parameters,
        int index,
        int entryIndex,
        IntPtr buffer,
        int size,
        int elementSize);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_copy_param_map_string_array_value_entries", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackCopyParamMapStringArrayValueEntries(
        IntPtr parameters,
        int index,
        IntPtr keys,
        int keyBytes,
        IntPtr lengths,
        int lengthCount);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_copy_param_map_string_array_value_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackCopyParamMapStringArrayValueData(
        IntPtr parameters,
        int index,
        int entryIndex,
        IntPtr values,
        int valueCount);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_copy_param_string_key_map_dynarray_value_entries", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackCopyParamStringKeyMapDynamicArrayValueEntries(
        IntPtr parameters,
        int index,
        IntPtr keys,
        int keyCount,
        IntPtr lengths,
        int lengthCount,
        int elementSize);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_copy_param_string_key_map_dynarray_value_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackCopyParamStringKeyMapDynamicArrayValueData(
        IntPtr parameters,
        int index,
        int entryIndex,
        IntPtr buffer,
        int size,
        int elementSize);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_copy_param_string_key_map_string_array_value_entries", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackCopyParamStringKeyMapStringArrayValueEntries(
        IntPtr parameters,
        int index,
        IntPtr keys,
        int keyCount,
        IntPtr lengths,
        int lengthCount);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_copy_param_string_key_map_string_array_value_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackCopyParamStringKeyMapStringArrayValueData(
        IntPtr parameters,
        int index,
        int entryIndex,
        IntPtr values,
        int valueCount);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_int", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CallbackSetResultInt(IntPtr parameters, IntPtr result, long value);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_uint", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CallbackSetResultUInt(IntPtr parameters, IntPtr result, ulong value);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_real", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CallbackSetResultReal(IntPtr parameters, IntPtr result, double value);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_ptr", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CallbackSetResultPointer(IntPtr parameters, IntPtr result, IntPtr value);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_string", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern void CallbackSetResultString(
        IntPtr parameters,
        IntPtr result,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? value);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_any_null", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackSetResultAnyNull(IntPtr parameters, IntPtr result);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_any_int", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackSetResultAnyInt(IntPtr parameters, IntPtr result, long value);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_any_uint", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackSetResultAnyUInt(IntPtr parameters, IntPtr result, ulong value);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_any_char", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackSetResultAnyChar(IntPtr parameters, IntPtr result, ulong value);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_any_real", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackSetResultAnyReal(IntPtr parameters, IntPtr result, double value);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_any_bool", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackSetResultAnyBool(IntPtr parameters, IntPtr result, int value);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_any_string", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int CallbackSetResultAnyString(
        IntPtr parameters,
        IntPtr result,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? value);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_any_native_value", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackSetResultAnyNativeValue(IntPtr parameters, IntPtr result, IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_data", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackSetResultData(IntPtr parameters, IntPtr result, IntPtr value, int size);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_native_value", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackSetResultNativeValue(IntPtr parameters, IntPtr result, IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_dynarray", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackSetResultDynamicArray(
        IntPtr parameters,
        IntPtr result,
        IntPtr value,
        int length,
        int elementSize);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_dynarray_strings", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackSetResultStringDynamicArray(
        IntPtr parameters,
        IntPtr result,
        IntPtr values,
        int length);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_nested_dynarray", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackSetResultNestedDynamicArray(
        IntPtr parameters,
        IntPtr result,
        IntPtr lengths,
        int lengthCount,
        IntPtr values,
        int valueByteCount,
        int elementSize);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_nested_dynarray_strings", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackSetResultNestedStringArray(
        IntPtr parameters,
        IntPtr result,
        IntPtr lengths,
        int lengthCount,
        IntPtr values,
        int valueCount);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_map", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackSetResultMap(
        IntPtr parameters,
        IntPtr result,
        IntPtr keys,
        int keyBytes,
        IntPtr values,
        int valueBytes);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_string_key_map", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackSetResultStringKeyMap(
        IntPtr parameters,
        IntPtr result,
        IntPtr keys,
        int keyCount,
        IntPtr values,
        int valueBytes);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_string_value_map", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackSetResultStringValueMap(
        IntPtr parameters,
        IntPtr result,
        IntPtr keys,
        int keyBytes,
        IntPtr values,
        int valueCount);

    [DllImport(LibraryName, EntryPoint = "ushim_callback_set_result_string_map", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CallbackSetResultStringMap(
        IntPtr parameters,
        IntPtr result,
        IntPtr keys,
        int keyCount,
        IntPtr values,
        int valueCount);

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
#pragma warning restore CA2101
