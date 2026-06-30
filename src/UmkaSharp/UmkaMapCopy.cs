namespace UmkaSharp;

using System.Runtime.InteropServices;

internal static class UmkaMapCopy
{
    public static Dictionary<TKey, TValue> Copy<TKey, TValue>(
        int count,
        int keySize,
        int valueSize,
        Func<IntPtr, int, IntPtr, int, int> copyEntries)
        where TKey : struct
        where TValue : struct
    {
        if (count < 0)
            throw new InvalidOperationException("The Umka map is not readable.");
        if (count == 0)
            return new Dictionary<TKey, TValue>();

        var keyBytes = checked(count * keySize);
        var valueBytes = checked(count * valueSize);
        var keyBuffer = Marshal.AllocHGlobal(keyBytes);
        var valueBuffer = Marshal.AllocHGlobal(valueBytes);
        try
        {
            var status = copyEntries(keyBuffer, keyBytes, valueBuffer, valueBytes);
            if (status != 0)
                throw new InvalidOperationException("The Umka map could not be copied into managed storage.");

            var result = new Dictionary<TKey, TValue>(count);
            for (var i = 0; i < count; i++)
            {
                var key = Marshal.PtrToStructure<TKey>(IntPtr.Add(keyBuffer, i * keySize));
                var value = Marshal.PtrToStructure<TValue>(IntPtr.Add(valueBuffer, i * valueSize));
                result[key] = value;
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(keyBuffer);
            Marshal.FreeHGlobal(valueBuffer);
        }
    }

    public static Dictionary<string, TValue> CopyStringKeys<TValue>(
        int count,
        int valueSize,
        Func<IntPtr, int, IntPtr, int, int> copyEntries)
        where TValue : struct
    {
        if (count < 0)
            throw new InvalidOperationException("The Umka map is not readable.");
        if (count == 0)
            return new Dictionary<string, TValue>(StringComparer.Ordinal);

        var keyBytes = checked(count * IntPtr.Size);
        var valueBytes = checked(count * valueSize);
        var keyBuffer = Marshal.AllocHGlobal(keyBytes);
        var valueBuffer = Marshal.AllocHGlobal(valueBytes);
        try
        {
            var status = copyEntries(keyBuffer, count, valueBuffer, valueBytes);
            if (status != 0)
                throw new InvalidOperationException("The Umka map could not be copied into managed storage.");

            var result = new Dictionary<string, TValue>(count, StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var key = ReadStringKey(keyBuffer, i);
                var value = Marshal.PtrToStructure<TValue>(IntPtr.Add(valueBuffer, i * valueSize));
                result[key] = value;
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(keyBuffer);
            Marshal.FreeHGlobal(valueBuffer);
        }
    }

    public static Dictionary<TKey, string?> CopyStringValues<TKey>(
        int count,
        int keySize,
        Func<IntPtr, int, IntPtr, int, int> copyEntries)
        where TKey : struct
    {
        if (count < 0)
            throw new InvalidOperationException("The Umka map is not readable.");
        if (count == 0)
            return new Dictionary<TKey, string?>();

        var keyBytes = checked(count * keySize);
        var valueBytes = checked(count * IntPtr.Size);
        var keyBuffer = Marshal.AllocHGlobal(keyBytes);
        var valueBuffer = Marshal.AllocHGlobal(valueBytes);
        try
        {
            var status = copyEntries(keyBuffer, keyBytes, valueBuffer, count);
            if (status != 0)
                throw new InvalidOperationException("The Umka map could not be copied into managed storage.");

            var result = new Dictionary<TKey, string?>(count);
            for (var i = 0; i < count; i++)
            {
                var key = Marshal.PtrToStructure<TKey>(IntPtr.Add(keyBuffer, i * keySize));
                var value = Marshal.ReadIntPtr(valueBuffer, i * IntPtr.Size).ToManagedString();
                result[key] = value;
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(keyBuffer);
            Marshal.FreeHGlobal(valueBuffer);
        }
    }

    public static Dictionary<string, string?> CopyStrings(
        int count,
        Func<IntPtr, int, IntPtr, int, int> copyEntries)
    {
        if (count < 0)
            throw new InvalidOperationException("The Umka map is not readable.");
        if (count == 0)
            return new Dictionary<string, string?>(StringComparer.Ordinal);

        var byteCount = checked(count * IntPtr.Size);
        var keyBuffer = Marshal.AllocHGlobal(byteCount);
        var valueBuffer = Marshal.AllocHGlobal(byteCount);
        try
        {
            var status = copyEntries(keyBuffer, count, valueBuffer, count);
            if (status != 0)
                throw new InvalidOperationException("The Umka map could not be copied into managed storage.");

            var result = new Dictionary<string, string?>(count, StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var key = ReadStringKey(keyBuffer, i);
                var value = Marshal.ReadIntPtr(valueBuffer, i * IntPtr.Size).ToManagedString();
                result[key] = value;
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(keyBuffer);
            Marshal.FreeHGlobal(valueBuffer);
        }
    }

    public static Dictionary<TKey, TElement[]> CopyDynamicArrayValues<TKey, TElement>(
        int count,
        int keySize,
        int elementSize,
        Func<IntPtr, int, IntPtr, int, int, int> copyEntries,
        Func<int, IntPtr, int, int, int> copyValueData)
        where TKey : struct
        where TElement : struct
    {
        if (count < 0)
            throw new InvalidOperationException("The Umka map is not readable.");
        if (count == 0)
            return new Dictionary<TKey, TElement[]>();

        var keyBytes = checked(count * keySize);
        var lengthBytes = checked(count * sizeof(int));
        var keyBuffer = Marshal.AllocHGlobal(keyBytes);
        var lengthBuffer = Marshal.AllocHGlobal(lengthBytes);
        try
        {
            var status = copyEntries(keyBuffer, keyBytes, lengthBuffer, count, elementSize);
            if (status != 0)
                throw new InvalidOperationException("The Umka map could not be copied into managed storage.");

            var lengths = new int[count];
            Marshal.Copy(lengthBuffer, lengths, 0, lengths.Length);

            var result = new Dictionary<TKey, TElement[]>(count);
            for (var i = 0; i < count; i++)
            {
                var key = Marshal.PtrToStructure<TKey>(IntPtr.Add(keyBuffer, i * keySize));
                result[key] = CopyDynamicArrayValue<TElement>(i, lengths[i], elementSize, copyValueData);
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(keyBuffer);
            Marshal.FreeHGlobal(lengthBuffer);
        }
    }

    public static Dictionary<string, TElement[]> CopyStringKeyDynamicArrayValues<TElement>(
        int count,
        int elementSize,
        Func<IntPtr, int, IntPtr, int, int, int> copyEntries,
        Func<int, IntPtr, int, int, int> copyValueData)
        where TElement : struct
    {
        if (count < 0)
            throw new InvalidOperationException("The Umka map is not readable.");
        if (count == 0)
            return new Dictionary<string, TElement[]>(StringComparer.Ordinal);

        var keyBytes = checked(count * IntPtr.Size);
        var lengthBytes = checked(count * sizeof(int));
        var keyBuffer = Marshal.AllocHGlobal(keyBytes);
        var lengthBuffer = Marshal.AllocHGlobal(lengthBytes);
        try
        {
            var status = copyEntries(keyBuffer, count, lengthBuffer, count, elementSize);
            if (status != 0)
                throw new InvalidOperationException("The Umka map could not be copied into managed storage.");

            var lengths = new int[count];
            Marshal.Copy(lengthBuffer, lengths, 0, lengths.Length);

            var result = new Dictionary<string, TElement[]>(count, StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var key = ReadStringKey(keyBuffer, i);
                result[key] = CopyDynamicArrayValue<TElement>(i, lengths[i], elementSize, copyValueData);
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(keyBuffer);
            Marshal.FreeHGlobal(lengthBuffer);
        }
    }

    public static Dictionary<TKey, string?[]> CopyStringArrayValues<TKey>(
        int count,
        int keySize,
        Func<IntPtr, int, IntPtr, int, int> copyEntries,
        Func<int, IntPtr, int, int> copyValueData)
        where TKey : struct
    {
        if (count < 0)
            throw new InvalidOperationException("The Umka map is not readable.");
        if (count == 0)
            return new Dictionary<TKey, string?[]>();

        var keyBytes = checked(count * keySize);
        var lengthBytes = checked(count * sizeof(int));
        var keyBuffer = Marshal.AllocHGlobal(keyBytes);
        var lengthBuffer = Marshal.AllocHGlobal(lengthBytes);
        try
        {
            var status = copyEntries(keyBuffer, keyBytes, lengthBuffer, count);
            if (status != 0)
                throw new InvalidOperationException("The Umka map could not be copied into managed storage.");

            var lengths = new int[count];
            Marshal.Copy(lengthBuffer, lengths, 0, lengths.Length);

            var result = new Dictionary<TKey, string?[]>(count);
            for (var i = 0; i < count; i++)
            {
                var key = Marshal.PtrToStructure<TKey>(IntPtr.Add(keyBuffer, i * keySize));
                result[key] = CopyStringArrayValue(i, lengths[i], copyValueData);
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(keyBuffer);
            Marshal.FreeHGlobal(lengthBuffer);
        }
    }

    public static Dictionary<string, string?[]> CopyStringKeyStringArrayValues(
        int count,
        Func<IntPtr, int, IntPtr, int, int> copyEntries,
        Func<int, IntPtr, int, int> copyValueData)
    {
        if (count < 0)
            throw new InvalidOperationException("The Umka map is not readable.");
        if (count == 0)
            return new Dictionary<string, string?[]>(StringComparer.Ordinal);

        var keyBytes = checked(count * IntPtr.Size);
        var lengthBytes = checked(count * sizeof(int));
        var keyBuffer = Marshal.AllocHGlobal(keyBytes);
        var lengthBuffer = Marshal.AllocHGlobal(lengthBytes);
        try
        {
            var status = copyEntries(keyBuffer, count, lengthBuffer, count);
            if (status != 0)
                throw new InvalidOperationException("The Umka map could not be copied into managed storage.");

            var lengths = new int[count];
            Marshal.Copy(lengthBuffer, lengths, 0, lengths.Length);

            var result = new Dictionary<string, string?[]>(count, StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var key = ReadStringKey(keyBuffer, i);
                result[key] = CopyStringArrayValue(i, lengths[i], copyValueData);
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(keyBuffer);
            Marshal.FreeHGlobal(lengthBuffer);
        }
    }

    private static TElement[] CopyDynamicArrayValue<TElement>(
        int entryIndex,
        int length,
        int elementSize,
        Func<int, IntPtr, int, int, int> copyValueData)
        where TElement : struct
    {
        if (length < 0)
            throw new InvalidOperationException("The Umka map contains an unreadable dynamic-array value.");
        if (length == 0)
            return Array.Empty<TElement>();

        var byteSize = checked(length * elementSize);
        var buffer = Marshal.AllocHGlobal(byteSize);
        try
        {
            var status = copyValueData(entryIndex, buffer, byteSize, elementSize);
            if (status != 0)
                throw new InvalidOperationException("The Umka map dynamic-array value could not be copied into managed storage.");

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

    private static string?[] CopyStringArrayValue(
        int entryIndex,
        int length,
        Func<int, IntPtr, int, int> copyValueData)
    {
        if (length < 0)
            throw new InvalidOperationException("The Umka map contains an unreadable string-array value.");
        if (length == 0)
            return Array.Empty<string?>();

        var byteSize = checked(length * IntPtr.Size);
        var buffer = Marshal.AllocHGlobal(byteSize);
        try
        {
            var status = copyValueData(entryIndex, buffer, length);
            if (status != 0)
                throw new InvalidOperationException("The Umka map string-array value could not be copied into managed storage.");

            var result = new string?[length];
            for (var i = 0; i < result.Length; i++)
                result[i] = Marshal.ReadIntPtr(buffer, i * IntPtr.Size).ToManagedString();
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string ReadStringKey(IntPtr buffer, int index)
    {
        var key = Marshal.ReadIntPtr(buffer, index * IntPtr.Size).ToManagedString();
        return key ?? throw new InvalidOperationException(
            "The Umka map contains a null string key, which cannot be copied into a managed dictionary.");
    }
}
