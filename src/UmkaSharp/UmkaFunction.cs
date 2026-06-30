namespace UmkaSharp;

using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>A compiled Umka function callable from C#.</summary>
public sealed class UmkaFunction
{
    private readonly UmkaRuntime _runtime;
    private readonly UmkaTypeInfo[] _parameterTypes;
    private readonly ReadOnlyCollection<UmkaTypeInfo> _publicParameterTypes;
    private readonly NativeUmkaTypeKind[] _nativeParameterKinds;
    private readonly NativeUmkaTypeKind[] _nativeParameterElementKinds;
    private NativeMethods.FunctionContext _context;

    internal UmkaFunction(
        UmkaRuntime runtime,
        string name,
        string? moduleName,
        NativeMethods.FunctionContext context,
        UmkaTypeInfo[] parameterTypes,
        NativeUmkaTypeKind[] nativeParameterKinds,
        NativeUmkaTypeKind[] nativeParameterElementKinds,
        int requiredParameterCount,
        int defaultParameterCount,
        UmkaTypeInfo resultType)
    {
        _runtime = runtime;
        _parameterTypes = parameterTypes;
        _publicParameterTypes = Array.AsReadOnly(parameterTypes);
        _nativeParameterKinds = nativeParameterKinds;
        _nativeParameterElementKinds = nativeParameterElementKinds;
        RequiredParameterCount = IsVariadicFunction(parameterTypes) ? parameterTypes.Length - 1 : requiredParameterCount;
        DefaultParameterCount = defaultParameterCount;
        ResultType = resultType;
        Name = name;
        ModuleName = moduleName;
        QualifiedName = moduleName is null ? name : $"{moduleName}::{name}";
        _context = context;
    }

    /// <summary>Gets the function name used to resolve this callable.</summary>
    public string Name { get; }

    /// <summary>Gets the module file name used to resolve this callable, or <see langword="null" /> for root-source functions.</summary>
    public string? ModuleName { get; }

    /// <summary>Gets the root or module-qualified function identity used in diagnostics.</summary>
    public string QualifiedName { get; }

    /// <summary>Gets the number of explicit arguments expected by this function.</summary>
    public int ParameterCount => _parameterTypes.Length;

    /// <summary>Gets the minimum number of arguments required when trailing Umka default parameters are omitted.</summary>
    public int RequiredParameterCount { get; }

    /// <summary>Gets the number of trailing Umka parameters that declare default values.</summary>
    public int DefaultParameterCount { get; }

    /// <summary>Gets the explicit parameter types expected by this function.</summary>
    public IReadOnlyList<UmkaTypeInfo> ParameterTypes => _publicParameterTypes;

    /// <summary>Gets the function result type.</summary>
    public UmkaTypeInfo ResultType { get; }

    private string DiagnosticName => QualifiedName;

    private bool HasVariadicParameter => IsVariadicFunction(_parameterTypes);

    /// <summary>Returns a diagnostic string that includes the function name, arity, and result type.</summary>
    public override string ToString() =>
        $"UmkaFunction({DiagnosticName}, Parameters={ParameterCount}, Result={ResultType.TypeName})";

    /// <summary>Returns whether the result metadata can be read as a dynamic value without executing the function.</summary>
    public bool CanReadResultAsValue() => ResultType.CanReadAsValue();

    /// <summary>Returns whether the result metadata can be read as a supported scalar, string, pointer, enum, or dynamic value without executing the function.</summary>
    public bool CanReadResultAsScalar<T>() => ResultType.CanReadAsScalar<T>();

    /// <summary>Returns whether the result metadata can be read into the managed struct type without executing the function.</summary>
    public bool CanReadResultAsStruct<T>() where T : struct => ResultType.CanReadAsFixedLayout<T>();

    /// <summary>Returns whether the result metadata can be read into a managed array of the given length without executing the function.</summary>
    public bool CanReadResultAsArray<TElement>(int length) where TElement : struct =>
        ResultType.CanReadAsArray<TElement>(length);

    /// <summary>Returns whether the result metadata can be copied into a managed dynamic array without executing the function.</summary>
    public bool CanReadResultAsDynamicArray<TElement>() where TElement : struct =>
        ResultType.CanReadAsDynamicArray<TElement>();

    /// <summary>Returns whether the result metadata can be copied into a managed jagged array without executing the function.</summary>
    public bool CanReadResultAsNestedDynamicArray<TElement>() where TElement : struct =>
        ResultType.CanReadAsNestedDynamicArray<TElement>();

    /// <summary>Returns whether the result metadata can be copied into a managed jagged string array without executing the function.</summary>
    public bool CanReadResultAsNestedStringArray() => ResultType.CanReadAsNestedStringArray();

    /// <summary>Returns whether the result metadata can be copied into a managed string array without executing the function.</summary>
    public bool CanReadResultAsStringArray() => ResultType.CanReadAsStringArray();

    /// <summary>Returns whether the result metadata can be copied into a managed dictionary with string-array values without executing the function.</summary>
    public bool CanReadResultAsStringArrayValueMap<TKey>() where TKey : struct =>
        ResultType.CanReadAsStringArrayValueMap<TKey>();

    /// <summary>Returns whether the result metadata can be copied into a managed dictionary without executing the function.</summary>
    public bool CanReadResultAsMap<TKey, TValue>()
        where TKey : struct
        where TValue : struct =>
        ResultType.CanReadAsMap<TKey, TValue>();

    /// <summary>Returns whether the result metadata can be copied into a managed dictionary with string keys without executing the function.</summary>
    public bool CanReadResultAsStringKeyMap<TValue>() where TValue : struct =>
        ResultType.CanReadAsStringKeyMap<TValue>();

    /// <summary>Returns whether the result metadata can be copied into a managed dictionary with string values without executing the function.</summary>
    public bool CanReadResultAsStringValueMap<TKey>() where TKey : struct =>
        ResultType.CanReadAsStringValueMap<TKey>();

    /// <summary>Returns whether the result metadata can be copied into a managed dictionary with string keys and string values without executing the function.</summary>
    public bool CanReadResultAsStringMap() => ResultType.CanReadAsStringMap();

    /// <summary>Returns whether the result metadata can be copied into a managed dictionary with dynamic-array values without executing the function.</summary>
    public bool CanReadResultAsDynamicArrayValueMap<TKey, TElement>()
        where TKey : struct
        where TElement : struct =>
        ResultType.CanReadAsDynamicArrayValueMap<TKey, TElement>();

    /// <summary>Returns whether the result metadata can be copied into a managed dictionary with string keys and dynamic-array values without executing the function.</summary>
    public bool CanReadResultAsStringKeyDynamicArrayValueMap<TElement>() where TElement : struct =>
        ResultType.CanReadAsStringKeyDynamicArrayValueMap<TElement>();

    /// <summary>Returns whether the result metadata can be copied into a managed dictionary with string keys and string-array values without executing the function.</summary>
    public bool CanReadResultAsStringKeyStringArrayValueMap() =>
        ResultType.CanReadAsStringKeyStringArrayValueMap();

    /// <summary>Returns whether the result metadata can be read as an opaque Umka weak pointer handle without executing the function.</summary>
    public bool CanReadResultAsWeakPointer() => ResultType.CanReadAsWeakPointer();

    /// <summary>Returns whether the supplied arguments match the resolved function parameters without executing the function.</summary>
    public bool CanCallWith(params UmkaValue[] arguments) => CanCallWith(ToArgumentSpan(arguments));

