namespace UmkaSharp;

using System.Runtime.InteropServices;

/// <summary>A compiled Umka function callable from C#.</summary>
public sealed class UmkaFunction
{
    private readonly UmkaRuntime _runtime;
    private NativeMethods.FunctionContext _context;

    internal UmkaFunction(UmkaRuntime runtime, string name, NativeMethods.FunctionContext context)
    {
        _runtime = runtime;
        Name = name;
        _context = context;
    }

    /// <summary>Gets the function name used to resolve this callable.</summary>
    public string Name { get; }

    /// <summary>Calls the function and ignores any result value.</summary>
    public void CallVoid(params UmkaValue[] arguments)
    {
        Invoke(arguments);
    }

    /// <summary>Calls the function and returns its result as a signed integer.</summary>
    public long CallInt64(params UmkaValue[] arguments)
    {
        Invoke(arguments);
        return NativeMethods.ContextGetResultInt(ref _context);
    }

    /// <summary>Calls the function and returns its result as an unsigned integer.</summary>
    public ulong CallUInt64(params UmkaValue[] arguments)
    {
        Invoke(arguments);
        return NativeMethods.ContextGetResultUInt(ref _context);
    }

    /// <summary>Calls the function and returns its result as a real number.</summary>
    public double CallDouble(params UmkaValue[] arguments)
    {
        Invoke(arguments);
        return NativeMethods.ContextGetResultReal(ref _context);
    }

    /// <summary>Calls the function and returns its result as a Boolean.</summary>
    public bool CallBoolean(params UmkaValue[] arguments) => CallInt64(arguments) != 0;

    /// <summary>Calls the function and returns its result as a string.</summary>
    public string? CallString(params UmkaValue[] arguments)
    {
        Invoke(arguments);
        return NativeMethods.ContextGetResultString(ref _context).ToManagedString();
    }

    /// <summary>Calls the function and returns its result as a pointer.</summary>
    public IntPtr CallPointer(params UmkaValue[] arguments)
    {
        Invoke(arguments);
        return NativeMethods.ContextGetResultPointer(ref _context);
    }

    /// <summary>Calls the function and marshals a structured result into a managed struct.</summary>
    public T CallStruct<T>(params UmkaValue[] arguments) where T : struct
    {
        _runtime.CheckUsable();

        var nativeSize = NativeMethods.ContextGetResultSize(ref _context);
        if (nativeSize <= 0)
            throw new InvalidOperationException("The function does not return a structured value.");

        var managedSize = Marshal.SizeOf<T>();
        if (managedSize < nativeSize)
            throw new InvalidOperationException(
                $"Managed result type {typeof(T).FullName} is {managedSize} bytes, but Umka requires {nativeSize} bytes.");

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

    private void Invoke(ReadOnlySpan<UmkaValue> arguments)
    {
        _runtime.CheckUsable();
        for (var i = 0; i < arguments.Length; i++)
            SetArgument(i, arguments[i]);

        var status = NativeMethods.Call(_runtime.Handle, ref _context);
        _runtime.ThrowIfError(status);
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
            _ => throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "Unsupported value kind.")
        };

        _runtime.ThrowIfError(status);
    }
}
