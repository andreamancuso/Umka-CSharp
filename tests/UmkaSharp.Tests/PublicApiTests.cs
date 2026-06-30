using System.Runtime.InteropServices;
using Xunit;

namespace UmkaSharp.Tests;

public sealed class PublicApiTests
{
    [Fact]
    public void Runtime_options_defaults_are_conservative()
    {
        var options = new UmkaRuntimeOptions();

        Assert.Equal(UmkaRuntime.DefaultStackSize, options.StackSize);
        Assert.False(options.FileSystemEnabled);
        Assert.False(options.ImplementationLibrariesEnabled);
        Assert.Null(options.Arguments);
        Assert.Null(options.WarningHandler);
    }

    [Fact]
    public void Runtime_options_validate_stack_size_at_assignment()
    {
        var options = new UmkaRuntimeOptions { StackSize = 64 };

        Assert.Equal(64, options.StackSize);
        Assert.Throws<ArgumentOutOfRangeException>(() => new UmkaRuntimeOptions { StackSize = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new UmkaRuntimeOptions { StackSize = -1 });
    }

    [Fact]
    public void Runtime_options_snapshot_and_validate_command_line_arguments()
    {
        var arguments = new[] { "script.um", "alpha" };
        var options = new UmkaRuntimeOptions { Arguments = arguments };

        arguments[1] = "mutated";

        Assert.NotNull(options.Arguments);
        Assert.Equal(["script.um", "alpha"], options.Arguments);
        Assert.Throws<ArgumentException>(() => new UmkaRuntimeOptions { Arguments = [null!] });
        Assert.Throws<ArgumentException>(() => new UmkaRuntimeOptions { Arguments = ["bad\0argument"] });
    }

    [Fact]
    public void Metadata_records_preserve_positional_value_semantics()
    {
        var error = new UmkaError("main.um", "main", 10, 4, 123, "failed");
        var sameError = new UmkaError("main.um", "main", 10, 4, 123, "failed");
        var changedError = error with { Line = 11 };

        var (fileName, functionName, line, position, code, message) = error;

        Assert.Equal(sameError, error);
        Assert.NotEqual(error, changedError);
        Assert.Equal("main.um", fileName);
        Assert.Equal("main", functionName);
        Assert.Equal(10, line);
        Assert.Equal(4, position);
        Assert.Equal(123, code);
        Assert.Equal("failed", message);

        var typeInfo = new UmkaTypeInfo(UmkaTypeKind.SignedInteger, "int");
        var sameTypeInfo = new UmkaTypeInfo(UmkaTypeKind.SignedInteger, "int");
        var changedTypeInfo = typeInfo with { TypeName = "int32" };
        var describedTypeInfo = typeInfo with { NativeSize = 8, ItemCount = 3, HasReferences = true };

        var (kind, typeName) = typeInfo;

        Assert.Equal(sameTypeInfo, typeInfo);
        Assert.NotEqual(typeInfo, changedTypeInfo);
        Assert.Equal(UmkaTypeKind.SignedInteger, kind);
        Assert.Equal("int", typeName);
        Assert.Equal(0, typeInfo.NativeSize);
        Assert.Equal(0, typeInfo.ItemCount);
        Assert.False(typeInfo.HasReferences);
        Assert.Equal(8, describedTypeInfo.NativeSize);
        Assert.Equal(3, describedTypeInfo.ItemCount);
        Assert.True(describedTypeInfo.HasReferences);
        Assert.Equal("UmkaTypeInfo(Kind=SignedInteger, TypeName=int)", typeInfo.ToString());
        Assert.Equal(
            "UmkaTypeInfo(Kind=SignedInteger, TypeName=int, NativeSize=8, ItemCount=3, HasReferences=True)",
            describedTypeInfo.ToString());

        Assert.True(new UmkaTypeInfo(UmkaTypeKind.SignedInteger, "int").IsScalar);
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.UnsignedInteger, "uint").IsScalar);
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.Boolean, "bool").IsScalar);
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.Character, "char").IsScalar);
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.Real, "real").IsScalar);
        Assert.False(new UmkaTypeInfo(UmkaTypeKind.String, "str").IsScalar);

        Assert.True(new UmkaTypeInfo(UmkaTypeKind.StaticArray, "[3]int").IsAggregate);
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.Struct, "Pair").IsAggregate);
        Assert.False(new UmkaTypeInfo(UmkaTypeKind.Pointer, "^void").IsAggregate);

        Assert.True(new UmkaTypeInfo(UmkaTypeKind.Void, "void").CanReadAsValue());
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.SignedInteger, "int").CanReadAsValue());
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.String, "str").CanReadAsValue());
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.Pointer, "^void").CanReadAsValue());
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.WeakPointer, "weak ^int").CanReadAsValue());
        Assert.False(new UmkaTypeInfo(UmkaTypeKind.DynamicArray, "[]int").CanReadAsValue());

        Assert.True(new UmkaTypeInfo(UmkaTypeKind.SignedInteger, "int").CanReadAsScalar<int>());
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.Character, "char").CanReadAsScalar<byte>());
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.String, "str").CanReadAsScalar<string>());
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.Pointer, "^void").CanReadAsScalar<IntPtr>());
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.UnsignedInteger, "uint").CanReadAsScalar<UnsignedApiMode>());
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.String, "str").CanReadAsScalar<UmkaValue>());
        Assert.False(new UmkaTypeInfo(UmkaTypeKind.SignedInteger, "int").CanReadAsScalar<string>());
        Assert.False(new UmkaTypeInfo(UmkaTypeKind.DynamicArray, "[]int").CanReadAsScalar<UmkaValue>());

        var pairSize = Marshal.SizeOf<ApiPair>();
        var pairType = new UmkaTypeInfo(UmkaTypeKind.Struct, "Pair") { NativeSize = pairSize };
        var arrayType = new UmkaTypeInfo(UmkaTypeKind.StaticArray, "[2]int") { NativeSize = pairSize, ItemCount = 2 };
        var unknownLengthArrayType = arrayType with { ItemCount = 0 };
        var referencePairType = pairType with { HasReferences = true };
        var dynamicArrayType = new UmkaTypeInfo(UmkaTypeKind.DynamicArray, "[]int")
        {
            ElementKind = UmkaTypeKind.SignedInteger,
            ElementTypeName = "int",
            ElementNativeSize = Marshal.SizeOf<long>()
        };
        var mapType = new UmkaTypeInfo(UmkaTypeKind.Map, "map[int]int")
        {
            MapKeyKind = UmkaTypeKind.SignedInteger,
            MapKeyTypeName = "int",
            MapKeyNativeSize = Marshal.SizeOf<long>(),
            MapValueKind = UmkaTypeKind.SignedInteger,
            MapValueTypeName = "int",
            MapValueNativeSize = Marshal.SizeOf<long>()
        };
        var stringKeyMapType = mapType with
        {
            TypeName = "map[str]int",
            MapKeyKind = UmkaTypeKind.String,
            MapKeyTypeName = "str",
            MapKeyNativeSize = IntPtr.Size,
            MapKeyHasReferences = true
        };
        var stringValueMapType = mapType with
        {
            TypeName = "map[int]str",
            MapValueKind = UmkaTypeKind.String,
            MapValueTypeName = "str",
            MapValueNativeSize = IntPtr.Size,
            MapValueHasReferences = true
        };
        var stringMapType = stringKeyMapType with
        {
            TypeName = "map[str]str",
            MapValueKind = UmkaTypeKind.String,
            MapValueTypeName = "str",
            MapValueNativeSize = IntPtr.Size,
            MapValueHasReferences = true
        };
        var dynamicArrayValueMapType = mapType with
        {
            TypeName = "map[int][]int",
            MapValueKind = UmkaTypeKind.DynamicArray,
            MapValueTypeName = "[]int",
            MapValueNativeSize = IntPtr.Size * 3,
            MapValueHasReferences = true,
            MapValueElementKind = UmkaTypeKind.SignedInteger,
            MapValueElementTypeName = "int",
            MapValueElementNativeSize = Marshal.SizeOf<long>()
        };
        var stringKeyDynamicArrayValueMapType = stringKeyMapType with
        {
            TypeName = "map[str][]int",
            MapValueKind = UmkaTypeKind.DynamicArray,
            MapValueTypeName = "[]int",
            MapValueNativeSize = IntPtr.Size * 3,
            MapValueHasReferences = true,
            MapValueElementKind = UmkaTypeKind.SignedInteger,
            MapValueElementTypeName = "int",
            MapValueElementNativeSize = Marshal.SizeOf<long>()
        };
        var stringArrayValueMapType = dynamicArrayValueMapType with
        {
            TypeName = "map[int][]str",
            MapValueTypeName = "[]str",
            MapValueElementKind = UmkaTypeKind.String,
            MapValueElementTypeName = "str",
            MapValueElementNativeSize = IntPtr.Size,
            MapValueElementHasReferences = true
        };
        var stringKeyStringArrayValueMapType = stringKeyDynamicArrayValueMapType with
        {
            TypeName = "map[str][]str",
            MapValueTypeName = "[]str",
            MapValueElementKind = UmkaTypeKind.String,
            MapValueElementTypeName = "str",
            MapValueElementNativeSize = IntPtr.Size,
            MapValueElementHasReferences = true
        };
        var unsupportedReferenceMapType = dynamicArrayValueMapType with
        {
            TypeName = "map[int][]any",
            MapValueTypeName = "[]any",
            MapValueElementKind = UmkaTypeKind.Interface,
            MapValueElementTypeName = "any",
            MapValueElementNativeSize = IntPtr.Size * 2,
            MapValueElementHasReferences = true
        };
        var referenceDynamicArrayType = dynamicArrayType with
        {
            TypeName = "[]str",
            ElementKind = UmkaTypeKind.String,
            ElementTypeName = "str",
            ElementNativeSize = IntPtr.Size,
            ElementHasReferences = true
        };
        var nestedDynamicArrayType = dynamicArrayType with
        {
            TypeName = "[][]int",
            ElementKind = UmkaTypeKind.DynamicArray,
            ElementTypeName = "[]int",
            ElementNativeSize = IntPtr.Size * 3,
            ElementHasReferences = true,
            NestedElementKind = UmkaTypeKind.SignedInteger,
            NestedElementTypeName = "int",
            NestedElementNativeSize = Marshal.SizeOf<long>()
        };
        var nestedReferenceDynamicArrayType = nestedDynamicArrayType with
        {
            TypeName = "[][]str",
            ElementTypeName = "[]str",
            NestedElementKind = UmkaTypeKind.String,
            NestedElementTypeName = "str",
            NestedElementNativeSize = IntPtr.Size,
            NestedElementHasReferences = true
        };

        Assert.True(pairType.CanReadAsStruct<ApiPair>());
        Assert.True(pairType.CanReadAsFixedLayout<ApiPair>());
        Assert.False(pairType.CanReadAsArray<long>(2));
        Assert.False(pairType.CanReadAsStruct<ApiPairWithReference>());
        Assert.False(referencePairType.CanReadAsFixedLayout<ApiPair>());

        Assert.True(arrayType.CanReadAsArray<long>(2));
        Assert.True(arrayType.CanReadAsFixedLayout<ApiPair>());
        Assert.False(arrayType.CanReadAsStruct<ApiPair>());
        Assert.False(arrayType.CanReadAsArray<long>(3));
        Assert.True(unknownLengthArrayType.CanReadAsArray<long>(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => arrayType.CanReadAsArray<long>(-1));

        Assert.False(new UmkaTypeInfo(UmkaTypeKind.DynamicArray, "[]int").CanReadAsDynamicArray<long>());
        Assert.True(dynamicArrayType.CanReadAsDynamicArray<long>());
        Assert.False(dynamicArrayType.CanReadAsDynamicArray<int>());
        Assert.False(referenceDynamicArrayType.CanReadAsDynamicArray<IntPtr>());
        Assert.True(referenceDynamicArrayType.CanReadAsStringArray());
        Assert.False(dynamicArrayType.CanReadAsStringArray());
        Assert.True(nestedDynamicArrayType.CanReadAsNestedDynamicArray<long>());
        Assert.False(nestedDynamicArrayType.CanReadAsNestedDynamicArray<int>());
        Assert.False(dynamicArrayType.CanReadAsNestedDynamicArray<long>());
        Assert.False(nestedReferenceDynamicArrayType.CanReadAsStringArray());
        Assert.True(nestedReferenceDynamicArrayType.CanReadAsNestedStringArray());
        Assert.False(dynamicArrayType.CanReadAsNestedStringArray());
        Assert.False(dynamicArrayType.IsDeferred);
        Assert.False(referenceDynamicArrayType.IsDeferred);
        Assert.False(nestedDynamicArrayType.IsDeferred);
        Assert.False(nestedReferenceDynamicArrayType.CanReadAsNestedDynamicArray<IntPtr>());
        Assert.False(nestedReferenceDynamicArrayType.IsDeferred);
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.DynamicArray, "[]int").IsDeferred);
        Assert.True(mapType.CanReadAsMap<long, long>());
        Assert.False(mapType.CanReadAsMap<int, long>());
        Assert.False(stringKeyMapType.CanReadAsMap<IntPtr, long>());
        Assert.True(stringKeyMapType.CanReadAsStringKeyMap<long>());
        Assert.False(stringKeyMapType.CanReadAsStringKeyMap<int>());
        Assert.False(stringKeyMapType.CanReadAsStringValueMap<long>());
        Assert.False(stringKeyMapType.CanReadAsStringMap());
        Assert.True(stringValueMapType.CanReadAsStringValueMap<long>());
        Assert.False(stringValueMapType.CanReadAsStringValueMap<int>());
        Assert.False(stringValueMapType.CanReadAsStringKeyMap<long>());
        Assert.False(stringValueMapType.CanReadAsStringMap());
        Assert.True(stringMapType.CanReadAsStringMap());
        Assert.False(stringMapType.CanReadAsStringKeyMap<long>());
        Assert.False(unsupportedReferenceMapType.CanReadAsStringValueMap<long>());
        Assert.True(dynamicArrayValueMapType.CanReadAsDynamicArrayValueMap<long, long>());
        Assert.False(dynamicArrayValueMapType.CanReadAsDynamicArrayValueMap<int, long>());
        Assert.False(dynamicArrayValueMapType.CanReadAsDynamicArrayValueMap<long, int>());
        Assert.False(dynamicArrayValueMapType.CanReadAsMap<long, IntPtr>());
        Assert.True(stringKeyDynamicArrayValueMapType.CanReadAsStringKeyDynamicArrayValueMap<long>());
        Assert.False(stringKeyDynamicArrayValueMapType.CanReadAsStringKeyDynamicArrayValueMap<int>());
        Assert.False(unsupportedReferenceMapType.CanReadAsDynamicArrayValueMap<long, IntPtr>());
        Assert.True(stringArrayValueMapType.CanReadAsStringArrayValueMap<long>());
        Assert.False(stringArrayValueMapType.CanReadAsStringArrayValueMap<int>());
        Assert.False(stringArrayValueMapType.CanReadAsDynamicArrayValueMap<long, IntPtr>());
        Assert.True(stringKeyStringArrayValueMapType.CanReadAsStringKeyStringArrayValueMap());
        Assert.False(dynamicArrayValueMapType.CanReadAsStringKeyStringArrayValueMap());
        Assert.False(mapType.IsDeferred);
        Assert.False(stringKeyMapType.IsDeferred);
        Assert.False(stringValueMapType.IsDeferred);
        Assert.False(stringMapType.IsDeferred);
        Assert.False(dynamicArrayValueMapType.IsDeferred);
        Assert.False(stringKeyDynamicArrayValueMapType.IsDeferred);
        Assert.False(stringArrayValueMapType.IsDeferred);
        Assert.False(stringKeyStringArrayValueMapType.IsDeferred);
        Assert.True(unsupportedReferenceMapType.IsDeferred);
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.Map, "map[int]int").IsDeferred);
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.WeakPointer, "weak ^int").CanReadAsWeakPointer());
        Assert.False(new UmkaTypeInfo(UmkaTypeKind.Pointer, "^int").CanReadAsWeakPointer());
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.Interface, "any").IsDeferred);
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.Closure, "fn()").IsDeferred);
        Assert.False(new UmkaTypeInfo(UmkaTypeKind.WeakPointer, "weak ^int").IsDeferred);
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.Fiber, "fiber").IsDeferred);
        Assert.True(new UmkaTypeInfo(UmkaTypeKind.Function, "fn()").IsDeferred);
        Assert.False(new UmkaTypeInfo(UmkaTypeKind.Pointer, "^void").IsDeferred);
    }

    [Fact]
    public void Type_info_rejects_invalid_public_metadata_values()
    {
        var typeInfo = new UmkaTypeInfo(UmkaTypeKind.SignedInteger, "int");

        Assert.Throws<ArgumentNullException>(() => new UmkaTypeInfo(UmkaTypeKind.Unknown, null!));
        Assert.Throws<ArgumentException>(() => new UmkaTypeInfo(UmkaTypeKind.Unknown, ""));
        Assert.Throws<ArgumentException>(() => new UmkaTypeInfo(UmkaTypeKind.Unknown, "   "));
        Assert.Throws<ArgumentException>(() => new UmkaTypeInfo(UmkaTypeKind.Unknown, "bad\0type"));
        Assert.Throws<ArgumentException>(() => typeInfo with { TypeName = "" });
        Assert.Throws<ArgumentException>(() => typeInfo with { TypeName = "bad\0type" });
        Assert.Throws<ArgumentException>(() => typeInfo with { ElementTypeName = "" });
        Assert.Throws<ArgumentException>(() => typeInfo with { ElementTypeName = "bad\0type" });
        Assert.Throws<ArgumentException>(() => typeInfo with { NestedElementTypeName = "" });
        Assert.Throws<ArgumentException>(() => typeInfo with { NestedElementTypeName = "bad\0type" });
        Assert.Throws<ArgumentException>(() => typeInfo with { MapKeyTypeName = "" });
        Assert.Throws<ArgumentException>(() => typeInfo with { MapKeyTypeName = "bad\0type" });
        Assert.Throws<ArgumentException>(() => typeInfo with { MapValueTypeName = "" });
        Assert.Throws<ArgumentException>(() => typeInfo with { MapValueTypeName = "bad\0type" });
        Assert.Throws<ArgumentException>(() => typeInfo with { MapValueElementTypeName = "" });
        Assert.Throws<ArgumentException>(() => typeInfo with { MapValueElementTypeName = "bad\0type" });
        Assert.Throws<ArgumentOutOfRangeException>(() => typeInfo with { NativeSize = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => typeInfo with { ItemCount = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => typeInfo with { ElementNativeSize = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => typeInfo with { NestedElementNativeSize = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => typeInfo with { MapKeyNativeSize = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => typeInfo with { MapValueNativeSize = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => typeInfo with { MapValueElementNativeSize = -1 });
    }

    [Fact]
    public void Error_metadata_rejects_invalid_public_values()
    {
        var error = new UmkaError("main.um", "main", 10, 4, 123, "failed");

        Assert.Throws<ArgumentException>(() => new UmkaError("bad\0file.um", null, 0, 0, 0, null));
        Assert.Throws<ArgumentException>(() => new UmkaError(null, "bad\0function", 0, 0, 0, null));
        Assert.Throws<ArgumentException>(() => new UmkaError(null, null, 0, 0, 0, "bad\0message"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UmkaError(null, null, -1, 0, 0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UmkaError(null, null, 0, -1, 0, null));
        Assert.Throws<ArgumentException>(() => error with { FileName = "bad\0file.um" });
        Assert.Throws<ArgumentException>(() => error with { FunctionName = "bad\0function" });
        Assert.Throws<ArgumentException>(() => error with { Message = "bad\0message" });
        Assert.Throws<ArgumentOutOfRangeException>(() => error with { Line = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => error with { Position = -1 });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ApiPair
    {
        public long X;
        public long Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ApiPairWithReference
    {
        public string? Value;
    }

    private enum UnsignedApiMode : byte
    {
        One = 1
    }
}