    /// <summary>Returns whether the supplied arguments match the resolved function parameters without executing the function.</summary>
    public bool CanCallWith(ReadOnlySpan<UmkaValue> arguments)
    {
        try
        {
            var boundArguments = BindVariadicArguments(arguments);
            return boundArguments is null
                ? CanCallWithBoundArguments(arguments)
                : CanCallWithBoundArguments(boundArguments);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>Calls the function and returns a dynamic value for supported scalar, string, pointer, weak pointer, or void results.</summary>
    public UmkaValue CallValue(params UmkaValue[] arguments) => CallValue(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns a dynamic value for supported scalar, string, pointer, weak pointer, or void results.</summary>
    public UmkaValue CallValue(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        return ResultType.Kind switch
        {
            UmkaTypeKind.Void => CallVoidAndReturnValue(arguments),
            UmkaTypeKind.SignedInteger => UmkaValue.From(CallInt64(arguments)),
            UmkaTypeKind.UnsignedInteger => UmkaValue.From(CallUInt64(arguments)),
            UmkaTypeKind.Real => UmkaValue.From(CallDouble(arguments)),
            UmkaTypeKind.Boolean => UmkaValue.From(CallBoolean(arguments)),
            UmkaTypeKind.Character => UmkaValue.From(CallChar(arguments)),
            UmkaTypeKind.String => UmkaValue.From(CallString(arguments)),
            UmkaTypeKind.Pointer => UmkaValue.FromPointer(CallPointer(arguments)),
            UmkaTypeKind.WeakPointer => UmkaValue.FromWeakPointer(CallWeakPointer(arguments)),
            _ => throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka type '{ResultType.TypeName}', which cannot be read as a dynamic UmkaValue.")
        };
    }

    /// <summary>Tries to call the function and read a dynamic value for supported scalar, string, pointer, weak pointer, or void results.</summary>
    public bool TryCallValue(ReadOnlySpan<UmkaValue> arguments, out UmkaValue value)
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsValue())
        {
            value = default;
            return false;
        }

        try
        {
            value = CallValue(arguments);
            return true;
        }
        catch (OverflowException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Tries to call the function and read a dynamic value for supported scalar, string, pointer, weak pointer, or void results.</summary>
    public bool TryCallValue(out UmkaValue value, params UmkaValue[] arguments) =>
        TryCallValue(ToArgumentSpan(arguments), out value);

    /// <summary>Calls the function and returns its result as a supported scalar, string, pointer, enum, or dynamic value.</summary>
    [return: MaybeNull]
    public T CallScalar<T>(params UmkaValue[] arguments) => CallScalar<T>(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns its result as a supported scalar, string, pointer, enum, or dynamic value.</summary>
    [return: MaybeNull]
    public T CallScalar<T>(ReadOnlySpan<UmkaValue> arguments)
    {
        var targetType = typeof(T);
        if (targetType == typeof(UmkaValue))
            return BoxScalar<T>(CallValue(arguments));
        if (targetType == typeof(sbyte))
            return BoxScalar<T>(CallSByte(arguments));
        if (targetType == typeof(short))
            return BoxScalar<T>(CallInt16(arguments));
        if (targetType == typeof(int))
            return BoxScalar<T>(CallInt32(arguments));
        if (targetType == typeof(long))
            return BoxScalar<T>(CallInt64(arguments));
        if (targetType == typeof(byte))
            return BoxScalar<T>(CallByte(arguments));
        if (targetType == typeof(ushort))
            return BoxScalar<T>(CallUInt16(arguments));
        if (targetType == typeof(uint))
            return BoxScalar<T>(CallUInt32(arguments));
        if (targetType == typeof(ulong))
            return BoxScalar<T>(CallUInt64(arguments));
        if (targetType == typeof(float))
            return BoxScalar<T>(CallSingle(arguments));
        if (targetType == typeof(double))
            return BoxScalar<T>(CallDouble(arguments));
        if (targetType == typeof(bool))
            return BoxScalar<T>(CallBoolean(arguments));
        if (targetType == typeof(char))
            return BoxScalar<T>(CallChar(arguments));
        if (targetType == typeof(string))
            return BoxScalar<T>(CallString(arguments));
        if (targetType == typeof(IntPtr))
            return BoxScalar<T>(CallPointer(arguments));
        if (targetType.IsEnum)
            return CallEnumScalar<T>(arguments);

        throw new NotSupportedException(
            $"CallScalar<T>() does not support result type {targetType.FullName}. Use an explicit result reader such as CallStruct<T>(), CallArray<TElement>(), CallHostObject<T>(), or CallValue().");
    }

    /// <summary>Tries to call the function and read its result as a supported scalar, string, pointer, enum, or dynamic value.</summary>
    public bool TryCallScalar<T>(ReadOnlySpan<UmkaValue> arguments, [MaybeNull] out T value)
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsScalar<T>())
        {
            value = default;
            return false;
        }

        try
        {
            value = CallScalar<T>(arguments);
            return true;
        }
        catch (OverflowException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Tries to call the function and read its result as a supported scalar, string, pointer, enum, or dynamic value.</summary>
    public bool TryCallScalar<T>([MaybeNull] out T value, params UmkaValue[] arguments) =>
        TryCallScalar(ToArgumentSpan(arguments), out value);

    /// <summary>Calls the function and ignores any result value.</summary>
    public void CallVoid(params UmkaValue[] arguments) => CallVoid(ToArgumentSpan(arguments));

    /// <summary>Calls the function and ignores any result value.</summary>
    public void CallVoid(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        if (ResultType.Kind is UmkaTypeKind.StaticArray or UmkaTypeKind.Struct or UmkaTypeKind.DynamicArray or UmkaTypeKind.Map)
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka type '{ResultType.TypeName}', which must be read with a structured, dynamic-array, nested-array, string-array, or map result reader.");

        Invoke(arguments);
    }

    /// <summary>Tries to call the function and ignore any scalar or void result value.</summary>
    public bool TryCallVoid(params UmkaValue[] arguments) => TryCallVoid(ToArgumentSpan(arguments));

    /// <summary>Tries to call the function and ignore any scalar or void result value.</summary>
    public bool TryCallVoid(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (ResultType.Kind is UmkaTypeKind.StaticArray or UmkaTypeKind.Struct or UmkaTypeKind.DynamicArray or UmkaTypeKind.Map)
            return false;

        Invoke(arguments);
        return true;
    }

    private UmkaValue CallVoidAndReturnValue(ReadOnlySpan<UmkaValue> arguments)
    {
        CallVoid(arguments);
        return UmkaValue.Void;
    }

    /// <summary>Calls the function and returns its result as a signed integer.</summary>
    public long CallInt64(params UmkaValue[] arguments) => CallInt64(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns its result as a signed integer.</summary>
    public long CallInt64(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        EnsureResultKind(UmkaTypeKind.SignedInteger, UmkaTypeKind.Character);
        Invoke(arguments);
        return NativeMethods.ContextGetResultInt(ref _context);
    }

    /// <summary>Calls the function and returns its result as an 8-bit signed integer.</summary>
    public sbyte CallSByte(params UmkaValue[] arguments) => CallSByte(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns its result as an 8-bit signed integer.</summary>
    public sbyte CallSByte(ReadOnlySpan<UmkaValue> arguments) => checked((sbyte)CallInt64(arguments));

    /// <summary>Calls the function and returns its result as a 16-bit signed integer.</summary>
    public short CallInt16(params UmkaValue[] arguments) => CallInt16(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns its result as a 16-bit signed integer.</summary>
    public short CallInt16(ReadOnlySpan<UmkaValue> arguments) => checked((short)CallInt64(arguments));

    /// <summary>Calls the function and returns its result as a 32-bit signed integer.</summary>
    public int CallInt32(params UmkaValue[] arguments) => CallInt32(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns its result as a 32-bit signed integer.</summary>
    public int CallInt32(ReadOnlySpan<UmkaValue> arguments) => checked((int)CallInt64(arguments));

    /// <summary>Calls the function and returns its result as an unsigned integer.</summary>
    public ulong CallUInt64(params UmkaValue[] arguments) => CallUInt64(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns its result as an unsigned integer.</summary>
    public ulong CallUInt64(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        EnsureResultKind(UmkaTypeKind.UnsignedInteger, UmkaTypeKind.Character);
        Invoke(arguments);
        return NativeMethods.ContextGetResultUInt(ref _context);
    }

    /// <summary>Calls the function and returns its result as an 8-bit unsigned integer.</summary>
    public byte CallByte(params UmkaValue[] arguments) => CallByte(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns its result as an 8-bit unsigned integer.</summary>
    public byte CallByte(ReadOnlySpan<UmkaValue> arguments) => checked((byte)CallUInt64(arguments));

    /// <summary>Calls the function and returns its result as a 16-bit unsigned integer.</summary>
    public ushort CallUInt16(params UmkaValue[] arguments) => CallUInt16(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns its result as a 16-bit unsigned integer.</summary>
    public ushort CallUInt16(ReadOnlySpan<UmkaValue> arguments) => checked((ushort)CallUInt64(arguments));

    /// <summary>Calls the function and returns its result as a 32-bit unsigned integer.</summary>
    public uint CallUInt32(params UmkaValue[] arguments) => CallUInt32(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns its result as a 32-bit unsigned integer.</summary>
    public uint CallUInt32(ReadOnlySpan<UmkaValue> arguments) => checked((uint)CallUInt64(arguments));

    /// <summary>Calls the function and returns its result as an opaque Umka weak pointer handle.</summary>
    public ulong CallWeakPointer(params UmkaValue[] arguments) => CallWeakPointer(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns its result as an opaque Umka weak pointer handle.</summary>
    public ulong CallWeakPointer(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        EnsureResultKind(UmkaTypeKind.WeakPointer);
        Invoke(arguments);
        return NativeMethods.ContextGetResultUInt(ref _context);
    }

    /// <summary>Tries to call the function and read its result as an opaque Umka weak pointer handle.</summary>
    public bool TryCallWeakPointer(ReadOnlySpan<UmkaValue> arguments, out ulong value)
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsWeakPointer())
        {
            value = default;
            return false;
        }

        value = CallWeakPointer(arguments);
        return true;
    }

    /// <summary>Tries to call the function and read its result as an opaque Umka weak pointer handle.</summary>
    public bool TryCallWeakPointer(out ulong value, params UmkaValue[] arguments) =>
        TryCallWeakPointer(ToArgumentSpan(arguments), out value);

    /// <summary>Calls the function and returns its result as a character.</summary>
    public char CallChar(params UmkaValue[] arguments) => CallChar(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns its result as a character.</summary>
    public char CallChar(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        EnsureResultKind(UmkaTypeKind.Character);
        Invoke(arguments);

        var value = NativeMethods.ContextGetResultUInt(ref _context);
        if (value > byte.MaxValue)
            throw new OverflowException($"Function '{DiagnosticName}' returned character value {value}, which is outside the Umka char range.");

        return (char)value;
    }

    /// <summary>Calls the function and returns its result as an enum through the enum's underlying signed or unsigned storage.</summary>
    public TEnum CallEnum<TEnum>(params UmkaValue[] arguments) where TEnum : struct, Enum =>
        CallEnum<TEnum>(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns its result as an enum through the enum's underlying signed or unsigned storage.</summary>
    public TEnum CallEnum<TEnum>(ReadOnlySpan<UmkaValue> arguments) where TEnum : struct, Enum =>
        UmkaEnumConversion.IsUnsigned<TEnum>()
            ? UmkaEnumConversion.ToEnum<TEnum>(CallUInt64(arguments))
            : UmkaEnumConversion.ToEnum<TEnum>(CallInt64(arguments));

    /// <summary>Tries to call the function and read its result as an enum through the enum's underlying signed or unsigned storage.</summary>
    public bool TryCallEnum<TEnum>(ReadOnlySpan<UmkaValue> arguments, out TEnum value) where TEnum : struct, Enum
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsScalar<TEnum>())
        {
            value = default;
            return false;
        }

        try
        {
            value = CallEnum<TEnum>(arguments);
            return true;
        }
        catch (OverflowException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Tries to call the function and read its result as an enum through the enum's underlying signed or unsigned storage.</summary>
    public bool TryCallEnum<TEnum>(out TEnum value, params UmkaValue[] arguments) where TEnum : struct, Enum =>
        TryCallEnum(ToArgumentSpan(arguments), out value);

    /// <summary>Calls the function and returns its result as a real number.</summary>
    public double CallDouble(params UmkaValue[] arguments) => CallDouble(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns its result as a real number.</summary>
    public double CallDouble(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        EnsureResultKind(UmkaTypeKind.Real);
        Invoke(arguments);
        return NativeMethods.ContextGetResultReal(ref _context);
    }

    /// <summary>Calls the function and returns its result as a single-precision real number.</summary>
    public float CallSingle(params UmkaValue[] arguments) => CallSingle(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns its result as a single-precision real number.</summary>
    public float CallSingle(ReadOnlySpan<UmkaValue> arguments) =>
        UmkaSingleConversion.ToSingleChecked(
            CallDouble(arguments),
            $"Function '{DiagnosticName}' returned real value");

    /// <summary>Calls the function and returns its result as a Boolean.</summary>
    public bool CallBoolean(params UmkaValue[] arguments) => CallBoolean(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns its result as a Boolean.</summary>
    public bool CallBoolean(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        EnsureResultKind(UmkaTypeKind.Boolean);
        Invoke(arguments);
        return NativeMethods.ContextGetResultInt(ref _context) != 0;
    }

    /// <summary>Calls the function and returns its result as a string.</summary>
    public string? CallString(params UmkaValue[] arguments) => CallString(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns its result as a string.</summary>
    public string? CallString(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        EnsureResultKind(UmkaTypeKind.String);
        Invoke(arguments);
        return NativeMethods.ContextGetResultString(ref _context).ToManagedString();
    }

    /// <summary>Calls the function and returns its result as a pointer.</summary>
    public IntPtr CallPointer(params UmkaValue[] arguments) => CallPointer(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns its result as a pointer.</summary>
    public IntPtr CallPointer(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        EnsureResultKind(UmkaTypeKind.Pointer);
        Invoke(arguments);
        return NativeMethods.ContextGetResultPointer(ref _context);
    }

    /// <summary>Calls the function, reads a runtime-owned host handle pointer, and returns its managed target object.</summary>
    public T CallHostObject<T>(params UmkaValue[] arguments) => CallHostObject<T>(ToArgumentSpan(arguments));

    /// <summary>Calls the function, reads a runtime-owned host handle pointer, and returns its managed target object.</summary>
    public T CallHostObject<T>(ReadOnlySpan<UmkaValue> arguments) =>
        _runtime.GetHostObject<T>(CallPointer(arguments));

    /// <summary>Calls the function, reads a runtime-owned host handle pointer, and tries to resolve its managed target object.</summary>
    public bool TryCallHostObject<T>(ReadOnlySpan<UmkaValue> arguments, [NotNullWhen(true)] out T? target) =>
        _runtime.TryGetHostObject(CallPointer(arguments), out target);

    /// <summary>Calls the function, reads a runtime-owned host handle pointer, and tries to resolve its managed target object.</summary>
    public bool TryCallHostObject<T>([NotNullWhen(true)] out T? target, params UmkaValue[] arguments) =>
        TryCallHostObject(ToArgumentSpan(arguments), out target);

    /// <summary>Calls the function and marshals a structured result into a managed struct.</summary>
    public T CallStruct<T>(params UmkaValue[] arguments) where T : struct =>
        CallStruct<T>(ToArgumentSpan(arguments));

    /// <summary>Calls the function and marshals a structured result into a managed struct.</summary>
    public T CallStruct<T>(ReadOnlySpan<UmkaValue> arguments) where T : struct
    {
        _runtime.CheckCallable();
        ValidateNoManagedReferences<T>();
        EnsureResultKind(UmkaTypeKind.StaticArray, UmkaTypeKind.Struct);
        EnsureReadableStructuredResult();

        var nativeSize = ResultType.NativeSize;
        if (nativeSize <= 0)
            throw new InvalidOperationException("The function does not return a structured value.");

        var managedSize = Marshal.SizeOf<T>();
        if (managedSize != nativeSize)
            throw new InvalidOperationException(
                $"Managed result type {typeof(T).FullName} is {managedSize} bytes, but Umka result type '{ResultType.TypeName}' is {nativeSize} bytes.");

        var buffer = Marshal.AllocHGlobal(managedSize);
        try
        {
            var status = NativeMethods.ContextSetResultBuffer(ref _context, buffer);
            _runtime.ThrowIfError(status);
            Invoke(arguments);
            return Marshal.PtrToStructure<T>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            _ = NativeMethods.ContextSetResultBuffer(ref _context, IntPtr.Zero);
        }
    }

    /// <summary>Tries to call the function and marshal a structured result into a managed struct.</summary>
    public bool TryCallStruct<T>(ReadOnlySpan<UmkaValue> arguments, out T value) where T : struct
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsFixedLayout<T>())
        {
            value = default;
            return false;
        }

        value = CallStruct<T>(arguments);
        return true;
    }

    /// <summary>Tries to call the function and marshal a structured result into a managed struct.</summary>
    public bool TryCallStruct<T>(out T value, params UmkaValue[] arguments) where T : struct =>
        TryCallStruct(ToArgumentSpan(arguments), out value);

    /// <summary>Calls the function and marshals a static array result into a managed array.</summary>
    public TElement[] CallArray<TElement>(int length, params UmkaValue[] arguments) where TElement : struct =>
        CallArray<TElement>(length, ToArgumentSpan(arguments));

    /// <summary>Calls the function and marshals a static array result into a managed array.</summary>
    public TElement[] CallArray<TElement>(int length, ReadOnlySpan<UmkaValue> arguments) where TElement : struct
    {
        _runtime.CheckCallable();
        ValidateNoManagedReferences<TElement>();
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        EnsureResultKind(UmkaTypeKind.StaticArray);
        EnsureReadableStructuredResult();

        var nativeSize = ResultType.NativeSize;
        if (nativeSize <= 0)
            throw new InvalidOperationException("The function does not return a static array value.");

        var nativeLength = ResultType.ItemCount;
        if (nativeLength > 0 && length != nativeLength)
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka type '{ResultType.TypeName}' with {nativeLength} item(s), but {length} item(s) were requested.");
        }

        var elementSize = Marshal.SizeOf<TElement>();
        var managedSize = checked(elementSize * length);
        if (managedSize != nativeSize)
            throw new InvalidOperationException(
                $"Managed array type {typeof(TElement).FullName}[{length}] is {managedSize} bytes, but Umka requires {nativeSize} bytes.");

        var buffer = Marshal.AllocHGlobal(nativeSize);
        try
        {
            var status = NativeMethods.ContextSetResultBuffer(ref _context, buffer);
            _runtime.ThrowIfError(status);
            Invoke(arguments);

            var result = new TElement[length];
            for (var i = 0; i < result.Length; i++)
                result[i] = Marshal.PtrToStructure<TElement>(IntPtr.Add(buffer, i * elementSize));
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            _ = NativeMethods.ContextSetResultBuffer(ref _context, IntPtr.Zero);
        }
    }

    /// <summary>Tries to call the function and marshal a static array result into a managed array.</summary>
    public bool TryCallArray<TElement>(
        int length,
        ReadOnlySpan<UmkaValue> arguments,
        [NotNullWhen(true)] out TElement[]? value)
        where TElement : struct
    {
        _runtime.CheckCallable();
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsArray<TElement>(length))
        {
            value = null;
            return false;
        }

        value = CallArray<TElement>(length, arguments);
        return true;
    }

    /// <summary>Tries to call the function and marshal a static array result into a managed array.</summary>
    public bool TryCallArray<TElement>(
        int length,
        [NotNullWhen(true)] out TElement[]? value,
        params UmkaValue[] arguments)
        where TElement : struct =>
        TryCallArray(length, ToArgumentSpan(arguments), out value);

    /// <summary>Calls the function and copies a dynamic array result into a managed array.</summary>
    public TElement[] CallDynamicArray<TElement>(params UmkaValue[] arguments) where TElement : struct =>
        CallDynamicArray<TElement>(ToArgumentSpan(arguments));

    /// <summary>Calls the function and copies a dynamic array result into a managed array.</summary>
    public TElement[] CallDynamicArray<TElement>(ReadOnlySpan<UmkaValue> arguments) where TElement : struct
    {
        _runtime.CheckCallable();
        ValidateNoManagedReferences<TElement>();
        EnsureResultKind(UmkaTypeKind.DynamicArray);
        EnsureReadableDynamicArrayResult<TElement>();

        var headerSize = ResultType.NativeSize;
        if (headerSize <= 0)
            throw new InvalidOperationException("The function does not return a dynamic array value with native header metadata.");

        var header = Marshal.AllocHGlobal(headerSize);
        try
        {
            Marshal.Copy(new byte[headerSize], 0, header, headerSize);
            var status = NativeMethods.ContextSetResultBuffer(ref _context, header);
            _runtime.ThrowIfError(status);
            Invoke(arguments);

            var length = NativeMethods.ContextGetResultDynamicArrayLength(ref _context);
            if (length < 0)
                throw new InvalidOperationException($"Function '{DiagnosticName}' did not return a readable dynamic array.");
            if (length == 0)
                return Array.Empty<TElement>();

            var elementSize = Marshal.SizeOf<TElement>();
            var byteSize = checked(length * elementSize);
            var buffer = Marshal.AllocHGlobal(byteSize);
            try
            {
                status = NativeMethods.ContextCopyResultDynamicArrayData(ref _context, buffer, byteSize);
                _runtime.ThrowIfError(status);

                var result = new TElement[length];
                for (var i = 0; i < result.Length; i++)
                    result[i] = Marshal.PtrToStructure<TElement>(IntPtr.Add(buffer, i * elementSize));
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = NativeMethods.ContextReleaseResultDynamicArray(_runtime.Handle, ref _context);
            Marshal.FreeHGlobal(header);
            _ = NativeMethods.ContextSetResultBuffer(ref _context, IntPtr.Zero);
        }
    }

    /// <summary>Calls the function and copies a <c>[]str</c> dynamic array result into a managed string array.</summary>
    public string?[] CallStringArray(params UmkaValue[] arguments) =>
        CallStringArray(ToArgumentSpan(arguments));

    /// <summary>Calls the function and copies a <c>[]str</c> dynamic array result into a managed string array.</summary>
    public string?[] CallStringArray(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        EnsureResultKind(UmkaTypeKind.DynamicArray);
        EnsureReadableStringDynamicArrayResult();

        var headerSize = ResultType.NativeSize;
        if (headerSize <= 0)
            throw new InvalidOperationException("The function does not return a dynamic array value with native header metadata.");

        var header = Marshal.AllocHGlobal(headerSize);
        try
        {
            Marshal.Copy(new byte[headerSize], 0, header, headerSize);
            var status = NativeMethods.ContextSetResultBuffer(ref _context, header);
            _runtime.ThrowIfError(status);
            Invoke(arguments);

            var length = NativeMethods.ContextGetResultDynamicArrayLength(ref _context);
            if (length < 0)
                throw new InvalidOperationException($"Function '{DiagnosticName}' did not return a readable dynamic array.");
            if (length == 0)
                return Array.Empty<string?>();

            var result = new string?[length];
            for (var i = 0; i < result.Length; i++)
            {
                status = NativeMethods.ContextGetResultDynamicArrayString(ref _context, i, out var value);
                _runtime.ThrowIfError(status);
                result[i] = value.ToManagedString();
            }

            return result;
        }
        finally
        {
            _ = NativeMethods.ContextReleaseResultDynamicArray(_runtime.Handle, ref _context);
            Marshal.FreeHGlobal(header);
            _ = NativeMethods.ContextSetResultBuffer(ref _context, IntPtr.Zero);
        }
    }

    /// <summary>Calls the function and copies a nested dynamic array result into a managed jagged array.</summary>
    public TElement[][] CallNestedDynamicArray<TElement>(params UmkaValue[] arguments) where TElement : struct =>
        CallNestedDynamicArray<TElement>(ToArgumentSpan(arguments));

    /// <summary>Calls the function and copies a nested dynamic array result into a managed jagged array.</summary>
    public TElement[][] CallNestedDynamicArray<TElement>(ReadOnlySpan<UmkaValue> arguments) where TElement : struct
    {
        _runtime.CheckCallable();
        ValidateNoManagedReferences<TElement>();
        EnsureResultKind(UmkaTypeKind.DynamicArray);
        EnsureReadableNestedDynamicArrayResult<TElement>();

        var headerSize = ResultType.NativeSize;
        if (headerSize <= 0)
            throw new InvalidOperationException("The function does not return a dynamic array value with native header metadata.");

        var header = Marshal.AllocHGlobal(headerSize);
        try
        {
            Marshal.Copy(new byte[headerSize], 0, header, headerSize);
            var status = NativeMethods.ContextSetResultBuffer(ref _context, header);
            _runtime.ThrowIfError(status);
            Invoke(arguments);

            var length = NativeMethods.ContextGetResultDynamicArrayLength(ref _context);
            if (length < 0)
                throw new InvalidOperationException($"Function '{DiagnosticName}' did not return a readable dynamic array.");
            if (length == 0)
                return Array.Empty<TElement[]>();

            var elementSize = Marshal.SizeOf<TElement>();
            var result = new TElement[length][];
            for (var i = 0; i < result.Length; i++)
            {
                var rowLength = NativeMethods.ContextGetResultNestedDynamicArrayLength(ref _context, i);
                if (rowLength < 0)
                    throw new InvalidOperationException($"Function '{DiagnosticName}' result row {i} could not be read as a nested dynamic array.");
                if (rowLength == 0)
                {
                    result[i] = Array.Empty<TElement>();
                    continue;
                }

                var byteSize = checked(rowLength * elementSize);
                var buffer = Marshal.AllocHGlobal(byteSize);
                try
                {
                    status = NativeMethods.ContextCopyResultNestedDynamicArrayData(ref _context, i, buffer, byteSize, elementSize);
                    _runtime.ThrowIfError(status);

                    var row = new TElement[rowLength];
                    for (var j = 0; j < row.Length; j++)
                        row[j] = Marshal.PtrToStructure<TElement>(IntPtr.Add(buffer, j * elementSize));
                    result[i] = row;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            return result;
        }
        finally
        {
            _ = NativeMethods.ContextReleaseResultDynamicArray(_runtime.Handle, ref _context);
            Marshal.FreeHGlobal(header);
            _ = NativeMethods.ContextSetResultBuffer(ref _context, IntPtr.Zero);
        }
    }

    /// <summary>Calls the function and copies a nested <c>[][]str</c> dynamic array result into a managed jagged string array.</summary>
    public string?[][] CallNestedStringArray(params UmkaValue[] arguments) =>
        CallNestedStringArray(ToArgumentSpan(arguments));

    /// <summary>Calls the function and copies a nested <c>[][]str</c> dynamic array result into a managed jagged string array.</summary>
    public string?[][] CallNestedStringArray(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        EnsureResultKind(UmkaTypeKind.DynamicArray);
        EnsureReadableNestedStringArrayResult();

        var headerSize = ResultType.NativeSize;
        if (headerSize <= 0)
            throw new InvalidOperationException("The function does not return a dynamic array value with native header metadata.");

        var header = Marshal.AllocHGlobal(headerSize);
        try
        {
            Marshal.Copy(new byte[headerSize], 0, header, headerSize);
            var status = NativeMethods.ContextSetResultBuffer(ref _context, header);
            _runtime.ThrowIfError(status);
            Invoke(arguments);

            var length = NativeMethods.ContextGetResultDynamicArrayLength(ref _context);
            if (length < 0)
                throw new InvalidOperationException($"Function '{DiagnosticName}' did not return a readable dynamic array.");
            if (length == 0)
                return Array.Empty<string?[]>();

            var result = new string?[length][];
            for (var i = 0; i < result.Length; i++)
                result[i] = CopyNestedStringArrayResultRow(i);

            return result;
        }
        finally
        {
            _ = NativeMethods.ContextReleaseResultDynamicArray(_runtime.Handle, ref _context);
            Marshal.FreeHGlobal(header);
            _ = NativeMethods.ContextSetResultBuffer(ref _context, IntPtr.Zero);
        }
    }

    /// <summary>Tries to call the function and copy a dynamic array result into a managed array.</summary>
    public bool TryCallDynamicArray<TElement>(
        ReadOnlySpan<UmkaValue> arguments,
        [NotNullWhen(true)] out TElement[]? value)
        where TElement : struct
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsDynamicArray<TElement>())
        {
            value = null;
            return false;
        }

        value = CallDynamicArray<TElement>(arguments);
        return true;
    }

    /// <summary>Tries to call the function and copy a dynamic array result into a managed array.</summary>
    public bool TryCallDynamicArray<TElement>(
        [NotNullWhen(true)] out TElement[]? value,
        params UmkaValue[] arguments)
        where TElement : struct =>
        TryCallDynamicArray(ToArgumentSpan(arguments), out value);

    /// <summary>Tries to call the function and copy a <c>[]str</c> dynamic array result into a managed string array.</summary>
    public bool TryCallStringArray(
        ReadOnlySpan<UmkaValue> arguments,
        [NotNullWhen(true)] out string?[]? value)
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsStringArray())
        {
            value = null;
            return false;
        }

        value = CallStringArray(arguments);
        return true;
    }

    /// <summary>Tries to call the function and copy a <c>[]str</c> dynamic array result into a managed string array.</summary>
    public bool TryCallStringArray(
        [NotNullWhen(true)] out string?[]? value,
        params UmkaValue[] arguments) =>
        TryCallStringArray(ToArgumentSpan(arguments), out value);

    /// <summary>Tries to call the function and copy a nested dynamic array result into a managed jagged array.</summary>
    public bool TryCallNestedDynamicArray<TElement>(
        ReadOnlySpan<UmkaValue> arguments,
        [NotNullWhen(true)] out TElement[][]? value)
        where TElement : struct
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsNestedDynamicArray<TElement>())
        {
            value = null;
            return false;
        }

        value = CallNestedDynamicArray<TElement>(arguments);
        return true;
    }

    /// <summary>Tries to call the function and copy a nested dynamic array result into a managed jagged array.</summary>
    public bool TryCallNestedDynamicArray<TElement>(
        [NotNullWhen(true)] out TElement[][]? value,
        params UmkaValue[] arguments)
        where TElement : struct =>
        TryCallNestedDynamicArray(ToArgumentSpan(arguments), out value);

    /// <summary>Tries to call the function and copy a nested <c>[][]str</c> dynamic array result into a managed jagged string array.</summary>
    public bool TryCallNestedStringArray(
        ReadOnlySpan<UmkaValue> arguments,
        [NotNullWhen(true)] out string?[][]? value)
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsNestedStringArray())
        {
            value = null;
            return false;
        }

        value = CallNestedStringArray(arguments);
        return true;
    }

    /// <summary>Tries to call the function and copy a nested <c>[][]str</c> dynamic array result into a managed jagged string array.</summary>
    public bool TryCallNestedStringArray(
        [NotNullWhen(true)] out string?[][]? value,
        params UmkaValue[] arguments) =>
        TryCallNestedStringArray(ToArgumentSpan(arguments), out value);

    private string?[] CopyNestedStringArrayResultRow(int index)
    {
        var rowLength = NativeMethods.ContextGetResultNestedStringArrayLength(ref _context, index);
        if (rowLength < 0)
            throw new InvalidOperationException($"Function '{DiagnosticName}' result row {index} could not be read as a nested string array.");
        if (rowLength == 0)
            return Array.Empty<string?>();

        var byteSize = checked(rowLength * IntPtr.Size);
        var buffer = Marshal.AllocHGlobal(byteSize);
        try
        {
            var status = NativeMethods.ContextCopyResultNestedStringArrayData(ref _context, index, buffer, rowLength);
            _runtime.ThrowIfError(status);

            var row = new string?[rowLength];
            for (var j = 0; j < row.Length; j++)
                row[j] = Marshal.ReadIntPtr(buffer, j * IntPtr.Size).ToManagedString();
            return row;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Calls the function and copies a map result into a managed dictionary.</summary>
    public Dictionary<TKey, TValue> CallMap<TKey, TValue>(params UmkaValue[] arguments)
        where TKey : struct
        where TValue : struct =>
        CallMap<TKey, TValue>(ToArgumentSpan(arguments));

    /// <summary>Calls the function and copies a map result into a managed dictionary.</summary>
    public Dictionary<TKey, TValue> CallMap<TKey, TValue>(ReadOnlySpan<UmkaValue> arguments)
        where TKey : struct
        where TValue : struct
    {
        _runtime.CheckCallable();
        ValidateNoManagedReferences<TKey>();
        ValidateNoManagedReferences<TValue>();
        EnsureResultKind(UmkaTypeKind.Map);
        EnsureReadableMapResult<TKey, TValue>();

        var headerSize = ResultType.NativeSize;
        if (headerSize <= 0)
            throw new InvalidOperationException("The function does not return a map value with native header metadata.");

        var header = Marshal.AllocHGlobal(headerSize);
        try
        {
            Marshal.Copy(new byte[headerSize], 0, header, headerSize);
            var status = NativeMethods.ContextSetResultBuffer(ref _context, header);
            _runtime.ThrowIfError(status);
            Invoke(arguments);

            var count = NativeMethods.ContextGetResultMapCount(ref _context);
            return UmkaMapCopy.Copy<TKey, TValue>(
                count,
                ResultType.MapKeyNativeSize,
                ResultType.MapValueNativeSize,
                (keys, keyBytes, values, valueBytes) =>
                    NativeMethods.ContextCopyResultMapEntries(ref _context, keys, keyBytes, values, valueBytes));
        }
        finally
        {
            Marshal.FreeHGlobal(header);
            _ = NativeMethods.ContextSetResultBuffer(ref _context, IntPtr.Zero);
        }
    }

    /// <summary>Tries to call the function and copy a map result into a managed dictionary.</summary>
    public bool TryCallMap<TKey, TValue>(
        ReadOnlySpan<UmkaValue> arguments,
        [NotNullWhen(true)] out Dictionary<TKey, TValue>? value)
        where TKey : struct
        where TValue : struct
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsMap<TKey, TValue>())
        {
            value = null;
            return false;
        }

        value = CallMap<TKey, TValue>(arguments);
        return true;
    }

    /// <summary>Tries to call the function and copy a map result into a managed dictionary.</summary>
    public bool TryCallMap<TKey, TValue>(
        [NotNullWhen(true)] out Dictionary<TKey, TValue>? value,
        params UmkaValue[] arguments)
        where TKey : struct
        where TValue : struct =>
        TryCallMap(ToArgumentSpan(arguments), out value);

    /// <summary>Calls the function and copies a map result with string keys into a managed dictionary.</summary>
    public Dictionary<string, TValue> CallStringKeyMap<TValue>(params UmkaValue[] arguments)
        where TValue : struct =>
        CallStringKeyMap<TValue>(ToArgumentSpan(arguments));

    /// <summary>Calls the function and copies a map result with string keys into a managed dictionary.</summary>
    public Dictionary<string, TValue> CallStringKeyMap<TValue>(ReadOnlySpan<UmkaValue> arguments)
        where TValue : struct
    {
        _runtime.CheckCallable();
        ValidateNoManagedReferences<TValue>();
        EnsureResultKind(UmkaTypeKind.Map);
        EnsureReadableStringKeyMapResult<TValue>();

        var headerSize = ResultType.NativeSize;
        if (headerSize <= 0)
            throw new InvalidOperationException("The function does not return a map value with native header metadata.");

        var header = Marshal.AllocHGlobal(headerSize);
        try
        {
            Marshal.Copy(new byte[headerSize], 0, header, headerSize);
            var status = NativeMethods.ContextSetResultBuffer(ref _context, header);
            _runtime.ThrowIfError(status);
            Invoke(arguments);

            var count = NativeMethods.ContextGetResultMapCount(ref _context);
            return UmkaMapCopy.CopyStringKeys<TValue>(
                count,
                ResultType.MapValueNativeSize,
                (keys, keyCount, values, valueBytes) =>
                    NativeMethods.ContextCopyResultStringKeyMapEntries(ref _context, keys, keyCount, values, valueBytes));
        }
        finally
        {
            Marshal.FreeHGlobal(header);
            _ = NativeMethods.ContextSetResultBuffer(ref _context, IntPtr.Zero);
        }
    }

    /// <summary>Tries to call the function and copy a map result with string keys into a managed dictionary.</summary>
    public bool TryCallStringKeyMap<TValue>(
        ReadOnlySpan<UmkaValue> arguments,
        [NotNullWhen(true)] out Dictionary<string, TValue>? value)
        where TValue : struct
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsStringKeyMap<TValue>())
        {
            value = null;
            return false;
        }

        value = CallStringKeyMap<TValue>(arguments);
        return true;
    }

    /// <summary>Tries to call the function and copy a map result with string keys into a managed dictionary.</summary>
    public bool TryCallStringKeyMap<TValue>(
        [NotNullWhen(true)] out Dictionary<string, TValue>? value,
        params UmkaValue[] arguments)
        where TValue : struct =>
        TryCallStringKeyMap(ToArgumentSpan(arguments), out value);

    /// <summary>Calls the function and copies a map result with string values into a managed dictionary.</summary>
    public Dictionary<TKey, string?> CallStringValueMap<TKey>(params UmkaValue[] arguments)
        where TKey : struct =>
        CallStringValueMap<TKey>(ToArgumentSpan(arguments));

    /// <summary>Calls the function and copies a map result with string values into a managed dictionary.</summary>
    public Dictionary<TKey, string?> CallStringValueMap<TKey>(ReadOnlySpan<UmkaValue> arguments)
        where TKey : struct
    {
        _runtime.CheckCallable();
        ValidateNoManagedReferences<TKey>();
        EnsureResultKind(UmkaTypeKind.Map);
        EnsureReadableStringValueMapResult<TKey>();

        var headerSize = ResultType.NativeSize;
        if (headerSize <= 0)
            throw new InvalidOperationException("The function does not return a map value with native header metadata.");

        var header = Marshal.AllocHGlobal(headerSize);
        try
        {
            Marshal.Copy(new byte[headerSize], 0, header, headerSize);
            var status = NativeMethods.ContextSetResultBuffer(ref _context, header);
            _runtime.ThrowIfError(status);
            Invoke(arguments);

            var count = NativeMethods.ContextGetResultMapCount(ref _context);
            return UmkaMapCopy.CopyStringValues<TKey>(
                count,
                ResultType.MapKeyNativeSize,
                (keys, keyBytes, values, valueCount) =>
                    NativeMethods.ContextCopyResultStringValueMapEntries(ref _context, keys, keyBytes, values, valueCount));
        }
        finally
        {
            Marshal.FreeHGlobal(header);
            _ = NativeMethods.ContextSetResultBuffer(ref _context, IntPtr.Zero);
        }
    }

    /// <summary>Tries to call the function and copy a map result with string values into a managed dictionary.</summary>
    public bool TryCallStringValueMap<TKey>(
        ReadOnlySpan<UmkaValue> arguments,
        [NotNullWhen(true)] out Dictionary<TKey, string?>? value)
        where TKey : struct
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsStringValueMap<TKey>())
        {
            value = null;
            return false;
        }

        value = CallStringValueMap<TKey>(arguments);
        return true;
    }

    /// <summary>Tries to call the function and copy a map result with string values into a managed dictionary.</summary>
    public bool TryCallStringValueMap<TKey>(
        [NotNullWhen(true)] out Dictionary<TKey, string?>? value,
        params UmkaValue[] arguments)
        where TKey : struct =>
        TryCallStringValueMap(ToArgumentSpan(arguments), out value);

    /// <summary>Calls the function and copies a map result with string keys and string values into a managed dictionary.</summary>
    public Dictionary<string, string?> CallStringMap(params UmkaValue[] arguments) =>
        CallStringMap(ToArgumentSpan(arguments));

    /// <summary>Calls the function and copies a map result with string keys and string values into a managed dictionary.</summary>
    public Dictionary<string, string?> CallStringMap(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        EnsureResultKind(UmkaTypeKind.Map);
        EnsureReadableStringMapResult();

        var headerSize = ResultType.NativeSize;
        if (headerSize <= 0)
            throw new InvalidOperationException("The function does not return a map value with native header metadata.");

        var header = Marshal.AllocHGlobal(headerSize);
        try
        {
            Marshal.Copy(new byte[headerSize], 0, header, headerSize);
            var status = NativeMethods.ContextSetResultBuffer(ref _context, header);
            _runtime.ThrowIfError(status);
            Invoke(arguments);

            var count = NativeMethods.ContextGetResultMapCount(ref _context);
            return UmkaMapCopy.CopyStrings(
                count,
                (keys, keyCount, values, valueCount) =>
                    NativeMethods.ContextCopyResultStringMapEntries(ref _context, keys, keyCount, values, valueCount));
        }
        finally
        {
            Marshal.FreeHGlobal(header);
            _ = NativeMethods.ContextSetResultBuffer(ref _context, IntPtr.Zero);
        }
    }

    /// <summary>Tries to call the function and copy a map result with string keys and string values into a managed dictionary.</summary>
    public bool TryCallStringMap(
        ReadOnlySpan<UmkaValue> arguments,
        [NotNullWhen(true)] out Dictionary<string, string?>? value)
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsStringMap())
        {
            value = null;
            return false;
        }

        value = CallStringMap(arguments);
        return true;
    }

    /// <summary>Tries to call the function and copy a map result with string keys and string values into a managed dictionary.</summary>
    public bool TryCallStringMap(
        [NotNullWhen(true)] out Dictionary<string, string?>? value,
        params UmkaValue[] arguments) =>
        TryCallStringMap(ToArgumentSpan(arguments), out value);

    /// <summary>Calls the function and copies a map result with dynamic-array values into a managed dictionary.</summary>
    public Dictionary<TKey, TElement[]> CallDynamicArrayValueMap<TKey, TElement>(params UmkaValue[] arguments)
        where TKey : struct
        where TElement : struct =>
        CallDynamicArrayValueMap<TKey, TElement>(ToArgumentSpan(arguments));

    /// <summary>Calls the function and copies a map result with dynamic-array values into a managed dictionary.</summary>
    public Dictionary<TKey, TElement[]> CallDynamicArrayValueMap<TKey, TElement>(ReadOnlySpan<UmkaValue> arguments)
        where TKey : struct
        where TElement : struct
    {
        _runtime.CheckCallable();
        ValidateNoManagedReferences<TKey>();
        ValidateNoManagedReferences<TElement>();
        EnsureResultKind(UmkaTypeKind.Map);
        EnsureReadableDynamicArrayValueMapResult<TKey, TElement>();

        var headerSize = ResultType.NativeSize;
        if (headerSize <= 0)
            throw new InvalidOperationException("The function does not return a map value with native header metadata.");

        var header = Marshal.AllocHGlobal(headerSize);
        try
        {
            Marshal.Copy(new byte[headerSize], 0, header, headerSize);
            var status = NativeMethods.ContextSetResultBuffer(ref _context, header);
            _runtime.ThrowIfError(status);
            Invoke(arguments);

            var count = NativeMethods.ContextGetResultMapCount(ref _context);
            return UmkaMapCopy.CopyDynamicArrayValues<TKey, TElement>(
                count,
                ResultType.MapKeyNativeSize,
                ResultType.MapValueElementNativeSize,
                (keys, keyBytes, lengths, lengthCount, elementSize) =>
                    NativeMethods.ContextCopyResultMapDynamicArrayValueEntries(ref _context, keys, keyBytes, lengths, lengthCount, elementSize),
                (entryIndex, buffer, byteSize, elementSize) =>
                    NativeMethods.ContextCopyResultMapDynamicArrayValueData(ref _context, entryIndex, buffer, byteSize, elementSize));
        }
        finally
        {
            Marshal.FreeHGlobal(header);
            _ = NativeMethods.ContextSetResultBuffer(ref _context, IntPtr.Zero);
        }
    }

    /// <summary>Tries to call the function and copy a map result with dynamic-array values into a managed dictionary.</summary>
    public bool TryCallDynamicArrayValueMap<TKey, TElement>(
        ReadOnlySpan<UmkaValue> arguments,
        [NotNullWhen(true)] out Dictionary<TKey, TElement[]>? value)
        where TKey : struct
        where TElement : struct
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsDynamicArrayValueMap<TKey, TElement>())
        {
            value = null;
            return false;
        }

        value = CallDynamicArrayValueMap<TKey, TElement>(arguments);
        return true;
    }

    /// <summary>Tries to call the function and copy a map result with dynamic-array values into a managed dictionary.</summary>
    public bool TryCallDynamicArrayValueMap<TKey, TElement>(
        [NotNullWhen(true)] out Dictionary<TKey, TElement[]>? value,
        params UmkaValue[] arguments)
        where TKey : struct
        where TElement : struct =>
        TryCallDynamicArrayValueMap(ToArgumentSpan(arguments), out value);

    /// <summary>Calls the function and copies a map result with string keys and dynamic-array values into a managed dictionary.</summary>
    public Dictionary<string, TElement[]> CallStringKeyDynamicArrayValueMap<TElement>(params UmkaValue[] arguments)
        where TElement : struct =>
        CallStringKeyDynamicArrayValueMap<TElement>(ToArgumentSpan(arguments));

    /// <summary>Calls the function and copies a map result with string keys and dynamic-array values into a managed dictionary.</summary>
    public Dictionary<string, TElement[]> CallStringKeyDynamicArrayValueMap<TElement>(ReadOnlySpan<UmkaValue> arguments)
        where TElement : struct
    {
        _runtime.CheckCallable();
        ValidateNoManagedReferences<TElement>();
        EnsureResultKind(UmkaTypeKind.Map);
        EnsureReadableStringKeyDynamicArrayValueMapResult<TElement>();

        var headerSize = ResultType.NativeSize;
        if (headerSize <= 0)
            throw new InvalidOperationException("The function does not return a map value with native header metadata.");

        var header = Marshal.AllocHGlobal(headerSize);
        try
        {
            Marshal.Copy(new byte[headerSize], 0, header, headerSize);
            var status = NativeMethods.ContextSetResultBuffer(ref _context, header);
            _runtime.ThrowIfError(status);
            Invoke(arguments);

            var count = NativeMethods.ContextGetResultMapCount(ref _context);
            return UmkaMapCopy.CopyStringKeyDynamicArrayValues<TElement>(
                count,
                ResultType.MapValueElementNativeSize,
                (keys, keyCount, lengths, lengthCount, elementSize) =>
                    NativeMethods.ContextCopyResultStringKeyMapDynamicArrayValueEntries(ref _context, keys, keyCount, lengths, lengthCount, elementSize),
                (entryIndex, buffer, byteSize, elementSize) =>
                    NativeMethods.ContextCopyResultStringKeyMapDynamicArrayValueData(ref _context, entryIndex, buffer, byteSize, elementSize));
        }
        finally
        {
            Marshal.FreeHGlobal(header);
            _ = NativeMethods.ContextSetResultBuffer(ref _context, IntPtr.Zero);
        }
    }

    /// <summary>Tries to call the function and copy a map result with string keys and dynamic-array values into a managed dictionary.</summary>
    public bool TryCallStringKeyDynamicArrayValueMap<TElement>(
        ReadOnlySpan<UmkaValue> arguments,
        [NotNullWhen(true)] out Dictionary<string, TElement[]>? value)
        where TElement : struct
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsStringKeyDynamicArrayValueMap<TElement>())
        {
            value = null;
            return false;
        }

