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
    private NativeMethods.FunctionContext _context;

    internal UmkaFunction(
        UmkaRuntime runtime,
        string name,
        string? moduleName,
        NativeMethods.FunctionContext context,
        UmkaTypeInfo[] parameterTypes,
        NativeUmkaTypeKind[] nativeParameterKinds,
        UmkaTypeInfo resultType)
    {
        _runtime = runtime;
        _parameterTypes = parameterTypes;
        _publicParameterTypes = Array.AsReadOnly(parameterTypes);
        _nativeParameterKinds = nativeParameterKinds;
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

    /// <summary>Gets the explicit parameter types expected by this function.</summary>
    public IReadOnlyList<UmkaTypeInfo> ParameterTypes => _publicParameterTypes;

    /// <summary>Gets the function result type.</summary>
    public UmkaTypeInfo ResultType { get; }

    private string DiagnosticName => QualifiedName;

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

    /// <summary>Returns whether the supplied arguments match the resolved function parameters without executing the function.</summary>
    public bool CanCallWith(params UmkaValue[] arguments) => CanCallWith(ToArgumentSpan(arguments));

    /// <summary>Returns whether the supplied arguments match the resolved function parameters without executing the function.</summary>
    public bool CanCallWith(ReadOnlySpan<UmkaValue> arguments)
    {
        if (arguments.Length != ParameterCount)
            return false;

        for (var i = 0; i < arguments.Length; i++)
        {
            if (!CanAcceptArgument(i, arguments[i]))
                return false;
        }

        return true;
    }

    /// <summary>Calls the function and returns a dynamic value for supported scalar, string, pointer, or void results.</summary>
    public UmkaValue CallValue(params UmkaValue[] arguments) => CallValue(ToArgumentSpan(arguments));

    /// <summary>Calls the function and returns a dynamic value for supported scalar, string, pointer, or void results.</summary>
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
            _ => throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka type '{ResultType.TypeName}', which cannot be read as a dynamic UmkaValue.")
        };
    }

    /// <summary>Tries to call the function and read a dynamic value for supported scalar, string, pointer, or void results.</summary>
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

    /// <summary>Tries to call the function and read a dynamic value for supported scalar, string, pointer, or void results.</summary>
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
        if (ResultType.Kind is UmkaTypeKind.StaticArray or UmkaTypeKind.Struct)
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka type '{ResultType.TypeName}', which must be read with CallStruct<T>() or CallArray<TElement>().");

        Invoke(arguments);
    }

    /// <summary>Tries to call the function and ignore any scalar or void result value.</summary>
    public bool TryCallVoid(params UmkaValue[] arguments) => TryCallVoid(ToArgumentSpan(arguments));

    /// <summary>Tries to call the function and ignore any scalar or void result value.</summary>
    public bool TryCallVoid(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        if (ResultType.Kind is UmkaTypeKind.StaticArray or UmkaTypeKind.Struct)
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

    private void Invoke(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckCallable();
        ValidateArguments(arguments);

        for (var i = 0; i < arguments.Length; i++)
            SetArgument(i, arguments[i]);

        _runtime.ClearLastCallbackException();
        var status = NativeMethods.Call(_runtime.Handle, ref _context);
        _runtime.ThrowIfExecutionError(status);
    }

    private void ValidateArguments(ReadOnlySpan<UmkaValue> arguments)
    {
        if (arguments.Length != ParameterCount)
            throw new ArgumentException(
                $"Function '{DiagnosticName}' expects {ParameterCount} argument(s), but {arguments.Length} were provided.",
                nameof(arguments));

        for (var i = 0; i < arguments.Length; i++)
            ValidateArgument(i, arguments[i]);
    }

    private static ReadOnlySpan<UmkaValue> ToArgumentSpan(UmkaValue[] arguments) =>
        arguments ?? throw new ArgumentNullException(nameof(arguments));

    private static T BoxScalar<T>(object? value) => (T)value!;

    private bool CanAcceptArgument(int index, UmkaValue value)
    {
        if (value.Kind == UmkaValueKind.Void)
            return false;

        var nativeKind = _nativeParameterKinds[index];
        if (!IsSupportedArgumentKind(nativeKind) || !IsCompatibleArgumentKind(nativeKind, value.Kind))
            return false;

        return value.Kind is UmkaValueKind.StaticArray or UmkaValueKind.Struct
            ? CanAcceptStructuredArgument(index, nativeKind, value)
            : IsArgumentInRange(nativeKind, value);
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
            UmkaValueKind.StaticArray or UmkaValueKind.Struct => SetStructuredArgument(index, value),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "Unsupported value kind.")
        };

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

    private void ValidateArgument(int index, UmkaValue value)
    {
        if (value.Kind == UmkaValueKind.Void)
            throw new ArgumentException("Void cannot be used as a function argument.", nameof(value));

        var nativeKind = _nativeParameterKinds[index];
        if (!IsSupportedArgumentKind(nativeKind))
        {
            throw new ArgumentException(
                $"Function '{DiagnosticName}' argument {index} expects Umka type '{_parameterTypes[index].TypeName}', which UmkaSharp does not support as a call argument.",
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

        ValidateArgumentRange(index, nativeKind, value);
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

    private void EnsureReadableStructuredResult()
    {
        if (ResultType.HasReferences)
        {
            throw new InvalidOperationException(
                $"Function '{DiagnosticName}' returns Umka type '{ResultType.TypeName}', which contains Umka-managed references and cannot be copied into a managed aggregate value.");
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
            or NativeUmkaTypeKind.String
            or NativeUmkaTypeKind.StaticArray
            or NativeUmkaTypeKind.Struct;

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
            NativeUmkaTypeKind.String => valueKind == UmkaValueKind.String,
            NativeUmkaTypeKind.StaticArray => valueKind == UmkaValueKind.StaticArray,
            NativeUmkaTypeKind.Struct => valueKind == UmkaValueKind.Struct,
            _ => false
        };

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
