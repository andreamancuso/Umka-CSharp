namespace UmkaSharp;

/// <summary>Managed view over an Umka external callback frame.</summary>
public readonly struct UmkaCallFrame
{
    private readonly IntPtr _parameters;
    private readonly IntPtr _result;

    internal UmkaCallFrame(IntPtr parameters, IntPtr result)
    {
        _parameters = parameters;
        _result = result;
    }

    /// <summary>Reads a signed integer callback argument.</summary>
    public long GetInt64(int index) => NativeMethods.CallbackGetParamInt(_parameters, index);

    /// <summary>Reads an unsigned integer callback argument.</summary>
    public ulong GetUInt64(int index) => NativeMethods.CallbackGetParamUInt(_parameters, index);

    /// <summary>Reads a real callback argument.</summary>
    public double GetDouble(int index) => NativeMethods.CallbackGetParamReal(_parameters, index);

    /// <summary>Reads a Boolean callback argument.</summary>
    public bool GetBoolean(int index) => GetInt64(index) != 0;

    /// <summary>Reads a pointer callback argument.</summary>
    public IntPtr GetPointer(int index) => NativeMethods.CallbackGetParamPointer(_parameters, index);

    /// <summary>Reads a string callback argument.</summary>
    public string? GetString(int index) => NativeMethods.CallbackGetParamString(_parameters, index).ToManagedString();

    internal void SetResult(UmkaValue value)
    {
        switch (value.Kind)
        {
            case UmkaValueKind.Void:
                break;
            case UmkaValueKind.Int:
                NativeMethods.CallbackSetResultInt(_parameters, _result, value.AsInt64());
                break;
            case UmkaValueKind.UInt:
                NativeMethods.CallbackSetResultUInt(_parameters, _result, value.AsUInt64());
                break;
            case UmkaValueKind.Real:
                NativeMethods.CallbackSetResultReal(_parameters, _result, value.AsDouble());
                break;
            case UmkaValueKind.Bool:
                NativeMethods.CallbackSetResultInt(_parameters, _result, value.AsBoolean() ? 1 : 0);
                break;
            case UmkaValueKind.String:
                NativeMethods.CallbackSetResultString(_parameters, _result, value.AsString());
                break;
            case UmkaValueKind.Pointer:
                NativeMethods.CallbackSetResultPointer(_parameters, _result, value.AsPointer());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "Unsupported callback result kind.");
        }
    }
}