        value = CallStringKeyDynamicArrayValueMap<TElement>(arguments);
        return true;
    }

    /// <summary>Tries to call the function and copy a map result with string keys and dynamic-array values into a managed dictionary.</summary>
    public bool TryCallStringKeyDynamicArrayValueMap<TElement>(
        [NotNullWhen(true)] out Dictionary<string, TElement[]>? value,
        params UmkaValue[] arguments)
        where TElement : struct =>
        TryCallStringKeyDynamicArrayValueMap(ToArgumentSpan(arguments), out value);

    /// <summary>Calls the function and copies a map result with string-array values into a managed dictionary.</summary>
    public Dictionary<TKey, string?[]> CallStringArrayValueMap<TKey>(params UmkaValue[] arguments)
        where TKey : struct =>
        CallStringArrayValueMap<TKey>(ToArgumentSpan(arguments));

    /// <summary>Calls the function and copies a map result with string-array values into a managed dictionary.</summary>
    public Dictionary<TKey, string?[]> CallStringArrayValueMap<TKey>(ReadOnlySpan<UmkaValue> arguments)
        where TKey : struct
    {
        _runtime.CheckCallable();
        ValidateNoManagedReferences<TKey>();
        EnsureResultKind(UmkaTypeKind.Map);
        EnsureReadableStringArrayValueMapResult<TKey>();

        var headerSize = ResultType.NativeSize;
        if (headerSize <= 0)
            throw new InvalidOperationException("The function does not return a map value with native header metadata.");

        var header = Marshal.AllocHGlobal(headerSize);
        try
        {
            Marshal.Copy(new byte[headerSize], 0, header, headerSize);
            var status = NativeMethods.ContextSetResultBuffer(ref _context, header);
            _runtime.ThrowIfError(status);
            Invoke(arguments);

            var count = NativeMethods.ContextGetResultMapCount(ref _context);
            return UmkaMapCopy.CopyStringArrayValues<TKey>(
                count,
                ResultType.MapKeyNativeSize,
                (keys, keyBytes, lengths, lengthCount) =>
                    NativeMethods.ContextCopyResultMapStringArrayValueEntries(ref _context, keys, keyBytes, lengths, lengthCount),
                (entryIndex, values, valueCount) =>
                    NativeMethods.ContextCopyResultMapStringArrayValueData(ref _context, entryIndex, values, valueCount));
        }
        finally
        {
            Marshal.FreeHGlobal(header);
            _ = NativeMethods.ContextSetResultBuffer(ref _context, IntPtr.Zero);
        }
    }

    /// <summary>Tries to call the function and copy a map result with string-array values into a managed dictionary.</summary>
    public bool TryCallStringArrayValueMap<TKey>(
        ReadOnlySpan<UmkaValue> arguments,
        [NotNullWhen(true)] out Dictionary<TKey, string?[]>? value)
        where TKey : struct
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsStringArrayValueMap<TKey>())
        {
            value = null;
            return false;
        }

        value = CallStringArrayValueMap<TKey>(arguments);
        return true;
    }

    /// <summary>Tries to call the function and copy a map result with string-array values into a managed dictionary.</summary>
    public bool TryCallStringArrayValueMap<TKey>(
        [NotNullWhen(true)] out Dictionary<TKey, string?[]>? value,
        params UmkaValue[] arguments)
        where TKey : struct =>
        TryCallStringArrayValueMap(ToArgumentSpan(arguments), out value);

    /// <summary>Calls the function and copies a map result with string keys and string-array values into a managed dictionary.</summary>
    public Dictionary<string, string?[]> CallStringKeyStringArrayValueMap(params UmkaValue[] arguments) =>
        CallStringKeyStringArrayValueMap(ToArgumentSpan(arguments));

    /// <summary>Calls the function and copies a map result with string keys and string-array values into a managed dictionary.</summary>
    public Dictionary<string, string?[]> CallStringKeyStringArrayValueMap(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        EnsureResultKind(UmkaTypeKind.Map);
        EnsureReadableStringKeyStringArrayValueMapResult();

        var headerSize = ResultType.NativeSize;
        if (headerSize <= 0)
            throw new InvalidOperationException("The function does not return a map value with native header metadata.");

        var header = Marshal.AllocHGlobal(headerSize);
        try
        {
            Marshal.Copy(new byte[headerSize], 0, header, headerSize);
            var status = NativeMethods.ContextSetResultBuffer(ref _context, header);
            _runtime.ThrowIfError(status);
            Invoke(arguments);

            var count = NativeMethods.ContextGetResultMapCount(ref _context);
            return UmkaMapCopy.CopyStringKeyStringArrayValues(
                count,
                (keys, keyCount, lengths, lengthCount) =>
                    NativeMethods.ContextCopyResultStringKeyMapStringArrayValueEntries(ref _context, keys, keyCount, lengths, lengthCount),
                (entryIndex, values, valueCount) =>
                    NativeMethods.ContextCopyResultStringKeyMapStringArrayValueData(ref _context, entryIndex, values, valueCount));
        }
        finally
        {
            Marshal.FreeHGlobal(header);
            _ = NativeMethods.ContextSetResultBuffer(ref _context, IntPtr.Zero);
        }
    }

    /// <summary>Tries to call the function and copy a map result with string keys and string-array values into a managed dictionary.</summary>
    public bool TryCallStringKeyStringArrayValueMap(
        ReadOnlySpan<UmkaValue> arguments,
        [NotNullWhen(true)] out Dictionary<string, string?[]>? value)
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (!ResultType.CanReadAsStringKeyStringArrayValueMap())
        {
            value = null;
            return false;
        }

        value = CallStringKeyStringArrayValueMap(arguments);
        return true;
    }

    /// <summary>Tries to call the function and copy a map result with string keys and string-array values into a managed dictionary.</summary>
    public bool TryCallStringKeyStringArrayValueMap(
        [NotNullWhen(true)] out Dictionary<string, string?[]>? value,
        params UmkaValue[] arguments) =>
        TryCallStringKeyStringArrayValueMap(ToArgumentSpan(arguments), out value);

    private void Invoke(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        var boundArguments = BindVariadicArguments(arguments);
        var actualArguments = boundArguments is null ? arguments : boundArguments.AsSpan();
        ValidateBoundArguments(actualArguments);

        for (var i = 0; i < actualArguments.Length; i++)
            SetArgument(i, actualArguments[i]);

        SetOmittedDefaultArguments(actualArguments.Length);

        _runtime.ClearLastCallbackException();
        var status = NativeMethods.Call(_runtime.Handle, ref _context);
        _runtime.ThrowIfExecutionError(status);
    }

    private void ValidateArguments(ReadOnlySpan<UmkaValue> arguments)
    {
        var boundArguments = BindVariadicArguments(arguments);
        if (boundArguments is null)
            ValidateBoundArguments(arguments);
        else
            ValidateBoundArguments(boundArguments);
    }

    private void ValidateBoundArguments(ReadOnlySpan<UmkaValue> arguments)
    {
        if (arguments.Length < RequiredParameterCount || arguments.Length > ParameterCount)
        {
            var expected = RequiredParameterCount == ParameterCount
                ? $"{ParameterCount}"
                : $"{RequiredParameterCount}..{ParameterCount}";
            throw new ArgumentException(
                $"Function '{DiagnosticName}' expects {expected} argument(s), but {arguments.Length} were provided.",
                nameof(arguments));
        }

        for (var i = 0; i < arguments.Length; i++)
            ValidateArgument(i, arguments[i]);

        for (var i = arguments.Length; i < ParameterCount; i++)
            ValidateOmittedDefaultArgument(i, nameof(arguments));
    }

    private static ReadOnlySpan<UmkaValue> ToArgumentSpan(UmkaValue[] arguments) =>
        arguments ?? throw new ArgumentNullException(nameof(arguments));

    private static bool IsVariadicFunction(UmkaTypeInfo[] parameterTypes) =>
        parameterTypes.Length > 0 && parameterTypes[^1].IsVariadicParameterList;

    private bool CanCallWithBoundArguments(ReadOnlySpan<UmkaValue> arguments)
    {
        if (arguments.Length < RequiredParameterCount || arguments.Length > ParameterCount)
            return false;

        for (var i = 0; i < arguments.Length; i++)
        {
            if (!CanAcceptArgument(i, arguments[i]))
                return false;
        }

        for (var i = arguments.Length; i < ParameterCount; i++)
        {
            if (!CanSetOmittedDefaultArgument(_nativeParameterKinds[i]))
                return false;
        }

        return true;
    }

    private UmkaValue[]? BindVariadicArguments(ReadOnlySpan<UmkaValue> arguments)
    {
        if (!HasVariadicParameter)
            return null;

        var variadicIndex = ParameterCount - 1;
        if (arguments.Length < variadicIndex)
        {
            throw new ArgumentException(
                $"Function '{DiagnosticName}' expects at least {variadicIndex} argument(s), but {arguments.Length} were provided.",
                nameof(arguments));
        }

        if (arguments.Length == ParameterCount && arguments[variadicIndex].Kind == UmkaValueKind.DynamicArray)
            return null;

        var boundArguments = new UmkaValue[ParameterCount];
        for (var i = 0; i < variadicIndex; i++)
            boundArguments[i] = arguments[i];

        boundArguments[variadicIndex] = CreateVariadicDynamicArray(arguments[variadicIndex..], variadicIndex);
        return boundArguments;
    }

    private UmkaValue CreateVariadicDynamicArray(ReadOnlySpan<UmkaValue> values, int parameterIndex)
    {
        var parameterType = _parameterTypes[parameterIndex];
        if (parameterType.ElementHasReferences)
        {
            throw new ArgumentException(
                $"Function '{DiagnosticName}' variadic argument {parameterIndex} expects Umka element type '{parameterType.ElementTypeName ?? "unknown"}', which contains Umka-managed references and cannot be packed from expanded C# arguments.",
                nameof(values));
        }

        var elementSize = parameterType.ElementNativeSize;
        if (elementSize <= 0)
        {
            throw new ArgumentException(
                $"Function '{DiagnosticName}' variadic argument {parameterIndex} expects Umka type '{parameterType.TypeName}', but its native element size is unavailable.",
                nameof(values));
        }

        var elementKind = _nativeParameterElementKinds[parameterIndex];
        if (!IsSupportedVariadicElementKind(elementKind))
        {
            throw new ArgumentException(
                $"Function '{DiagnosticName}' variadic argument {parameterIndex} expects Umka element type '{parameterType.ElementTypeName ?? "unknown"}', which UmkaSharp cannot pack from expanded C# arguments. Pass an explicit UmkaValue.FromDynamicArray<TElement>(...) value instead.",
                nameof(values));
        }

        var bytes = new byte[checked(values.Length * elementSize)];
        for (var i = 0; i < values.Length; i++)
            PackVariadicElement(values[i], parameterIndex, variadicArgumentIndex: parameterIndex + i, elementKind, elementSize, bytes.AsSpan(i * elementSize, elementSize));

        return UmkaValue.FromRawDynamicArray(bytes, values.Length, elementSize);
    }

    private void PackVariadicElement(
        UmkaValue value,
        int parameterIndex,
        int variadicArgumentIndex,
        NativeUmkaTypeKind elementKind,
        int elementSize,
        Span<byte> destination)
    {
        if (!IsCompatibleVariadicElementKind(elementKind, value.Kind))
        {
            throw new ArgumentException(
                $"Function '{DiagnosticName}' variadic argument {variadicArgumentIndex} expects Umka element type '{_parameterTypes[parameterIndex].ElementTypeName ?? "unknown"}', but value kind {value.Kind} was provided.",
                nameof(value));
        }

        switch (elementKind)
        {
            case NativeUmkaTypeKind.Int8:
            case NativeUmkaTypeKind.Int16:
            case NativeUmkaTypeKind.Int32:
            case NativeUmkaTypeKind.Int:
                ValidateArgumentRange(parameterIndex, elementKind, value);
                WriteSignedInteger(destination, value.AsInt64());
                return;

            case NativeUmkaTypeKind.UInt8:
            case NativeUmkaTypeKind.UInt16:
            case NativeUmkaTypeKind.UInt32:
            case NativeUmkaTypeKind.UInt:
                ValidateArgumentRange(parameterIndex, elementKind, value);
                WriteUnsignedInteger(destination, value.AsUInt64());
                return;

            case NativeUmkaTypeKind.Bool:
                WriteUnsignedInteger(destination, value.AsBoolean() ? 1UL : 0UL);
                return;

            case NativeUmkaTypeKind.Char:
                ValidateArgumentRange(parameterIndex, elementKind, value);
                WriteUnsignedInteger(destination, value.AsChar());
                return;

            case NativeUmkaTypeKind.Real32:
                ValidateArgumentRange(parameterIndex, elementKind, value);
                WriteSingle(destination, value.AsSingle());
                return;

            case NativeUmkaTypeKind.Real:
                WriteDouble(destination, value.AsDouble());
                return;

            case NativeUmkaTypeKind.Pointer:
                WritePointer(destination, value.AsPointer());
                return;

            case NativeUmkaTypeKind.StaticArray:
            case NativeUmkaTypeKind.Struct:
                PackStructuredVariadicElement(value, parameterIndex, elementSize, destination);
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(elementKind), elementKind, "Unsupported variadic element kind.");
        }
    }

    private static bool IsSupportedVariadicElementKind(NativeUmkaTypeKind kind) =>
        kind is NativeUmkaTypeKind.Int8
            or NativeUmkaTypeKind.Int16
            or NativeUmkaTypeKind.Int32
            or NativeUmkaTypeKind.Int
            or NativeUmkaTypeKind.UInt8
            or NativeUmkaTypeKind.UInt16
            or NativeUmkaTypeKind.UInt32
            or NativeUmkaTypeKind.UInt
            or NativeUmkaTypeKind.Bool
            or NativeUmkaTypeKind.Char
            or NativeUmkaTypeKind.Real32
            or NativeUmkaTypeKind.Real
            or NativeUmkaTypeKind.Pointer
            or NativeUmkaTypeKind.StaticArray
            or NativeUmkaTypeKind.Struct;

    private static bool IsCompatibleVariadicElementKind(NativeUmkaTypeKind nativeKind, UmkaValueKind valueKind) =>
        nativeKind switch
        {
            NativeUmkaTypeKind.Int8
                or NativeUmkaTypeKind.Int16
                or NativeUmkaTypeKind.Int32
                or NativeUmkaTypeKind.Int => valueKind == UmkaValueKind.Int,
            NativeUmkaTypeKind.UInt8
                or NativeUmkaTypeKind.UInt16
                or NativeUmkaTypeKind.UInt32
                or NativeUmkaTypeKind.UInt => valueKind == UmkaValueKind.UInt,
            NativeUmkaTypeKind.Bool => valueKind == UmkaValueKind.Bool,
            NativeUmkaTypeKind.Char => valueKind is UmkaValueKind.Int or UmkaValueKind.UInt,
            NativeUmkaTypeKind.Real32 or NativeUmkaTypeKind.Real => valueKind == UmkaValueKind.Real,
            NativeUmkaTypeKind.Pointer => valueKind == UmkaValueKind.Pointer,
            NativeUmkaTypeKind.StaticArray => valueKind == UmkaValueKind.StaticArray,
            NativeUmkaTypeKind.Struct => valueKind == UmkaValueKind.Struct,
            _ => false
        };

    private void PackStructuredVariadicElement(
        UmkaValue value,
        int parameterIndex,
        int elementSize,
        Span<byte> destination)
    {
        if (value.StructuredSize != elementSize)
        {
            throw new ArgumentException(
                $"Function '{DiagnosticName}' variadic argument {parameterIndex} expects Umka element type '{_parameterTypes[parameterIndex].ElementTypeName ?? "unknown"}' with native size {elementSize} bytes, but value kind {value.Kind} has size {value.StructuredSize} bytes.",
                nameof(value));
        }

        var buffer = Marshal.AllocHGlobal(elementSize);
        try
        {
            value.CopyStructuredTo(buffer);
            var bytes = new byte[elementSize];
            Marshal.Copy(buffer, bytes, 0, elementSize);
            bytes.AsSpan().CopyTo(destination);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void WriteSignedInteger(Span<byte> destination, long value)
    {
        switch (destination.Length)
        {
            case 1:
                destination[0] = unchecked((byte)(sbyte)value);
                return;
            case 2:
                _ = BitConverter.TryWriteBytes(destination, checked((short)value));
                return;
            case 4:
                _ = BitConverter.TryWriteBytes(destination, checked((int)value));
                return;
            case 8:
                _ = BitConverter.TryWriteBytes(destination, value);
                return;
            default:
                throw new ArgumentException($"Unsupported signed integer element size {destination.Length}.", nameof(destination));
        }
    }

    private static void WriteUnsignedInteger(Span<byte> destination, ulong value)
    {
        switch (destination.Length)
        {
            case 1:
                destination[0] = checked((byte)value);
                return;
            case 2:
                _ = BitConverter.TryWriteBytes(destination, checked((ushort)value));
                return;
            case 4:
                _ = BitConverter.TryWriteBytes(destination, checked((uint)value));
                return;
            case 8:
                _ = BitConverter.TryWriteBytes(destination, value);
                return;
            default:
                throw new ArgumentException($"Unsupported unsigned integer element size {destination.Length}.", nameof(destination));
        }
    }

    private static void WriteSingle(Span<byte> destination, float value)
    {
        if (destination.Length != sizeof(float))
            throw new ArgumentException($"Unsupported real32 element size {destination.Length}.", nameof(destination));
        _ = BitConverter.TryWriteBytes(destination, value);
    }

    private static void WriteDouble(Span<byte> destination, double value)
    {
        if (destination.Length != sizeof(double))
            throw new ArgumentException($"Unsupported real element size {destination.Length}.", nameof(destination));
        _ = BitConverter.TryWriteBytes(destination, value);
    }

    private static void WritePointer(Span<byte> destination, IntPtr value)
    {
        if (destination.Length == sizeof(long))
        {
            _ = BitConverter.TryWriteBytes(destination, value.ToInt64());
            return;
        }

        if (destination.Length == sizeof(int))
        {
            _ = BitConverter.TryWriteBytes(destination, checked((int)value.ToInt64()));
            return;
        }

        throw new ArgumentException($"Unsupported pointer element size {destination.Length}.", nameof(destination));
    }

    private static T BoxScalar<T>(object? value) => (T)value!;

    private bool CanAcceptArgument(int index, UmkaValue value)
    {
        if (value.Kind == UmkaValueKind.Void)
            return false;

        var nativeKind = _nativeParameterKinds[index];
        if (!IsSupportedArgumentKind(nativeKind) || !IsCompatibleArgumentKind(nativeKind, value.Kind))
            return false;

        return value.Kind switch
        {
            UmkaValueKind.StaticArray or UmkaValueKind.Struct => CanAcceptStructuredArgument(index, nativeKind, value),
            UmkaValueKind.DynamicArray => CanAcceptDynamicArrayArgument(index, value),
            _ => IsArgumentInRange(nativeKind, value)
        };
    }

    private bool CanAcceptStructuredArgument(int index, NativeUmkaTypeKind nativeKind, UmkaValue value)
    {
        var parameterType = _parameterTypes[index];
        if (parameterType.HasReferences || parameterType.NativeSize <= 0)
            return false;

        if (nativeKind == NativeUmkaTypeKind.StaticArray && value.StructuredLength != parameterType.ItemCount)
            return false;

        return value.StructuredSize == parameterType.NativeSize;
    }

    private bool CanAcceptDynamicArrayArgument(int index, UmkaValue value)
    {
        var parameterType = _parameterTypes[index];
        if (parameterType.CanReadAsStringArray())
            return value.IsStringDynamicArray;

        if (parameterType.CanReadAsNestedStringArray())
            return value.IsNestedStringDynamicArray;

        if (parameterType.Kind == UmkaTypeKind.DynamicArray && parameterType.ElementKind == UmkaTypeKind.DynamicArray)
        {
            return value.IsNestedDynamicArray
                && !value.IsNestedStringDynamicArray
                && !parameterType.NestedElementHasReferences
                && parameterType.NestedElementNativeSize > 0
                && value.StructuredElementSize == parameterType.NestedElementNativeSize;
        }

        return parameterType.Kind == UmkaTypeKind.DynamicArray
            && !parameterType.ElementHasReferences
            && parameterType.ElementNativeSize > 0
            && !value.IsStringDynamicArray
            && !value.IsNestedDynamicArray
            && value.StructuredElementSize == parameterType.ElementNativeSize;
    }

    private static bool IsArgumentInRange(NativeUmkaTypeKind nativeKind, UmkaValue value) =>
        nativeKind switch
        {
            NativeUmkaTypeKind.Int8 => IsSignedInRange(value.AsInt64(), sbyte.MinValue, sbyte.MaxValue),
            NativeUmkaTypeKind.Int16 => IsSignedInRange(value.AsInt64(), short.MinValue, short.MaxValue),
            NativeUmkaTypeKind.Int32 => IsSignedInRange(value.AsInt64(), int.MinValue, int.MaxValue),
            NativeUmkaTypeKind.UInt8 => value.AsUInt64() <= byte.MaxValue,
            NativeUmkaTypeKind.UInt16 => value.AsUInt64() <= ushort.MaxValue,
            NativeUmkaTypeKind.UInt32 => value.AsUInt64() <= uint.MaxValue,
            NativeUmkaTypeKind.Char when value.Kind == UmkaValueKind.Int => IsSignedInRange(value.AsInt64(), byte.MinValue, byte.MaxValue),
            NativeUmkaTypeKind.Char => value.AsUInt64() <= byte.MaxValue,
            NativeUmkaTypeKind.Real32 => !UmkaSingleConversion.IsOutsideFiniteSingleRange(value.AsDouble()),
            _ => true
        };

    private static bool IsSignedInRange(long value, long minValue, long maxValue) =>
        value >= minValue && value <= maxValue;

    private T CallEnumScalar<T>(ReadOnlySpan<UmkaValue> arguments)
    {
        var enumType = typeof(T);
        object rawValue = Type.GetTypeCode(Enum.GetUnderlyingType(enumType)) switch
        {
            TypeCode.SByte => checked((sbyte)CallInt64(arguments)),
            TypeCode.Int16 => checked((short)CallInt64(arguments)),
            TypeCode.Int32 => checked((int)CallInt64(arguments)),
            TypeCode.Int64 => CallInt64(arguments),
            TypeCode.Byte => checked((byte)CallUInt64(arguments)),
            TypeCode.UInt16 => checked((ushort)CallUInt64(arguments)),
            TypeCode.UInt32 => checked((uint)CallUInt64(arguments)),
            TypeCode.UInt64 => CallUInt64(arguments),
            _ => throw new InvalidOperationException($"Enum type {enumType.FullName} has an unsupported underlying storage type.")
        };

        return (T)Enum.ToObject(enumType, rawValue);
    }

    private void SetArgument(int index, UmkaValue value)
    {
        var status = value.Kind switch
        {
            UmkaValueKind.Void => throw new ArgumentException("Void cannot be used as a function argument.", nameof(value)),
            UmkaValueKind.Int => NativeMethods.ContextSetArgInt(_runtime.Handle, ref _context, index, value.AsInt64()),
            UmkaValueKind.UInt => NativeMethods.ContextSetArgUInt(_runtime.Handle, ref _context, index, value.AsUInt64()),
            UmkaValueKind.Real => NativeMethods.ContextSetArgReal(_runtime.Handle, ref _context, index, value.AsDouble()),
            UmkaValueKind.Bool => NativeMethods.ContextSetArgInt(_runtime.Handle, ref _context, index, value.AsBoolean() ? 1 : 0),
            UmkaValueKind.String => NativeMethods.ContextSetArgString(_runtime.Handle, ref _context, index, value.AsString()),
            UmkaValueKind.Pointer => NativeMethods.ContextSetArgPointer(_runtime.Handle, ref _context, index, value.AsPointer()),
            UmkaValueKind.WeakPointer => NativeMethods.ContextSetArgUInt(_runtime.Handle, ref _context, index, value.AsWeakPointer()),
            UmkaValueKind.StaticArray or UmkaValueKind.Struct => SetStructuredArgument(index, value),
            UmkaValueKind.DynamicArray => SetDynamicArrayArgument(index, value),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "Unsupported value kind.")
        };

        _runtime.ThrowIfError(status);
    }

    private void SetOmittedDefaultArguments(int providedCount)
    {
        if (providedCount == ParameterCount)
            return;

        var status = NativeMethods.ContextSetDefaultArguments(_runtime.Handle, ref _context, providedCount);
        _runtime.ThrowIfError(status);
    }

    private int SetStructuredArgument(int index, UmkaValue value)
    {
        var size = value.StructuredSize;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            value.CopyStructuredTo(buffer);
            return NativeMethods.ContextSetArgData(_runtime.Handle, ref _context, index, buffer, size);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private int SetDynamicArrayArgument(int index, UmkaValue value)
    {
        if (value.IsStringDynamicArray)
        {
            using var strings = new NativeUtf8StringArray(value.GetStringDynamicArray());
            return NativeMethods.ContextSetArgStringDynamicArray(
                _runtime.Handle,
                ref _context,
                index,
                strings.Pointer,
                strings.Length);
        }

        if (value.IsNestedStringDynamicArray)
        {
            var rowLengths = value.GetNestedDynamicArrayRowLengths();
            var lengthsBuffer = rowLengths.Length == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(checked(sizeof(int) * rowLengths.Length));
            try
            {
                if (rowLengths.Length > 0)
                    Marshal.Copy(rowLengths, 0, lengthsBuffer, rowLengths.Length);

                using var strings = new NativeUtf8StringArray(value.GetFlattenedNestedStringDynamicArray());
                return NativeMethods.ContextSetArgNestedStringArray(
                    _runtime.Handle,
                    ref _context,
                    index,
                    lengthsBuffer,
                    rowLengths.Length,
                    strings.Pointer,
                    strings.Length);
            }
            finally
            {
                if (lengthsBuffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(lengthsBuffer);
            }
        }

        if (value.IsNestedDynamicArray)
        {
            var rowLengths = value.GetNestedDynamicArrayRowLengths();
            var lengthsBuffer = rowLengths.Length == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(checked(sizeof(int) * rowLengths.Length));
            var valueByteCount = value.StructuredSize;
            var valueBuffer = valueByteCount == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(valueByteCount);
            try
            {
                if (rowLengths.Length > 0)
                    Marshal.Copy(rowLengths, 0, lengthsBuffer, rowLengths.Length);
                if (valueByteCount > 0)
                    value.CopyNestedDynamicArrayElementsTo(valueBuffer);

                return NativeMethods.ContextSetArgNestedDynamicArray(
                    _runtime.Handle,
                    ref _context,
                    index,
                    lengthsBuffer,
                    rowLengths.Length,
                    valueBuffer,
                    valueByteCount,
                    value.StructuredElementSize);
            }
            finally
            {
                if (valueBuffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(valueBuffer);
                if (lengthsBuffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(lengthsBuffer);
            }
        }

        var size = value.StructuredSize;
        var buffer = size == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(size);
        try
        {
            if (size > 0)
                value.CopyStructuredTo(buffer);

            return NativeMethods.ContextSetArgDynamicArray(
                _runtime.Handle,
                ref _context,
                index,
                buffer,
                value.StructuredLength,
                value.StructuredElementSize);
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
        }
    }

    private void ValidateArgument(int index, UmkaValue value)
    {
        if (value.Kind == UmkaValueKind.Void)
            throw new ArgumentException("Void cannot be used as a function argument.", nameof(value));

        var nativeKind = _nativeParameterKinds[index];
        if (!IsSupportedArgumentKind(nativeKind))
        {
            throw new ArgumentException(
                UnsupportedArgumentMessage(index, nativeKind),
                nameof(value));
        }

        if (!IsCompatibleArgumentKind(nativeKind, value.Kind))
        {
            throw new ArgumentException(
                $"Function '{DiagnosticName}' argument {index} expects Umka type '{_parameterTypes[index].TypeName}', but value kind {value.Kind} was provided.",
                nameof(value));
        }

        if (value.Kind is UmkaValueKind.StaticArray or UmkaValueKind.Struct)
        {
            ValidateStructuredArgument(index, nativeKind, value);
            return;
        }

        if (value.Kind == UmkaValueKind.DynamicArray)
        {
            ValidateDynamicArrayArgument(index, value);
            return;
        }

        ValidateArgumentRange(index, nativeKind, value);
    }

    private void ValidateOmittedDefaultArgument(int index, string parameterName)
    {
        var nativeKind = _nativeParameterKinds[index];
        if (CanSetOmittedDefaultArgument(nativeKind))
            return;

        throw new ArgumentException(
            $"Function '{DiagnosticName}' argument {index} has Umka default type '{_parameterTypes[index].TypeName}', which UmkaSharp cannot synthesize for an omitted C# argument. Pass the argument explicitly.",
            parameterName);
    }

    private void ValidateStructuredArgument(int index, NativeUmkaTypeKind nativeKind, UmkaValue value)
    {
        var parameterType = _parameterTypes[index];
        if (parameterType.HasReferences)
        {
            throw new ArgumentException(
                $"Function '{DiagnosticName}' argument {index} expects Umka type '{parameterType.TypeName}', which contains Umka-managed references and cannot be copied from a managed aggregate value.",
                nameof(value));
        }

        var nativeSize = parameterType.NativeSize;
        if (nativeSize <= 0)
        {
            throw new ArgumentException(
                $"Function '{DiagnosticName}' argument {index} expects Umka type '{parameterType.TypeName}', but its native size is unavailable.",
                nameof(value));
        }

        if (nativeKind == NativeUmkaTypeKind.StaticArray)
        {
            var nativeLength = parameterType.ItemCount;
            if (value.StructuredLength != nativeLength)
            {
                throw new ArgumentException(
                    $"Function '{DiagnosticName}' argument {index} expects Umka type '{parameterType.TypeName}' with {nativeLength} item(s), but value kind {value.Kind} has {value.StructuredLength} item(s).",
                    nameof(value));
            }
        }

        if (value.StructuredSize != nativeSize)
        {
            throw new ArgumentException(
                $"Function '{DiagnosticName}' argument {index} expects Umka type '{parameterType.TypeName}' with native size {nativeSize} bytes, but value kind {value.Kind} has size {value.StructuredSize} bytes.",
                nameof(value));
        }
    }

    private void ValidateDynamicArrayArgument(int index, UmkaValue value)
    {
        var parameterType = _parameterTypes[index];
        if (parameterType.CanReadAsStringArray())
        {
            if (!value.IsStringDynamicArray)
            {
                throw new ArgumentException(
                    $"Function '{DiagnosticName}' argument {index} expects Umka type '{parameterType.TypeName}', but the dynamic array value is not a string array.",
                    nameof(value));
            }

            return;
        }

        if (value.IsStringDynamicArray)
        {
            throw new ArgumentException(
                $"Function '{DiagnosticName}' argument {index} expects Umka type '{parameterType.TypeName}', but a string dynamic array value was provided.",
                nameof(value));
        }

        if (parameterType.CanReadAsNestedStringArray())
        {
            if (!value.IsNestedStringDynamicArray)
            {
                throw new ArgumentException(
                    $"Function '{DiagnosticName}' argument {index} expects Umka type '{parameterType.TypeName}', but the dynamic array value is not a nested string array.",
                    nameof(value));
            }

            return;
        }

        if (value.IsNestedStringDynamicArray)
        {
            throw new ArgumentException(
                $"Function '{DiagnosticName}' argument {index} expects Umka type '{parameterType.TypeName}', but a nested string dynamic array value was provided.",
                nameof(value));
        }

        if (parameterType.Kind == UmkaTypeKind.DynamicArray && parameterType.ElementKind == UmkaTypeKind.DynamicArray)
        {
            if (!value.IsNestedDynamicArray)
            {
                throw new ArgumentException(
                    $"Function '{DiagnosticName}' argument {index} expects Umka type '{parameterType.TypeName}', but the dynamic array value is not a nested dynamic array.",
                    nameof(value));
            }

            if (parameterType.NestedElementHasReferences)
            {
                throw new ArgumentException(
                    $"Function '{DiagnosticName}' argument {index} expects Umka type '{parameterType.TypeName}', whose inner element type '{parameterType.NestedElementTypeName ?? "unknown"}' contains Umka-managed references and cannot be copied from a managed nested dynamic array value.",
                    nameof(value));
            }

            var nestedElementSize = parameterType.NestedElementNativeSize;
            if (nestedElementSize <= 0)
            {
                throw new ArgumentException(
                    $"Function '{DiagnosticName}' argument {index} expects Umka type '{parameterType.TypeName}', but its native inner element size is unavailable.",
                    nameof(value));
            }

            if (value.StructuredElementSize != nestedElementSize)
            {
                throw new ArgumentException(
                    $"Function '{DiagnosticName}' argument {index} expects Umka type '{parameterType.TypeName}' with native inner element size {nestedElementSize} bytes, but nested dynamic array value elements have size {value.StructuredElementSize} bytes.",
                    nameof(value));
            }

            return;
        }

        if (value.IsNestedDynamicArray)
        {
            throw new ArgumentException(
                $"Function '{DiagnosticName}' argument {index} expects Umka type '{parameterType.TypeName}', but a nested dynamic array value was provided.",
                nameof(value));
        }

        if (parameterType.ElementHasReferences)
        {
            throw new ArgumentException(
                $"Function '{DiagnosticName}' argument {index} expects Umka type '{parameterType.TypeName}', whose element type '{parameterType.ElementTypeName ?? "unknown"}' contains Umka-managed references and cannot be copied from a managed dynamic array value.",
                nameof(value));
        }

        var elementSize = parameterType.ElementNativeSize;
        if (elementSize <= 0)
        {
            throw new ArgumentException(
                $"Function '{DiagnosticName}' argument {index} expects Umka type '{parameterType.TypeName}', but its native element size is unavailable.",
                nameof(value));
        }

        if (value.StructuredElementSize != elementSize)
        {
            throw new ArgumentException(
                $"Function '{DiagnosticName}' argument {index} expects Umka type '{parameterType.TypeName}' with native element size {elementSize} bytes, but dynamic array value elements have size {value.StructuredElementSize} bytes.",
                nameof(value));
        }
    }

    private void EnsureReadableStructuredResult()
    {
        if (ResultType.HasReferences)
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka type '{ResultType.TypeName}', which contains Umka-managed references and cannot be copied into a managed aggregate value.");
        }
    }

    private void EnsureReadableDynamicArrayResult<TElement>() where TElement : struct
    {
        if (ResultType.ElementHasReferences)
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka type '{ResultType.TypeName}', whose element type '{ResultType.ElementTypeName ?? "unknown"}' contains Umka-managed references and cannot be copied into a managed dynamic array.");
        }

        var elementSize = ResultType.ElementNativeSize;
        if (elementSize <= 0)
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka type '{ResultType.TypeName}', but its native element size is unavailable.");
        }

        var managedElementSize = Marshal.SizeOf<TElement>();
        if (managedElementSize != elementSize)
        {
            throw new InvalidOperationException(
                $"Managed dynamic array element type {typeof(TElement).FullName} is {managedElementSize} bytes, but Umka element type '{ResultType.ElementTypeName ?? "unknown"}' is {elementSize} bytes.");
        }
    }

    private void EnsureReadableStringDynamicArrayResult()
    {
        if (!ResultType.CanReadAsStringArray())
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka type '{ResultType.TypeName}', which cannot be copied into a managed string array.");
        }
    }

    private void EnsureReadableNestedDynamicArrayResult<TElement>() where TElement : struct
    {
        if (ResultType.ElementKind != UmkaTypeKind.DynamicArray)
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka type '{ResultType.TypeName}', which cannot be copied into a managed jagged array because its element type is '{ResultType.ElementTypeName ?? "unknown"}'.");
        }

        if (ResultType.NestedElementHasReferences)
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka type '{ResultType.TypeName}', whose inner element type '{ResultType.NestedElementTypeName ?? "unknown"}' contains Umka-managed references and cannot be copied into a managed jagged array.");
        }

        var elementSize = ResultType.NestedElementNativeSize;
        if (elementSize <= 0)
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka type '{ResultType.TypeName}', but its native inner element size is unavailable.");
        }

        var managedElementSize = Marshal.SizeOf<TElement>();
        if (managedElementSize != elementSize)
        {
            throw new InvalidOperationException(
                $"Managed jagged array inner element type {typeof(TElement).FullName} is {managedElementSize} bytes, but Umka inner element type '{ResultType.NestedElementTypeName ?? "unknown"}' is {elementSize} bytes.");
        }
    }

    private void EnsureReadableNestedStringArrayResult()
    {
        if (!ResultType.CanReadAsNestedStringArray())
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka type '{ResultType.TypeName}', which cannot be copied into a managed jagged string array.");
        }
    }

    private void EnsureReadableMapResult<TKey, TValue>()
        where TKey : struct
        where TValue : struct
    {
        if (ResultType.MapKeyHasReferences)
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka map type '{ResultType.TypeName}', whose key type '{ResultType.MapKeyTypeName ?? "unknown"}' contains Umka-managed references and cannot be copied into a managed dictionary.");
        }

        if (ResultType.MapValueHasReferences)
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka map type '{ResultType.TypeName}', whose value type '{ResultType.MapValueTypeName ?? "unknown"}' contains Umka-managed references and cannot be copied into a managed dictionary.");
        }

        if (ResultType.MapKeyNativeSize <= 0 || ResultType.MapValueNativeSize <= 0)
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka map type '{ResultType.TypeName}', but its native key/value size metadata is unavailable.");
        }

        var managedKeySize = Marshal.SizeOf<TKey>();
        if (managedKeySize != ResultType.MapKeyNativeSize)
        {
            throw new InvalidOperationException(
                $"Managed map key type {typeof(TKey).FullName} is {managedKeySize} bytes, but Umka key type '{ResultType.MapKeyTypeName ?? "unknown"}' is {ResultType.MapKeyNativeSize} bytes.");
        }

        var managedValueSize = Marshal.SizeOf<TValue>();
        if (managedValueSize != ResultType.MapValueNativeSize)
        {
            throw new InvalidOperationException(
                $"Managed map value type {typeof(TValue).FullName} is {managedValueSize} bytes, but Umka value type '{ResultType.MapValueTypeName ?? "unknown"}' is {ResultType.MapValueNativeSize} bytes.");
        }
    }

    private void EnsureReadableStringKeyMapResult<TValue>() where TValue : struct
    {
        if (!ResultType.CanReadAsStringKeyMap<TValue>())
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka map type '{ResultType.TypeName}', which cannot be copied into a managed dictionary with string keys and {typeof(TValue).FullName} values.");
        }
    }

    private void EnsureReadableStringValueMapResult<TKey>() where TKey : struct
    {
        if (!ResultType.CanReadAsStringValueMap<TKey>())
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka map type '{ResultType.TypeName}', which cannot be copied into a managed dictionary with {typeof(TKey).FullName} keys and string values.");
        }
    }

    private void EnsureReadableStringMapResult()
    {
        if (!ResultType.CanReadAsStringMap())
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka map type '{ResultType.TypeName}', which cannot be copied into a managed dictionary with string keys and string values.");
        }
    }

    private void EnsureReadableDynamicArrayValueMapResult<TKey, TElement>()
        where TKey : struct
        where TElement : struct
    {
        if (!ResultType.CanReadAsDynamicArrayValueMap<TKey, TElement>())
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka map type '{ResultType.TypeName}', which cannot be copied into a managed dictionary with {typeof(TKey).FullName} keys and {typeof(TElement).FullName}[] values.");
        }
    }

    private void EnsureReadableStringKeyDynamicArrayValueMapResult<TElement>() where TElement : struct
    {
        if (!ResultType.CanReadAsStringKeyDynamicArrayValueMap<TElement>())
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka map type '{ResultType.TypeName}', which cannot be copied into a managed dictionary with string keys and {typeof(TElement).FullName}[] values.");
        }
    }

    private void EnsureReadableStringArrayValueMapResult<TKey>() where TKey : struct
    {
        if (!ResultType.CanReadAsStringArrayValueMap<TKey>())
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka map type '{ResultType.TypeName}', which cannot be copied into a managed dictionary with {typeof(TKey).FullName} keys and string-array values.");
        }
    }

    private void EnsureReadableStringKeyStringArrayValueMapResult()
    {
        if (!ResultType.CanReadAsStringKeyStringArrayValueMap())
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka map type '{ResultType.TypeName}', which cannot be copied into a managed dictionary with string keys and string-array values.");
        }
    }

    private static void ValidateNoManagedReferences<T>() where T : struct
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            throw new ArgumentException(
                $"Managed type {typeof(T).FullName} contains managed references and cannot receive Umka aggregate storage.");
        }
    }

    private void EnsureResultKind(UmkaTypeKind expectedKind)
    {
        if (ResultType.Kind == expectedKind)
            return;

        throw new InvalidOperationException(
            $"Function '{DiagnosticName}' returns Umka type '{ResultType.TypeName}', which cannot be read as {expectedKind}.");
    }

    private void EnsureResultKind(UmkaTypeKind expectedKind, UmkaTypeKind alternateKind)
    {
        if (ResultType.Kind == expectedKind || ResultType.Kind == alternateKind)
            return;

        throw new InvalidOperationException(
            $"Function '{DiagnosticName}' returns Umka type '{ResultType.TypeName}', which cannot be read as {expectedKind} or {alternateKind}.");
    }

    private static bool IsSupportedArgumentKind(NativeUmkaTypeKind kind) =>
        kind is NativeUmkaTypeKind.Int8
            or NativeUmkaTypeKind.Int16
            or NativeUmkaTypeKind.Int32
            or NativeUmkaTypeKind.Int
            or NativeUmkaTypeKind.UInt8
            or NativeUmkaTypeKind.UInt16
            or NativeUmkaTypeKind.UInt32
            or NativeUmkaTypeKind.UInt
            or NativeUmkaTypeKind.Bool
            or NativeUmkaTypeKind.Char
            or NativeUmkaTypeKind.Real32
            or NativeUmkaTypeKind.Real
            or NativeUmkaTypeKind.Pointer
            or NativeUmkaTypeKind.WeakPointer
            or NativeUmkaTypeKind.String
            or NativeUmkaTypeKind.StaticArray
            or NativeUmkaTypeKind.Struct
            or NativeUmkaTypeKind.DynamicArray;

    private string UnsupportedArgumentMessage(int index, NativeUmkaTypeKind nativeKind)
    {
        var typeName = _parameterTypes[index].TypeName;
        if (nativeKind == NativeUmkaTypeKind.Map)
        {
            return $"Function '{DiagnosticName}' argument {index} expects Umka map type '{typeName}', but UmkaSharp cannot synthesize map arguments from C#. The current Umka public C API exposes map lookup and type metadata, but not host-side map creation, insertion, rooting, ownership transfer, or assignment/reference-count updates.";
        }

        return $"Function '{DiagnosticName}' argument {index} expects Umka type '{typeName}', which UmkaSharp does not support as a call argument.";
    }

    private static bool IsCompatibleArgumentKind(NativeUmkaTypeKind nativeKind, UmkaValueKind valueKind) =>
        nativeKind switch
        {
            NativeUmkaTypeKind.Int8
                or NativeUmkaTypeKind.Int16
                or NativeUmkaTypeKind.Int32
                or NativeUmkaTypeKind.Int => valueKind == UmkaValueKind.Int,
            NativeUmkaTypeKind.UInt8
                or NativeUmkaTypeKind.UInt16
                or NativeUmkaTypeKind.UInt32
                or NativeUmkaTypeKind.UInt => valueKind == UmkaValueKind.UInt,
            NativeUmkaTypeKind.Bool => valueKind == UmkaValueKind.Bool,
            NativeUmkaTypeKind.Char => valueKind is UmkaValueKind.Int or UmkaValueKind.UInt,
            NativeUmkaTypeKind.Real32 or NativeUmkaTypeKind.Real => valueKind == UmkaValueKind.Real,
            NativeUmkaTypeKind.Pointer => valueKind == UmkaValueKind.Pointer,
            NativeUmkaTypeKind.WeakPointer => valueKind == UmkaValueKind.WeakPointer,
            NativeUmkaTypeKind.String => valueKind == UmkaValueKind.String,
            NativeUmkaTypeKind.StaticArray => valueKind == UmkaValueKind.StaticArray,
            NativeUmkaTypeKind.Struct => valueKind == UmkaValueKind.Struct,
            NativeUmkaTypeKind.DynamicArray => valueKind == UmkaValueKind.DynamicArray,
            _ => false
        };

    private static bool CanSetOmittedDefaultArgument(NativeUmkaTypeKind kind) =>
        kind is NativeUmkaTypeKind.Int8
            or NativeUmkaTypeKind.Int16
            or NativeUmkaTypeKind.Int32
            or NativeUmkaTypeKind.Int
            or NativeUmkaTypeKind.UInt8
            or NativeUmkaTypeKind.UInt16
            or NativeUmkaTypeKind.UInt32
            or NativeUmkaTypeKind.UInt
            or NativeUmkaTypeKind.Bool
            or NativeUmkaTypeKind.Char
            or NativeUmkaTypeKind.Real32
            or NativeUmkaTypeKind.Real
            or NativeUmkaTypeKind.Pointer
            or NativeUmkaTypeKind.String;

    private void ValidateArgumentRange(int index, NativeUmkaTypeKind nativeKind, UmkaValue value)
    {
        switch (nativeKind)
        {
            case NativeUmkaTypeKind.Int8:
                ValidateSignedRange(index, value.AsInt64(), sbyte.MinValue, sbyte.MaxValue);
                break;
            case NativeUmkaTypeKind.Int16:
                ValidateSignedRange(index, value.AsInt64(), short.MinValue, short.MaxValue);
                break;
            case NativeUmkaTypeKind.Int32:
                ValidateSignedRange(index, value.AsInt64(), int.MinValue, int.MaxValue);
                break;
            case NativeUmkaTypeKind.UInt8:
                ValidateUnsignedRange(index, value.AsUInt64(), byte.MaxValue);
                break;
            case NativeUmkaTypeKind.UInt16:
                ValidateUnsignedRange(index, value.AsUInt64(), ushort.MaxValue);
                break;
            case NativeUmkaTypeKind.UInt32:
                ValidateUnsignedRange(index, value.AsUInt64(), uint.MaxValue);
                break;
            case NativeUmkaTypeKind.Char when value.Kind == UmkaValueKind.Int:
                ValidateSignedRange(index, value.AsInt64(), byte.MinValue, byte.MaxValue);
                break;
            case NativeUmkaTypeKind.Char:
                ValidateUnsignedRange(index, value.AsUInt64(), byte.MaxValue);
                break;
            case NativeUmkaTypeKind.Real32:
                ValidateSingleRange(index, value.AsDouble());
                break;
        }
    }

    private void ValidateSignedRange(int index, long value, long minValue, long maxValue)
    {
        if (value < minValue || value > maxValue)
            ThrowRangeError(index, value.ToString(System.Globalization.CultureInfo.InvariantCulture), minValue, maxValue);
    }

    private void ValidateUnsignedRange(int index, ulong value, ulong maxValue)
    {
        if (value > maxValue)
            ThrowRangeError(index, value.ToString(System.Globalization.CultureInfo.InvariantCulture), 0UL, maxValue);
    }

    private void ValidateSingleRange(int index, double value)
    {
        if (UmkaSingleConversion.IsOutsideFiniteSingleRange(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                UmkaSingleConversion.Format(value),
                $"Function '{DiagnosticName}' argument {index} expects Umka type '{_parameterTypes[index].TypeName}' in finite range {UmkaSingleConversion.FiniteRangeDescription}.");
        }
    }

    private void ThrowRangeError<T>(int index, string value, T minValue, T maxValue)
    {
        throw new ArgumentOutOfRangeException(
            nameof(value),
            value,
            $"Function '{DiagnosticName}' argument {index} expects Umka type '{_parameterTypes[index].TypeName}' in range {minValue}..{maxValue}.");
    }
}
