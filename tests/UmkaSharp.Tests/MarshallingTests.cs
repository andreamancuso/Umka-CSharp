using System.Runtime.InteropServices;
using Xunit;

namespace UmkaSharp.Tests;

public sealed class MarshallingTests
{
    [Fact]
    public void UmkaValue_creates_expected_kinds_for_common_numeric_types()
    {
        AssertValue(UmkaValue.From((sbyte)-8), UmkaValueKind.Int, -8L);
        AssertValue(UmkaValue.From((short)-16), UmkaValueKind.Int, -16L);
        AssertValue(UmkaValue.From(-32), UmkaValueKind.Int, -32L);
        AssertValue(UmkaValue.From(-64L), UmkaValueKind.Int, -64L);

        AssertValue(UmkaValue.From((byte)8), UmkaValueKind.UInt, 8UL);
        AssertValue(UmkaValue.From((ushort)16), UmkaValueKind.UInt, 16UL);
        AssertValue(UmkaValue.From(32U), UmkaValueKind.UInt, 32UL);
        AssertValue(UmkaValue.From(64UL), UmkaValueKind.UInt, 64UL);
        AssertValue(UmkaValue.From('A'), UmkaValueKind.UInt, 65UL);

        var real32 = UmkaValue.From(1.25f);
        Assert.Equal(UmkaValueKind.Real, real32.Kind);
        Assert.Equal(1.25d, real32.AsDouble());

        var real64 = UmkaValue.From(2.5d);
        Assert.Equal(UmkaValueKind.Real, real64.Kind);
        Assert.Equal(2.5d, real64.AsDouble());
    }

    [Fact]
    public void UmkaValue_reads_narrow_values_with_checked_helpers()
    {
        var signed = UmkaValue.From(127L);
        Assert.Equal((sbyte)127, signed.AsSByte());
        Assert.Equal((short)127, signed.AsInt16());
        Assert.Equal(127, signed.AsInt32());

        var unsigned = UmkaValue.From(255UL);
        Assert.Equal((byte)255, unsigned.AsByte());
        Assert.Equal((ushort)255, unsigned.AsUInt16());
        Assert.Equal(255U, unsigned.AsUInt32());
        Assert.Equal((char)255, unsigned.AsChar());

        Assert.Equal('A', UmkaValue.From('A').AsChar());
        Assert.Equal((char)255, UmkaValue.From((char)255).AsChar());
        Assert.Equal('A', UmkaValue.From(65L).AsChar());
        Assert.Equal(1.25f, UmkaValue.From(1.25d).AsSingle());

        Assert.Throws<ArgumentOutOfRangeException>(() => UmkaValue.From((char)256));
        Assert.Throws<OverflowException>(() => UmkaValue.From(128L).AsSByte());
        Assert.Throws<OverflowException>(() => UmkaValue.From(256UL).AsByte());
        Assert.Throws<OverflowException>(() => UmkaValue.From(-1L).AsChar());
        Assert.Throws<OverflowException>(() => UmkaValue.From(256UL).AsChar());
        Assert.Throws<OverflowException>(() => UmkaValue.From(double.MaxValue).AsSingle());
    }

    [Fact]
    public void UmkaValue_converts_enum_values_by_underlying_storage()
    {
        var color = UmkaValue.FromEnum(HostColor.Green);
        Assert.Equal(UmkaValueKind.Int, color.Kind);
        Assert.Equal(1L, color.AsInt64());
        Assert.Equal(HostColor.Green, color.AsEnum<HostColor>());

        var mode = UmkaValue.FromEnum(HostMode.Select);
        Assert.Equal(UmkaValueKind.UInt, mode.Kind);
        Assert.Equal(75UL, mode.AsUInt64());
        Assert.Equal(HostMode.Select, mode.AsEnum<HostMode>());
        Assert.Equal(HostMode.Remove, UmkaValue.From(8UL).AsEnum<HostMode>());
        Assert.Equal(HostSignedTiny.Low, UmkaValue.From((sbyte)-8).AsEnum<HostSignedTiny>());
        Assert.True(color.TryAsEnum<HostColor>(out var tryColor));
        Assert.Equal(HostColor.Green, tryColor);
        Assert.True(mode.TryAsEnum<HostMode>(out var tryMode));
        Assert.Equal(HostMode.Select, tryMode);

        Assert.Throws<InvalidOperationException>(() => UmkaValue.From(1L).AsEnum<HostMode>());
        Assert.Throws<OverflowException>(() => UmkaValue.From(256UL).AsEnum<HostMode>());
        Assert.False(UmkaValue.From(1L).TryAsEnum<HostMode>(out var wrongStorage));
        Assert.Equal(default, wrongStorage);
        Assert.False(UmkaValue.From(256UL).TryAsEnum<HostMode>(out var overflowed));
        Assert.Equal(default, overflowed);
    }

    [Fact]
    public void UmkaValue_creates_scalar_values_from_generic_inputs()
    {
        var pointer = new IntPtr(0x1234);

        Assert.Equal(-8, UmkaValue.FromScalar((sbyte)-8).AsInt64());
        Assert.Equal(42, UmkaValue.FromScalar(42).AsInt64());
        Assert.Equal(42UL, UmkaValue.FromScalar(42UL).AsUInt64());
        Assert.Equal('A', UmkaValue.FromScalar('A').AsChar());
        Assert.Equal(1.25f, UmkaValue.FromScalar(1.25f).AsSingle());
        Assert.Equal(2.5, UmkaValue.FromScalar(2.5).AsDouble());
        Assert.True(UmkaValue.FromScalar(true).AsBoolean());
        Assert.Equal("value", UmkaValue.FromScalar("value").AsString());
        Assert.Null(UmkaValue.FromScalar<string?>(null).AsString());
        Assert.Equal(pointer, UmkaValue.FromScalar(pointer).AsPointer());
        Assert.Equal(HostColor.Green, UmkaValue.FromScalar(HostColor.Green).AsEnum<HostColor>());
        Assert.Equal(HostMode.Select, UmkaValue.FromScalar(HostMode.Select).AsEnum<HostMode>());
        Assert.Equal(42, UmkaValue.FromScalar(UmkaValue.From(42)).AsInt64());

        Assert.Throws<NotSupportedException>(() => UmkaValue.FromScalar(new IntPair()));
        Assert.Throws<NotSupportedException>(() => UmkaValue.FromScalar<object?>(null));
    }

    [Fact]
    public void UmkaValue_try_creates_scalar_values_from_generic_inputs()
    {
        var pointer = new IntPtr(0x1234);

        Assert.True(UmkaValue.TryFromScalar(42, out var signed));
        Assert.Equal(42, signed.AsInt64());

        Assert.True(UmkaValue.TryFromScalar("value", out var text));
        Assert.Equal("value", text.AsString());

        Assert.True(UmkaValue.TryFromScalar<string?>(null, out var nullableText));
        Assert.Equal(UmkaValueKind.String, nullableText.Kind);
        Assert.Null(nullableText.AsString());

        Assert.True(UmkaValue.TryFromScalar(pointer, out var pointerValue));
        Assert.Equal(pointer, pointerValue.AsPointer());

        Assert.True(UmkaValue.TryFromScalar(HostColor.Green, out var color));
        Assert.Equal(HostColor.Green, color.AsEnum<HostColor>());

        Assert.True(UmkaValue.TryFromScalar(UmkaValue.From(42), out var dynamicValue));
        Assert.Equal(42, dynamicValue.AsInt64());

        Assert.False(UmkaValue.TryFromScalar(new IntPair(), out var unsupported));
        Assert.Equal(UmkaValueKind.Void, unsupported.Kind);

        Assert.False(UmkaValue.TryFromScalar<object?>(null, out var nullObject));
        Assert.Equal(UmkaValueKind.Void, nullObject.Kind);

        Assert.False(UmkaValue.TryFromScalar("bad\0value", out var embeddedNull));
        Assert.Equal(UmkaValueKind.Void, embeddedNull.Kind);

        Assert.False(UmkaValue.TryFromScalar('\u0100', out var outOfRangeChar));
        Assert.Equal(UmkaValueKind.Void, outOfRangeChar.Kind);
    }

    [Fact]
    public void UmkaValue_reads_scalar_values_with_generic_helper()
    {
        var pointer = new IntPtr(0x1234);

        Assert.Equal((sbyte)-8, UmkaValue.From((sbyte)-8).AsScalar<sbyte>());
        Assert.Equal(42, UmkaValue.From(42).AsScalar<int>());
        Assert.Equal(42UL, UmkaValue.From(42UL).AsScalar<ulong>());
        Assert.Equal('A', UmkaValue.From('A').AsScalar<char>());
        Assert.Equal(1.25f, UmkaValue.From(1.25f).AsScalar<float>());
        Assert.Equal(2.5, UmkaValue.From(2.5).AsScalar<double>());
        Assert.True(UmkaValue.From(true).AsScalar<bool>());
        Assert.Equal("value", UmkaValue.From("value").AsScalar<string>());
        Assert.Null(UmkaValue.From((string?)null).AsScalar<string?>());
        Assert.Equal(pointer, UmkaValue.FromPointer(pointer).AsScalar<IntPtr>());
        Assert.Equal(HostColor.Green, UmkaValue.FromEnum(HostColor.Green).AsScalar<HostColor>());
        Assert.Equal(HostMode.Select, UmkaValue.FromEnum(HostMode.Select).AsScalar<HostMode>());
        Assert.Equal(42, UmkaValue.From(42).AsScalar<UmkaValue>().AsInt64());

        Assert.Throws<InvalidOperationException>(() => UmkaValue.From(42).AsScalar<string>());
        Assert.Throws<InvalidOperationException>(() => UmkaValue.From(8UL).AsScalar<HostColor>());
        Assert.Throws<NotSupportedException>(() => UmkaValue.FromStruct(new IntPair()).AsScalar<IntPair>());
    }

    [Fact]
    public void UmkaValue_try_reads_scalar_values_with_generic_helper()
    {
        var pointer = new IntPtr(0x1234);

        Assert.True(UmkaValue.From(42).TryAsScalar<int>(out var signed));
        Assert.Equal(42, signed);

        Assert.True(UmkaValue.FromPointer(pointer).TryAsScalar<IntPtr>(out var readPointer));
        Assert.Equal(pointer, readPointer);

        Assert.True(UmkaValue.From((string?)null).TryAsScalar<string?>(out var nullableText));
        Assert.Null(nullableText);

        Assert.True(UmkaValue.FromEnum(HostColor.Green).TryAsScalar<HostColor>(out var color));
        Assert.Equal(HostColor.Green, color);

        Assert.True(UmkaValue.From(42).TryAsScalar<UmkaValue>(out var dynamicValue));
        Assert.Equal(42, dynamicValue.AsInt64());

        Assert.False(UmkaValue.From(42).TryAsScalar<string>(out var wrongKindText));
        Assert.Null(wrongKindText);

        Assert.False(UmkaValue.From(300).TryAsScalar<sbyte>(out var overflowed));
        Assert.Equal(default, overflowed);

        Assert.False(UmkaValue.From(8UL).TryAsScalar<HostColor>(out var wrongStorageColor));
        Assert.Equal(default, wrongStorageColor);

        Assert.False(UmkaValue.FromStruct(new IntPair()).TryAsScalar<IntPair>(out _));
    }

    [Fact]
    public void UmkaValue_static_array_values_are_defensively_copied()
    {
        var source = new[] { 1L, 2L, 3L };
        var value = UmkaValue.FromStaticArray(source);
        var spanSource = new[] { 0L, 1L, 2L, 3L, 4L };
        var spanValue = UmkaValue.FromStaticArray(spanSource.AsSpan(1, 3));

        source[0] = 99L;
        spanSource[2] = 99L;
        var firstRead = Assert.IsType<long[]>(value.Value);
        AssertStaticArraySnapshot(firstRead);
        AssertStaticArraySnapshot(spanValue.AsStaticArray<long>());

        firstRead[1] = 99L;
        var secondRead = Assert.IsType<long[]>(value.Value);

        Assert.Equal(UmkaValueKind.StaticArray, value.Kind);
        AssertStaticArraySnapshot(secondRead);
    }

    [Fact]
    public void UmkaValue_dynamic_array_values_are_defensively_copied()
    {
        var source = new[] { 1L, 2L, 3L };
        var value = UmkaValue.FromDynamicArray(source);
        var spanSource = new[] { 0L, 1L, 2L, 3L, 4L };
        var spanValue = UmkaValue.FromDynamicArray(spanSource.AsSpan(1, 3));
        var nestedSource = new[] { new[] { 1L, 2L }, Array.Empty<long>(), new[] { 3L } };
        var nestedValue = UmkaValue.FromNestedDynamicArray(nestedSource);
        var nestedStringSource = new[] { new string?[] { "a", null }, Array.Empty<string?>(), new string?[] { "b" } };
        var nestedStringValue = UmkaValue.FromNestedDynamicArray(nestedStringSource);

        source[0] = 99L;
        spanSource[2] = 99L;
        nestedSource[0][0] = 99L;
        nestedStringSource[0][0] = "mutated";
        var firstRead = Assert.IsType<long[]>(value.Value);
        var firstNestedRead = Assert.IsType<long[][]>(nestedValue.Value);
        var firstNestedStringRead = Assert.IsType<string?[][]>(nestedStringValue.Value);
        AssertStaticArraySnapshot(firstRead);
        AssertStaticArraySnapshot(spanValue.AsDynamicArray<long>());
        AssertNestedArraySnapshot(firstNestedRead);
        AssertNestedStringArraySnapshot(firstNestedStringRead);

        firstRead[1] = 99L;
        firstNestedRead[0][1] = 99L;
        firstNestedStringRead[2][0] = "mutated";
        var secondRead = Assert.IsType<long[]>(value.Value);
        var secondNestedRead = Assert.IsType<long[][]>(nestedValue.Value);
        var secondNestedStringRead = Assert.IsType<string?[][]>(nestedStringValue.Value);

        Assert.Equal(UmkaValueKind.DynamicArray, value.Kind);
        Assert.Equal(UmkaValueKind.DynamicArray, nestedValue.Kind);
        AssertStaticArraySnapshot(secondRead);
        AssertNestedArraySnapshot(secondNestedRead);
        AssertNestedStringArraySnapshot(secondNestedStringRead);
    }

    [Fact]
    public void UmkaValue_try_creates_structured_values()
    {
        var pair = new IntPair { X = 19, Y = 23 };
        var source = new[] { 1L, 2L, 3L };
        var spanSource = new[] { 0L, 1L, 2L, 3L, 4L };

        Assert.True(UmkaValue.TryFromStruct(pair, out var structValue));
        Assert.Equal(UmkaValueKind.Struct, structValue.Kind);
        Assert.Equal(pair, structValue.AsStruct<IntPair>());

        Assert.True(UmkaValue.TryFromStaticArray(source, out var arrayValue));
        Assert.Equal(UmkaValueKind.StaticArray, arrayValue.Kind);
        AssertStaticArraySnapshot(arrayValue.AsStaticArray<long>());

        Assert.True(UmkaValue.TryFromStaticArray(spanSource.AsSpan(1, 3), out var spanValue));
        AssertStaticArraySnapshot(spanValue.AsStaticArray<long>());

        Assert.True(UmkaValue.TryFromDynamicArray(source, out var dynamicArrayValue));
        Assert.Equal(UmkaValueKind.DynamicArray, dynamicArrayValue.Kind);
        AssertStaticArraySnapshot(dynamicArrayValue.AsDynamicArray<long>());

        Assert.True(UmkaValue.TryFromDynamicArray(spanSource.AsSpan(1, 3), out var dynamicSpanValue));
        AssertStaticArraySnapshot(dynamicSpanValue.AsDynamicArray<long>());
        var nestedSource = new[] { new[] { 1L, 2L }, Array.Empty<long>(), new[] { 3L } };
        var nestedStringSource = new[] { new string?[] { "a", null }, Array.Empty<string?>(), new string?[] { "b" } };
        Assert.True(UmkaValue.TryFromNestedDynamicArray(nestedSource, out var nestedArrayValue));
        AssertNestedArraySnapshot(nestedArrayValue.AsNestedDynamicArray<long>());
        Assert.True(UmkaValue.TryFromNestedDynamicArray(nestedStringSource, out var nestedStringArrayValue));
        AssertNestedStringArraySnapshot(nestedStringArrayValue.AsNestedStringArray());

        source[0] = 99L;
        spanSource[2] = 99L;
        nestedSource[0][0] = 99L;
        AssertStaticArraySnapshot(arrayValue.AsStaticArray<long>());
        AssertStaticArraySnapshot(spanValue.AsStaticArray<long>());
        AssertStaticArraySnapshot(dynamicArrayValue.AsDynamicArray<long>());
        AssertStaticArraySnapshot(dynamicSpanValue.AsDynamicArray<long>());
        AssertNestedArraySnapshot(nestedArrayValue.AsNestedDynamicArray<long>());

        Assert.False(UmkaValue.TryFromStaticArray<long>(null, out var nullArray));
        Assert.Equal(UmkaValueKind.Void, nullArray.Kind);
        Assert.False(UmkaValue.TryFromDynamicArray<long>(null, out var nullDynamicArray));
        Assert.Equal(UmkaValueKind.Void, nullDynamicArray.Kind);
        Assert.False(UmkaValue.TryFromNestedDynamicArray<long>(null, out var nullNestedDynamicArray));
        Assert.Equal(UmkaValueKind.Void, nullNestedDynamicArray.Kind);

        Assert.False(UmkaValue.TryFromStruct(new ManagedStringBox { Value = "text" }, out var managedStruct));
        Assert.Equal(UmkaValueKind.Void, managedStruct.Kind);

        Assert.False(UmkaValue.TryFromStaticArray(new[] { new ManagedStringBox { Value = "text" } }, out var managedArray));
        Assert.Equal(UmkaValueKind.Void, managedArray.Kind);
        Assert.False(UmkaValue.TryFromDynamicArray(new[] { new ManagedStringBox { Value = "text" } }, out var managedDynamicArray));
        Assert.Equal(UmkaValueKind.Void, managedDynamicArray.Kind);
        var managedNestedSource = new[] { new[] { new ManagedStringBox { Value = "text" } } };
        Assert.False(UmkaValue.TryFromNestedDynamicArray(managedNestedSource, out var managedNestedDynamicArray));
        Assert.Equal(UmkaValueKind.Void, managedNestedDynamicArray.Kind);
        var nullRowSource = new long[1][];
        nullRowSource[0] = null!;
        Assert.False(UmkaValue.TryFromNestedDynamicArray(nullRowSource, out var nullRowNestedArray));
        Assert.Equal(UmkaValueKind.Void, nullRowNestedArray.Kind);
    }

    [Fact]
    public void UmkaValue_reads_structured_values_with_typed_snapshot_helpers()
    {
        var pair = new IntPair { X = 19, Y = 23 };
        var structValue = UmkaValue.FromStruct(pair);
        var arrayValue = UmkaValue.FromStaticArray(1L, 2L, 3L);
        var dynamicArrayValue = UmkaValue.FromDynamicArray(1L, 2L, 3L);
        var stringArrayValue = UmkaValue.FromDynamicArray("a", null, "b");
        var nestedSource = new[] { new[] { 1L, 2L }, Array.Empty<long>(), new[] { 3L } };
        var nestedStringSource = new[] { new string?[] { "a", null }, Array.Empty<string?>(), new string?[] { "b" } };
        var nestedArrayValue = UmkaValue.FromNestedDynamicArray(nestedSource);
        var nestedStringArrayValue = UmkaValue.FromNestedDynamicArray(nestedStringSource);

        Assert.Equal(pair, structValue.AsStruct<IntPair>());
        AssertStaticArraySnapshot(arrayValue.AsStaticArray<long>());
        AssertStaticArraySnapshot(dynamicArrayValue.AsDynamicArray<long>());
        AssertStringArraySnapshot(stringArrayValue.AsStringArray());
        AssertNestedArraySnapshot(nestedArrayValue.AsNestedDynamicArray<long>());
        AssertNestedStringArraySnapshot(nestedStringArrayValue.AsNestedStringArray());

        Assert.True(structValue.TryAsStruct<IntPair>(out var tryPair));
        Assert.Equal(pair, tryPair);

        Assert.True(arrayValue.TryAsStaticArray<long>(out var tryArray));
        Assert.NotNull(tryArray);
        AssertStaticArraySnapshot(tryArray);
        Assert.True(dynamicArrayValue.TryAsDynamicArray<long>(out var tryDynamicArray));
        Assert.NotNull(tryDynamicArray);
        AssertStaticArraySnapshot(tryDynamicArray);
        Assert.True(stringArrayValue.TryAsStringArray(out var tryStringArray));
        Assert.NotNull(tryStringArray);
        AssertStringArraySnapshot(tryStringArray);
        Assert.True(nestedArrayValue.TryAsNestedDynamicArray<long>(out var tryNestedArray));
        Assert.NotNull(tryNestedArray);
        AssertNestedArraySnapshot(tryNestedArray);
        Assert.True(nestedStringArrayValue.TryAsNestedStringArray(out var tryNestedStringArray));
        Assert.NotNull(tryNestedStringArray);
        AssertNestedStringArraySnapshot(tryNestedStringArray);

        var arraySnapshot = arrayValue.AsStaticArray<long>();
        arraySnapshot[0] = 99L;
        AssertStaticArraySnapshot(arrayValue.AsStaticArray<long>());

        tryArray[1] = 99L;
        AssertStaticArraySnapshot(arrayValue.AsStaticArray<long>());
        tryDynamicArray[1] = 99L;
        AssertStaticArraySnapshot(dynamicArrayValue.AsDynamicArray<long>());
        tryStringArray[0] = "mutated";
        AssertStringArraySnapshot(stringArrayValue.AsStringArray());
        tryNestedArray[0][0] = 99L;
        AssertNestedArraySnapshot(nestedArrayValue.AsNestedDynamicArray<long>());
        tryNestedStringArray[0][0] = "mutated";
        AssertNestedStringArraySnapshot(nestedStringArrayValue.AsNestedStringArray());

        Assert.False(structValue.TryAsStruct<RealPair>(out var wrongStructType));
        Assert.Equal(default, wrongStructType);
        Assert.False(structValue.TryAsStaticArray<long>(out var structAsArray));
        Assert.Null(structAsArray);
        Assert.False(structValue.TryAsDynamicArray<long>(out var structAsDynamicArray));
        Assert.Null(structAsDynamicArray);
        Assert.False(arrayValue.TryAsStaticArray<int>(out var wrongArrayType));
        Assert.Null(wrongArrayType);
        Assert.False(dynamicArrayValue.TryAsDynamicArray<int>(out var wrongDynamicArrayType));
        Assert.Null(wrongDynamicArrayType);
        Assert.False(arrayValue.TryAsStruct<IntPair>(out var arrayAsStruct));
        Assert.Equal(default, arrayAsStruct);
        Assert.False(UmkaValue.From(42).TryAsStruct<IntPair>(out var scalarAsStruct));
        Assert.Equal(default, scalarAsStruct);
        Assert.False(UmkaValue.From(42).TryAsStaticArray<long>(out var scalarAsArray));
        Assert.Null(scalarAsArray);
        Assert.False(UmkaValue.From(42).TryAsDynamicArray<long>(out var scalarAsDynamicArray));
        Assert.Null(scalarAsDynamicArray);
        Assert.False(dynamicArrayValue.TryAsStringArray(out var numericDynamicArrayAsStrings));
        Assert.Null(numericDynamicArrayAsStrings);
        Assert.False(dynamicArrayValue.TryAsNestedDynamicArray<long>(out var flatAsNestedArray));
        Assert.Null(flatAsNestedArray);
        Assert.False(dynamicArrayValue.TryAsNestedStringArray(out var flatAsNestedStringArray));
        Assert.Null(flatAsNestedStringArray);
        Assert.False(nestedArrayValue.TryAsNestedDynamicArray<int>(out var wrongNestedArrayType));
        Assert.Null(wrongNestedArrayType);
        Assert.False(nestedStringArrayValue.TryAsNestedDynamicArray<IntPtr>(out var nestedStringAsPointers));
        Assert.Null(nestedStringAsPointers);
        Assert.False(UmkaValue.From(42).TryAsStringArray(out var scalarAsStringArray));
        Assert.Null(scalarAsStringArray);
        Assert.False(structValue.TryAsStruct<ManagedStringBox>(out var managedStruct));
        Assert.Equal(default, managedStruct);
        Assert.False(arrayValue.TryAsStaticArray<ManagedStringBox>(out var managedArray));
        Assert.Null(managedArray);
        Assert.False(dynamicArrayValue.TryAsDynamicArray<ManagedStringBox>(out var managedDynamicArray));
        Assert.Null(managedDynamicArray);

        Assert.Throws<InvalidOperationException>(() => structValue.AsStruct<RealPair>());
        Assert.Throws<InvalidOperationException>(() => structValue.AsStaticArray<long>());
        Assert.Throws<InvalidOperationException>(() => structValue.AsDynamicArray<long>());
        Assert.Throws<InvalidOperationException>(() => arrayValue.AsStaticArray<int>());
        Assert.Throws<InvalidOperationException>(() => dynamicArrayValue.AsDynamicArray<int>());
        Assert.Throws<InvalidOperationException>(() => arrayValue.AsStruct<IntPair>());
        Assert.Throws<InvalidOperationException>(() => UmkaValue.From(42).AsStruct<IntPair>());
        Assert.Throws<InvalidOperationException>(() => UmkaValue.From(42).AsStaticArray<long>());
        Assert.Throws<InvalidOperationException>(() => UmkaValue.From(42).AsDynamicArray<long>());
        Assert.Throws<InvalidOperationException>(() => UmkaValue.From(42).AsStringArray());
        Assert.Throws<InvalidOperationException>(() => stringArrayValue.AsDynamicArray<IntPtr>());
        Assert.Throws<InvalidOperationException>(() => dynamicArrayValue.AsNestedDynamicArray<long>());
        Assert.Throws<InvalidOperationException>(() => dynamicArrayValue.AsNestedStringArray());
        Assert.Throws<InvalidOperationException>(() => nestedArrayValue.AsNestedDynamicArray<int>());
        Assert.Throws<InvalidOperationException>(() => nestedStringArrayValue.AsNestedDynamicArray<IntPtr>());
        Assert.Throws<ArgumentException>(() => structValue.AsStruct<ManagedStringBox>());
        Assert.Throws<ArgumentException>(() => arrayValue.AsStaticArray<ManagedStringBox>());
        Assert.Throws<ArgumentException>(() => dynamicArrayValue.AsDynamicArray<ManagedStringBox>());
    }

    [Fact]
    public void UmkaValue_rejects_aggregate_values_with_managed_references()
    {
        Assert.Throws<ArgumentException>(() => UmkaValue.FromStruct(new ManagedStringBox { Value = "text" }));
        Assert.Throws<ArgumentException>(() => UmkaValue.FromStaticArray(new ManagedStringBox { Value = "text" }));
        Assert.Throws<ArgumentException>(() => UmkaValue.FromDynamicArray(new ManagedStringBox { Value = "text" }));
        var managedNestedSource = new[] { new[] { new ManagedStringBox { Value = "text" } } };
        Assert.Throws<ArgumentException>(() => UmkaValue.FromNestedDynamicArray(managedNestedSource));
        var nullRowSource = new long[1][];
        nullRowSource[0] = null!;
        Assert.Throws<ArgumentException>(() => UmkaValue.FromNestedDynamicArray(nullRowSource));
    }

    [Fact]
    public void UmkaValue_rejects_embedded_null_strings()
    {
        var ex = Assert.Throws<ArgumentException>(() => UmkaValue.From("bad\0value"));

        Assert.Contains("Embedded null", ex.Message);
        Assert.Throws<ArgumentException>(() => UmkaValue.FromDynamicArray("good", "bad\0value"));
        var nestedStringSource = new[] { new string?[] { "good", "bad\0value" } };
        Assert.Throws<ArgumentException>(() => UmkaValue.FromNestedDynamicArray(nestedStringSource));
    }

    [Fact]
    public void UmkaValue_formats_diagnostic_strings_without_exposing_mutable_aggregate_data()
    {
        Assert.Equal("UmkaValue(Void)", UmkaValue.Void.ToString());
        Assert.Equal("UmkaValue(Int: -42)", UmkaValue.From(-42).ToString());
        Assert.Equal("UmkaValue(UInt: 42)", UmkaValue.From(42U).ToString());
        Assert.Equal("UmkaValue(Real: 1.25)", UmkaValue.From(1.25).ToString());
        Assert.Equal("UmkaValue(Bool: True)", UmkaValue.From(true).ToString());
        Assert.Equal("UmkaValue(String: null)", UmkaValue.From((string?)null).ToString());
        Assert.Equal("UmkaValue(String: \"a\\nb\\\"c\")", UmkaValue.From("a\nb\"c").ToString());
        Assert.Equal("UmkaValue(Pointer: 0x1234)", UmkaValue.FromPointer(new IntPtr(0x1234)).ToString());
        Assert.Equal("UmkaValue(StaticArray: Length=3, Size=24)", UmkaValue.FromStaticArray(1L, 2L, 3L).ToString());
        Assert.Equal("UmkaValue(DynamicArray: Length=3, Size=24)", UmkaValue.FromDynamicArray(1L, 2L, 3L).ToString());
        Assert.Equal($"UmkaValue(DynamicArray: Length=2, Size={IntPtr.Size * 2})", UmkaValue.FromDynamicArray("a", "b").ToString());
        var nestedSource = new[] { new[] { 1L, 2L }, Array.Empty<long>(), new[] { 3L } };
        var nestedStringSource = new[] { new string?[] { "a", "b" }, new string?[] { "c" } };
        Assert.Equal("UmkaValue(NestedDynamicArray: Length=3, Size=24)", UmkaValue.FromNestedDynamicArray(nestedSource).ToString());
        Assert.Equal($"UmkaValue(NestedDynamicArray: Length=2, Size={IntPtr.Size * 3})", UmkaValue.FromNestedDynamicArray(nestedStringSource).ToString());
        Assert.Equal("UmkaValue(Struct: Size=16)", UmkaValue.FromStruct(new IntPair { X = 1, Y = 2 }).ToString());
    }

    [Fact]
    public void Runtime_roundtrips_scalar_boundary_values()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn intId*(value: int): int {
                return value
            }

            fn uintId*(value: uint): uint {
                return value
            }

            fn realId*(value: real): real {
                return value
            }

            fn boolId*(value: bool): bool {
                return value
            }
            """);

        runtime.Compile();

        var intId = runtime.GetFunction("intId");
        var uintId = runtime.GetFunction("uintId");
        var realId = runtime.GetFunction("realId");
        var boolId = runtime.GetFunction("boolId");

        Assert.Equal(long.MinValue, intId.CallInt64(UmkaValue.From(long.MinValue)));
        Assert.Equal(long.MaxValue, intId.CallInt64(UmkaValue.From(long.MaxValue)));
        Assert.Equal(ulong.MaxValue, uintId.CallUInt64(UmkaValue.From(ulong.MaxValue)));
        Assert.Equal(Math.PI, realId.CallDouble(UmkaValue.From(Math.PI)));
        Assert.True(boolId.CallBoolean(UmkaValue.From(true)));
        Assert.False(boolId.CallBoolean(UmkaValue.From(false)));
    }

    [Fact]
    public void Runtime_reads_narrow_scalar_results_with_typed_helpers()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn int8Id*(value: int8): int8 {
                return value
            }

            fn int16Id*(value: int16): int16 {
                return value
            }

            fn int32Id*(value: int32): int32 {
                return value
            }

            fn uint8Id*(value: uint8): uint8 {
                return value
            }

            fn uint16Id*(value: uint16): uint16 {
                return value
            }

            fn uint32Id*(value: uint32): uint32 {
                return value
            }

            fn charId*(value: char): char {
                return value
            }

            fn real32Id*(value: real32): real32 {
                return value
            }

            fn broadInt*(): int {
                return 1000
            }

            fn broadReal*(): real {
                return 1.0e100
            }
            """);

        runtime.Compile();

        Assert.Equal((sbyte)-8, runtime.GetFunction("int8Id").CallSByte(UmkaValue.From((sbyte)-8)));
        Assert.Equal((short)-1600, runtime.GetFunction("int16Id").CallInt16(UmkaValue.From((short)-1600)));
        Assert.Equal(-32000, runtime.GetFunction("int32Id").CallInt32(UmkaValue.From(-32000)));

        Assert.Equal((byte)200, runtime.GetFunction("uint8Id").CallByte(UmkaValue.From((byte)200)));
        Assert.Equal((ushort)65000, runtime.GetFunction("uint16Id").CallUInt16(UmkaValue.From((ushort)65000)));
        Assert.Equal(4_000_000_000U, runtime.GetFunction("uint32Id").CallUInt32(UmkaValue.From(4_000_000_000U)));

        Assert.Equal('A', runtime.GetFunction("charId").CallChar(UmkaValue.From('A')));
        Assert.Equal(1.25f, runtime.GetFunction("real32Id").CallSingle(UmkaValue.From(1.25f)));
        Assert.Throws<OverflowException>(() => runtime.GetFunction("broadInt").CallSByte());
        Assert.Throws<OverflowException>(() => runtime.GetFunction("broadReal").CallSingle());
    }

    [Fact]
    public void Runtime_marshals_enum_values_as_underlying_integer_values()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            type Color = enum {
                red
                green
                blue
            }

            type Mode = enum (uint8) {
                draw = 74
                select
                remove = 8
                edit
            }

            fn nextColor*(value: Color): Color {
                if value == .red {
                    return .green
                }
                return .blue
            }

            fn selectedMode*(): Mode {
                return .select
            }

            fn modeId*(value: Mode): Mode {
                return value
            }
            """);

        runtime.Compile();

        var nextColor = runtime.GetFunction("nextColor");
        var selectedMode = runtime.GetFunction("selectedMode");
        var modeId = runtime.GetFunction("modeId");

        Assert.Equal(UmkaTypeKind.SignedInteger, nextColor.ParameterTypes[0].Kind);
        Assert.Equal(UmkaTypeKind.SignedInteger, nextColor.ResultType.Kind);
        Assert.Equal(UmkaTypeKind.UnsignedInteger, selectedMode.ResultType.Kind);
        Assert.Equal(UmkaTypeKind.UnsignedInteger, modeId.ParameterTypes[0].Kind);
        Assert.True(nextColor.ParameterTypes[0].IsEnum);
        Assert.True(nextColor.ResultType.IsEnum);
        Assert.True(selectedMode.ResultType.IsEnum);
        Assert.Collection(
            nextColor.ParameterTypes[0].EnumMembers,
            member => AssertEnumMember(member, "red", 0, 0),
            member => AssertEnumMember(member, "green", 1, 1),
            member => AssertEnumMember(member, "blue", 2, 2));
        Assert.Contains(
            selectedMode.ResultType.EnumMembers,
            member => member.Name == "zero" && member.SignedValue == 0 && member.UnsignedValue == 0);
        Assert.Contains(
            selectedMode.ResultType.EnumMembers,
            member => member.Name == "select" && member.SignedValue == 75 && member.UnsignedValue == 75);

        Assert.Equal(1, nextColor.CallInt64(UmkaValue.From(0)));
        Assert.Equal(2, nextColor.CallInt64(UmkaValue.From(1)));
        Assert.Equal(HostColor.Green, nextColor.CallEnum<HostColor>(UmkaValue.FromEnum(HostColor.Red)));
        Assert.Equal(HostColor.Blue, nextColor.CallEnum<HostColor>(UmkaValue.FromEnum(HostColor.Green)));
        Assert.Equal((byte)75, selectedMode.CallByte());
        Assert.Equal(HostMode.Select, selectedMode.CallEnum<HostMode>());
        Assert.Equal((byte)8, modeId.CallByte(UmkaValue.From((byte)8)));
        Assert.Equal(HostMode.Remove, modeId.CallEnum<HostMode>(UmkaValue.FromEnum(HostMode.Remove)));
        Assert.True(nextColor.TryCallEnum<HostColor>(out var tryNextColor, UmkaValue.FromEnum(HostColor.Red)));
        Assert.Equal(HostColor.Green, tryNextColor);
        Assert.True(selectedMode.TryCallEnum<HostMode>(ReadOnlySpan<UmkaValue>.Empty, out var trySelectedMode));
        Assert.Equal(HostMode.Select, trySelectedMode);
        Assert.False(selectedMode.TryCallEnum<HostColor>(out var wrongStorageColor));
        Assert.Equal(default, wrongStorageColor);
        Assert.Throws<InvalidOperationException>(() => selectedMode.CallEnum<HostColor>());
        Assert.Throws<ArgumentOutOfRangeException>(() => modeId.CallByte(UmkaValue.From(256UL)));
    }

    [Fact]
    public void Callback_frame_exposes_enum_member_metadata()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            type Status = enum (uint8) {
                ok = 10
                fail = 20
            }

            fn inspect*(value: Status): Status

            fn run*(): Status {
                return inspect(.fail)
            }
            """);

        UmkaTypeInfo? argumentType = null;
        UmkaTypeInfo? resultType = null;

        runtime.Register("inspect", frame =>
        {
            argumentType = frame.ParameterTypes[0];
            resultType = frame.ResultType;
            return UmkaValue.From((byte)10);
        });

        runtime.Compile();

        Assert.Equal((byte)10, runtime.GetFunction("run").CallByte());

        Assert.NotNull(argumentType);
        Assert.NotNull(resultType);
        Assert.Equal(UmkaTypeKind.UnsignedInteger, argumentType.Kind);
        Assert.True(argumentType.IsEnum);
        Assert.Equal(UmkaTypeKind.UnsignedInteger, resultType.Kind);
        Assert.True(resultType.IsEnum);
        Assert.Collection(
            argumentType.EnumMembers,
            member => AssertEnumMember(member, "ok", 10, 10),
            member => AssertEnumMember(member, "fail", 20, 20),
            member => AssertEnumMember(member, "zero", 0, 0));
        Assert.Collection(
            resultType.EnumMembers,
            member => AssertEnumMember(member, "ok", 10, 10),
            member => AssertEnumMember(member, "fail", 20, 20),
            member => AssertEnumMember(member, "zero", 0, 0));
    }

    [Fact]
    public void Function_call_scalar_reads_supported_scalar_result_types()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            type Color = enum {red; green; blue}

            type Mode = enum (uint8) {
                draw = 74
                select
                remove = 8
                edit
            }

            fn signedValue*(): int {
                return -42
            }

            fn unsignedValue*(): uint {
                return uint(42)
            }

            fn realValue*(): real {
                return 12.25
            }

            fn truthValue*(): bool {
                return true
            }

            fn letterValue*(): char {
                return 'A'
            }

            fn textValue*(): str {
                return "value"
            }

            fn pointerValue*(): ^void {
                return null
            }

            fn colorValue*(): Color {
                return .green
            }

            fn modeValue*(): Mode {
                return .select
            }

            fn narrowValue*(): int {
                return 300
            }

            fn pair*(): [2]real {
                return [2]real{1.0, 2.0}
            }
            """);

        runtime.Compile();

        var signedValue = runtime.GetFunction("signedValue");
        var unsignedValue = runtime.GetFunction("unsignedValue");
        var realValue = runtime.GetFunction("realValue");
        var truthValue = runtime.GetFunction("truthValue");
        var letterValue = runtime.GetFunction("letterValue");
        var textValue = runtime.GetFunction("textValue");
        var pointerValue = runtime.GetFunction("pointerValue");
        var colorValue = runtime.GetFunction("colorValue");
        var modeValue = runtime.GetFunction("modeValue");
        var narrowValue = runtime.GetFunction("narrowValue");
        var pair = runtime.GetFunction("pair");

        Assert.True(signedValue.CanReadResultAsScalar<int>());
        Assert.True(signedValue.CanReadResultAsValue());
        Assert.False(signedValue.CanReadResultAsScalar<string>());
        Assert.False(signedValue.CanReadResultAsStruct<RealPair>());
        Assert.False(signedValue.CanReadResultAsArray<long>(2));
        Assert.True(pair.CanReadResultAsStruct<RealPair>());
        Assert.True(pair.CanReadResultAsArray<double>(2));
        Assert.False(pair.CanReadResultAsValue());
        Assert.False(pair.CanReadResultAsScalar<RealPair>());
        Assert.False(pair.CanReadResultAsStruct<IntTriple>());
        Assert.False(pair.CanReadResultAsStruct<ManagedStringBox>());
        Assert.False(pair.CanReadResultAsArray<double>(1));
        Assert.False(pair.CanReadResultAsArray<int>(2));
        Assert.False(pair.CanReadResultAsArray<ManagedStringBox>(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => pair.CanReadResultAsArray<double>(-1));

        Assert.Equal(-42, signedValue.CallScalar<int>());
        Assert.Equal(42UL, unsignedValue.CallScalar<ulong>());
        Assert.Equal(12.25, realValue.CallScalar<double>());
        Assert.Equal(12.25f, realValue.CallScalar<float>());
        Assert.True(truthValue.CallScalar<bool>());
        Assert.Equal('A', letterValue.CallScalar<char>());
        Assert.Equal("value", textValue.CallScalar<string>());
        Assert.Equal(IntPtr.Zero, pointerValue.CallScalar<IntPtr>());
        Assert.Equal(HostColor.Green, colorValue.CallScalar<HostColor>());
        Assert.Equal(HostMode.Select, modeValue.CallScalar<HostMode>());
        Assert.Equal(-42, signedValue.CallScalar<UmkaValue>().AsInt64());

        Assert.True(signedValue.TryCallScalar<int>(out var trySigned));
        Assert.Equal(-42, trySigned);
        Assert.True(unsignedValue.TryCallScalar<ulong>(ReadOnlySpan<UmkaValue>.Empty, out var tryUnsigned));
        Assert.Equal(42UL, tryUnsigned);
        Assert.True(textValue.TryCallScalar<string>(out var tryText));
        Assert.Equal("value", tryText);
        Assert.True(colorValue.TryCallScalar<HostColor>(out var tryColor));
        Assert.Equal(HostColor.Green, tryColor);
        Assert.True(signedValue.TryCallScalar<UmkaValue>(out var tryDynamic));
        Assert.Equal(-42, tryDynamic.AsInt64());

        Assert.Throws<InvalidOperationException>(() => signedValue.CallScalar<string>());
        Assert.Throws<InvalidOperationException>(() => modeValue.CallScalar<HostColor>());
        Assert.Throws<NotSupportedException>(() => pair.CallScalar<RealPair>());
        Assert.False(signedValue.TryCallScalar<string>(out var wrongText));
        Assert.Null(wrongText);
        Assert.False(modeValue.TryCallScalar<HostColor>(out var wrongStorageColor));
        Assert.Equal(default, wrongStorageColor);
        Assert.False(narrowValue.TryCallScalar<sbyte>(out var overflowed));
        Assert.Equal(default, overflowed);
        Assert.False(pair.TryCallScalar<RealPair>(out _));
        Assert.Throws<ArgumentException>(() => signedValue.TryCallScalar<int>(out _, UmkaValue.From(1)));
        Assert.Throws<ArgumentException>(() => pair.TryCallScalar<RealPair>(out _, UmkaValue.From(1)));
    }

    [Fact]
    public void Runtime_roundtrips_utf8_and_null_strings()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn echo*(value: str): str {
                return value
            }
            """);

        runtime.Compile();
        var echo = runtime.GetFunction("echo");

        const string utf8 = "Zażółć gęślą jaźń";

        Assert.Equal(utf8, echo.CallString(UmkaValue.From(utf8)));
        Assert.Null(echo.CallString(UmkaValue.From((string?)null)));
    }

    [Fact]
    public void Runtime_roundtrips_pointer_values()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn ptrId*(value: ^int): ^int {
                return value
            }
            """);

        runtime.Compile();
        var ptrId = runtime.GetFunction("ptrId");
        var pointer = new IntPtr(0x123456);

        Assert.Equal(pointer, ptrId.CallPointer(UmkaValue.FromPointer(pointer)));
        Assert.Equal(IntPtr.Zero, ptrId.CallPointer(UmkaValue.FromPointer(IntPtr.Zero)));
    }

    [Fact]
    public void Function_call_overloads_accept_reusable_argument_spans()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn add*(a, b: int): int {
                return a + b
            }

            fn greet*(name: str): str {
                return "Hello, " + name
            }

            fn pair*(x, y: real): [2]real {
                return [2]real{x, y}
            }
            """);

        runtime.Compile();

        var values = new[]
        {
            UmkaValue.Void,
            UmkaValue.From(19),
            UmkaValue.From(23),
            UmkaValue.From("Umka"),
            UmkaValue.From(2.5),
            UmkaValue.From(7.5)
        };

        Assert.Equal(42, runtime.GetFunction("add").CallInt64(values.AsSpan(1, 2)));
        Assert.Equal("Hello, Umka", runtime.GetFunction("greet").CallString(values.AsSpan(3, 1)));

        var pair = runtime.GetFunction("pair").CallStruct<RealPair>(values.AsSpan(4, 2));

        Assert.Equal(2.5, pair.X);
        Assert.Equal(7.5, pair.Y);
    }

    [Fact]
    public void Function_exposes_parameter_and_result_type_metadata()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn mix*(i: int, u: uint, r: real, b: bool, s: str, p: ^int): bool {
                return b
            }
            """);

        runtime.Compile();
        var mix = runtime.GetFunction("mix");

        Assert.Equal(6, mix.ParameterCount);
        Assert.IsNotType<UmkaTypeInfo[]>(mix.ParameterTypes);
        Assert.Throws<NotSupportedException>(() =>
            ((System.Collections.Generic.IList<UmkaTypeInfo>)mix.ParameterTypes)[0] =
                new UmkaTypeInfo(UmkaTypeKind.Unknown, "mutated"));
        Assert.Equal(UmkaTypeKind.SignedInteger, mix.ParameterTypes[0].Kind);
        Assert.Equal(UmkaTypeKind.UnsignedInteger, mix.ParameterTypes[1].Kind);
        Assert.Equal(UmkaTypeKind.Real, mix.ParameterTypes[2].Kind);
        Assert.Equal(UmkaTypeKind.Boolean, mix.ParameterTypes[3].Kind);
        Assert.Equal(UmkaTypeKind.String, mix.ParameterTypes[4].Kind);
        Assert.Equal(UmkaTypeKind.Pointer, mix.ParameterTypes[5].Kind);
        Assert.Equal(UmkaTypeKind.Boolean, mix.ResultType.Kind);
        Assert.All(mix.ParameterTypes, type => Assert.False(string.IsNullOrWhiteSpace(type.TypeName)));
        Assert.False(string.IsNullOrWhiteSpace(mix.ResultType.TypeName));
    }

    [Fact]
    public void Function_type_metadata_exposes_native_size_and_reference_flags()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            type Pair = struct {
                x, y: int
            }

            type TextBox = struct {
                value: str
            }

            fn inspect*(pair: Pair, values: [3]int, text: TextBox): [2]int {
                return [2]int{pair.x, values[0]}
            }

            fn textBox*(): TextBox {
                return TextBox{"value"}
            }
            """);

        runtime.Compile();

        var inspect = runtime.GetFunction("inspect");
        var textBox = runtime.GetFunction("textBox");

        Assert.Equal(Marshal.SizeOf<IntPair>(), inspect.ParameterTypes[0].NativeSize);
        Assert.False(inspect.ParameterTypes[0].HasReferences);
        Assert.Equal(3 * Marshal.SizeOf<long>(), inspect.ParameterTypes[1].NativeSize);
        Assert.Equal(3, inspect.ParameterTypes[1].ItemCount);
        Assert.False(inspect.ParameterTypes[1].HasReferences);
        Assert.True(inspect.ParameterTypes[2].NativeSize > 0);
        Assert.True(inspect.ParameterTypes[2].HasReferences);
        Assert.Equal(2 * Marshal.SizeOf<long>(), inspect.ResultType.NativeSize);
        Assert.Equal(2, inspect.ResultType.ItemCount);
        Assert.False(inspect.ResultType.HasReferences);
        Assert.True(textBox.ResultType.NativeSize > 0);
        Assert.True(textBox.ResultType.HasReferences);
    }

    [Fact]
    public void Function_rejects_argument_type_mismatches_before_native_call()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn addOne*(value: int): int {
                return value + 1
            }
            """);

        runtime.Compile();
        var addOne = runtime.GetFunction("addOne");

        Assert.True(addOne.CanCallWith(UmkaValue.From(41)));
        Assert.True(addOne.CanCallWith(new[] { UmkaValue.From(41) }.AsSpan()));
        Assert.False(addOne.CanCallWith());
        Assert.False(addOne.CanCallWith(UmkaValue.From(41), UmkaValue.From(1)));
        Assert.False(addOne.CanCallWith(UmkaValue.From("not an int")));
        Assert.False(addOne.CanCallWith(UmkaValue.Void));
        var ex = Assert.Throws<ArgumentException>(() => addOne.CallInt64(UmkaValue.From("not an int")));

        Assert.Contains("expects Umka type", ex.Message);
        Assert.Equal(42, addOne.CallInt64(UmkaValue.From(41)));
    }

    [Fact]
    public void Function_rejects_out_of_range_narrow_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn int8Id*(value: int8): int {
                return int(value)
            }

            fn uint8Id*(value: uint8): uint {
                return uint(value)
            }

            fn charId*(value: char): int {
                return int(value)
            }

            fn real32Id*(value: real32): real32 {
                return value
            }
            """);

        runtime.Compile();

        var int8Id = runtime.GetFunction("int8Id");
        var uint8Id = runtime.GetFunction("uint8Id");
        var charId = runtime.GetFunction("charId");
        var real32Id = runtime.GetFunction("real32Id");

        Assert.Equal(127, int8Id.CallInt64(UmkaValue.From(127)));
        Assert.Equal(255UL, uint8Id.CallUInt64(UmkaValue.From(255UL)));
        Assert.Equal(65, charId.CallInt64(UmkaValue.From(65)));
        Assert.Equal(1.25f, real32Id.CallSingle(UmkaValue.From(1.25)));
        Assert.Equal(127, int8Id.CallInt64(UmkaValue.From((sbyte)127)));
        Assert.Equal(255UL, uint8Id.CallUInt64(UmkaValue.From((byte)255)));
        Assert.Equal(65, charId.CallInt64(UmkaValue.From('A')));

        Assert.True(int8Id.CanCallWith(UmkaValue.From(127)));
        Assert.True(uint8Id.CanCallWith(UmkaValue.From(255UL)));
        Assert.True(charId.CanCallWith(UmkaValue.From('A')));
        Assert.True(real32Id.CanCallWith(UmkaValue.From(1.25)));
        Assert.False(int8Id.CanCallWith(UmkaValue.From(128)));
        Assert.False(uint8Id.CanCallWith(UmkaValue.From(256UL)));
        Assert.False(charId.CanCallWith(UmkaValue.From(-1)));
        Assert.False(real32Id.CanCallWith(UmkaValue.From(double.MaxValue)));

        Assert.Throws<ArgumentOutOfRangeException>(() => int8Id.CallInt64(UmkaValue.From(128)));
        Assert.Throws<ArgumentOutOfRangeException>(() => uint8Id.CallUInt64(UmkaValue.From(256UL)));
        Assert.Throws<ArgumentOutOfRangeException>(() => charId.CallInt64(UmkaValue.From(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => real32Id.CallSingle(UmkaValue.From(double.MaxValue)));
    }

    [Fact]
    public void Function_marshals_fixed_layout_struct_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            type Pair = struct {
                x, y: int
            }

            type Segment = struct {
                start, finish: Pair
            }

            fn sumPair*(value: Pair): int {
                return value.x + value.y
            }

            fn sumSegment*(value: Segment): int {
                return value.start.x + value.start.y + value.finish.x + value.finish.y
            }
            """);

        runtime.Compile();
        var sumPair = runtime.GetFunction("sumPair");
        var sumSegment = runtime.GetFunction("sumSegment");
        var pair = UmkaValue.FromStruct(new IntPair { X = 19, Y = 23 });
        var segment = UmkaValue.FromStruct(new IntSegment
        {
            Start = new IntPair { X = 10, Y = 20 },
            Finish = new IntPair { X = 30, Y = 40 }
        });

        Assert.True(sumPair.CanCallWith(pair));
        Assert.False(sumPair.CanCallWith(UmkaValue.FromStruct(new IntTriple { X = 10, Y = 20, Z = 30 })));
        Assert.False(sumPair.CanCallWith(UmkaValue.FromStaticArray(19L, 23L)));
        Assert.True(sumSegment.CanCallWith(segment));

        Assert.Equal(42, sumPair.CallInt64(pair));
        Assert.Equal(100, sumSegment.CallInt64(segment));
    }

    [Fact]
    public void Function_marshals_static_array_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn sum*(values: [3]int): int {
                return values[0] + values[1] + values[2]
            }
            """);

        runtime.Compile();
        var sum = runtime.GetFunction("sum");

        Assert.Equal(42, sum.CallInt64(UmkaValue.FromStaticArray(10L, 14L, 18L)));
        var reusableValues = new[] { 4L, 18L, 20L };
        Assert.Equal(42, sum.CallInt64(UmkaValue.FromStaticArray(reusableValues.AsSpan())));

        Assert.True(sum.CanCallWith(UmkaValue.FromStaticArray(10L, 14L, 18L)));
        Assert.False(sum.CanCallWith(UmkaValue.FromStaticArray(21L, 21L)));
        Assert.False(sum.CanCallWith(UmkaValue.FromStruct(new IntTriple { X = 10, Y = 14, Z = 18 })));

        var ex = Assert.Throws<ArgumentException>(() => sum.CallInt64(UmkaValue.FromStaticArray(21L, 21L)));
        Assert.Contains("3 item", ex.Message);
    }

    [Fact]
    public void Runtime_marshals_static_arrays_of_fixed_layout_structs()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            type Point = struct {
                x, y: real
            }

            fn sumPoints*(points: [2]Point): real {
                return points[0].x + points[0].y + points[1].x + points[1].y
            }

            fn points*(): [2]Point {
                return [2]Point{Point{1.5, 2.5}, Point{3.5, 4.5}}
            }
            """);

        runtime.Compile();
        var sumPoints = runtime.GetFunction("sumPoints");
        var points = runtime.GetFunction("points");

        Assert.Equal(UmkaTypeKind.StaticArray, sumPoints.ParameterTypes[0].Kind);
        Assert.Equal(2, sumPoints.ParameterTypes[0].ItemCount);
        Assert.Equal(2 * Marshal.SizeOf<Point>(), sumPoints.ParameterTypes[0].NativeSize);
        Assert.False(sumPoints.ParameterTypes[0].HasReferences);
        Assert.Equal(UmkaTypeKind.StaticArray, points.ResultType.Kind);
        Assert.Equal(2, points.ResultType.ItemCount);
        Assert.Equal(2 * Marshal.SizeOf<Point>(), points.ResultType.NativeSize);
        Assert.False(points.ResultType.HasReferences);

        var sum = sumPoints.CallDouble(UmkaValue.FromStaticArray(
            new Point { X = 1.5, Y = 2.5 },
            new Point { X = 3.5, Y = 4.5 }));

        Assert.Equal(12.0, sum);

        var result = points.CallArray<Point>(2);

        Assert.Collection(
            result,
            point =>
            {
                Assert.Equal(1.5, point.X);
                Assert.Equal(2.5, point.Y);
            },
            point =>
            {
                Assert.Equal(3.5, point.X);
                Assert.Equal(4.5, point.Y);
            });
    }

    [Fact]
    public void Function_rejects_reference_bearing_struct_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            type TextBox = struct {
                value: str
            }

            fn length*(value: TextBox): int {
                return len(value.value)
            }
            """);

        runtime.Compile();
        var length = runtime.GetFunction("length");

        Assert.False(length.CanCallWith(UmkaValue.FromStruct(new NativeStringBox())));
        var ex = Assert.Throws<ArgumentException>(() => length.CallInt64(UmkaValue.FromStruct(new NativeStringBox())));
        Assert.Contains("contains Umka-managed references", ex.Message);
    }

    [Fact]
    public void Function_marshals_dynamic_array_arguments_and_results()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn sum*(values: []int): int {
                total := 0
                for _, value in values {
                    total += value
                }
                return total
            }

            fn joinText*(values: []str): str {
                result := ""
                for _, value in values {
                    result += value
                }
                return result
            }

            fn values*(): []int {
                return []int{5, 7, 11}
            }

            fn echoText*(values: []str): []str {
                return values
            }

            fn textValues*(): []str {
                return []str{"a", "b"}
            }

            fn anyLength*(values: []any): int {
                return len(values)
            }

            fn anyValues*(): []any {
                return []any{42}
            }
            """);

        runtime.Compile();

        var sum = runtime.GetFunction("sum");
        var joinText = runtime.GetFunction("joinText");
        var values = runtime.GetFunction("values");
        var echoText = runtime.GetFunction("echoText");
        var textValues = runtime.GetFunction("textValues");
        var anyLength = runtime.GetFunction("anyLength");
        var anyValues = runtime.GetFunction("anyValues");
        var parameterType = Assert.Single(sum.ParameterTypes);
        var textParameterType = Assert.Single(joinText.ParameterTypes);

        Assert.Equal(UmkaTypeKind.DynamicArray, parameterType.Kind);
        Assert.Equal(UmkaTypeKind.SignedInteger, parameterType.ElementKind);
        Assert.Equal("int", parameterType.ElementTypeName);
        Assert.Equal(8, parameterType.ElementNativeSize);
        Assert.False(parameterType.ElementHasReferences);
        Assert.False(parameterType.IsDeferred);
        Assert.True(sum.CanCallWith(UmkaValue.FromDynamicArray(10L, 14L, 18L)));
        Assert.False(sum.CanCallWith(UmkaValue.FromStaticArray(10L, 14L, 18L)));
        Assert.Equal(42, sum.CallInt64(UmkaValue.FromDynamicArray(10L, 14L, 18L)));
        Assert.Equal(0, sum.CallInt64(UmkaValue.FromDynamicArray<long>()));

        Assert.Equal(UmkaTypeKind.DynamicArray, textParameterType.Kind);
        Assert.Equal(UmkaTypeKind.String, textParameterType.ElementKind);
        Assert.True(textParameterType.ElementHasReferences);
        Assert.True(textParameterType.CanReadAsStringArray());
        Assert.False(textParameterType.IsDeferred);
        Assert.True(joinText.CanCallWith(UmkaValue.FromDynamicArray("um", "ka")));
        Assert.False(joinText.CanCallWith(UmkaValue.FromDynamicArray(10L, 14L)));
        Assert.Equal("umka", joinText.CallString(UmkaValue.FromDynamicArray("um", "ka")));

        Assert.True(values.CanReadResultAsDynamicArray<long>());
        Assert.Collection(
            values.CallDynamicArray<long>(),
            value => Assert.Equal(5L, value),
            value => Assert.Equal(7L, value),
            value => Assert.Equal(11L, value));
        Assert.True(values.TryCallDynamicArray<long>(out var tryValues));
        Assert.NotNull(tryValues);
        Assert.Collection(
            tryValues,
            value => Assert.Equal(5L, value),
            value => Assert.Equal(7L, value),
            value => Assert.Equal(11L, value));
        Assert.False(values.TryCallDynamicArray<int>(out var wrongElementSize));
        Assert.Null(wrongElementSize);
        Assert.Throws<InvalidOperationException>(() => values.CallDynamicArray<int>());

        Assert.Equal(UmkaTypeKind.String, textValues.ResultType.ElementKind);
        Assert.True(textValues.ResultType.ElementHasReferences);
        Assert.True(textValues.CanReadResultAsStringArray());
        Assert.False(textValues.CanReadResultAsDynamicArray<IntPtr>());
        Assert.Collection(
            textValues.CallStringArray(),
            value => Assert.Equal("a", value),
            value => Assert.Equal("b", value));
        Assert.True(textValues.TryCallStringArray(out var tryTextValues));
        Assert.NotNull(tryTextValues);
        Assert.Collection(
            tryTextValues,
            value => Assert.Equal("a", value),
            value => Assert.Equal("b", value));
        Assert.Collection(
            echoText.CallStringArray(UmkaValue.FromDynamicArray("left", "right")),
            value => Assert.Equal("left", value),
            value => Assert.Equal("right", value));
        var ex = Assert.Throws<InvalidOperationException>(() => textValues.CallDynamicArray<IntPtr>());
        Assert.Contains("contains Umka-managed references", ex.Message);

        var anyParameterType = Assert.Single(anyLength.ParameterTypes);
        Assert.Equal(UmkaTypeKind.DynamicArray, anyParameterType.Kind);
        Assert.Equal(UmkaTypeKind.Interface, anyParameterType.ElementKind);
        Assert.True(anyParameterType.ElementHasReferences);
        Assert.True(anyParameterType.IsDeferred);
        Assert.False(anyLength.CanCallWith(UmkaValue.FromDynamicArray(1L)));
        var anyArgEx = Assert.Throws<ArgumentException>(() => anyLength.CallInt64(UmkaValue.FromDynamicArray(1L)));
        Assert.Contains("element type", anyArgEx.Message);
        Assert.Contains("contains Umka-managed references", anyArgEx.Message);

        Assert.Equal(UmkaTypeKind.DynamicArray, anyValues.ResultType.Kind);
        Assert.Equal(UmkaTypeKind.Interface, anyValues.ResultType.ElementKind);
        Assert.True(anyValues.ResultType.ElementHasReferences);
        Assert.True(anyValues.ResultType.IsDeferred);
        Assert.False(anyValues.CanReadResultAsDynamicArray<IntPtr>());
        Assert.False(anyValues.TryCallDynamicArray<IntPtr>(out var anyResult));
        Assert.Null(anyResult);
        var anyResultEx = Assert.Throws<InvalidOperationException>(() => anyValues.CallDynamicArray<IntPtr>());
        Assert.Contains("element type", anyResultEx.Message);
        Assert.Contains("contains Umka-managed references", anyResultEx.Message);
    }

    [Fact]
    public void Function_marshals_nested_dynamic_array_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn sumMatrix*(values: [][]int): int {
                total := 0
                for _, row in values {
                    for _, value in row {
                        total += value
                    }
                }
                return total
            }

            fn textLength*(values: [][]str): int {
                total := 0
                for _, row in values {
                    for _, value in row {
                        total += len(value)
                    }
                }
                return total
            }

            fn anyMatrixLength*(values: [][]any): int {
                return len(values)
            }
            """);

        runtime.Compile();
        var sumMatrix = runtime.GetFunction("sumMatrix");
        var textLength = runtime.GetFunction("textLength");
        var anyMatrixLength = runtime.GetFunction("anyMatrixLength");
        var matrixRows = new[] { new[] { 10L, 14L }, Array.Empty<long>(), new[] { 18L } };
        var wrongMatrixRows = new[] { new[] { 1, 2 } };
        var emptyMatrixRows = new[] { Array.Empty<long>() };
        var textMatrixRows = new[] { new string?[] { "um", "ka" }, Array.Empty<string?>(), new string?[] { "sharp" } };
        var matrixValue = UmkaValue.FromNestedDynamicArray(matrixRows);
        var textMatrixValue = UmkaValue.FromNestedDynamicArray(textMatrixRows);

        var matrixType = Assert.Single(sumMatrix.ParameterTypes);
        Assert.Equal(UmkaTypeKind.DynamicArray, matrixType.Kind);
        Assert.Equal(UmkaTypeKind.DynamicArray, matrixType.ElementKind);
        Assert.Equal(UmkaTypeKind.SignedInteger, matrixType.NestedElementKind);
        Assert.Equal(8, matrixType.NestedElementNativeSize);
        Assert.False(matrixType.NestedElementHasReferences);
        Assert.True(sumMatrix.CanCallWith(matrixValue));
        Assert.False(sumMatrix.CanCallWith(UmkaValue.FromDynamicArray(10L, 14L, 18L)));
        Assert.False(sumMatrix.CanCallWith(UmkaValue.FromNestedDynamicArray(wrongMatrixRows)));
        Assert.Equal(42, sumMatrix.CallInt64(matrixValue));
        Assert.Equal(0, sumMatrix.CallInt64(UmkaValue.FromNestedDynamicArray(emptyMatrixRows)));

        var textMatrixType = Assert.Single(textLength.ParameterTypes);
        Assert.Equal(UmkaTypeKind.DynamicArray, textMatrixType.ElementKind);
        Assert.Equal(UmkaTypeKind.String, textMatrixType.NestedElementKind);
        Assert.True(textMatrixType.NestedElementHasReferences);
        Assert.True(textMatrixType.CanReadAsNestedStringArray());
        Assert.True(textLength.CanCallWith(textMatrixValue));
        Assert.False(textLength.CanCallWith(matrixValue));
        Assert.Equal(9, textLength.CallInt64(textMatrixValue));

        var anyMatrixType = Assert.Single(anyMatrixLength.ParameterTypes);
        Assert.Equal(UmkaTypeKind.Interface, anyMatrixType.NestedElementKind);
        Assert.True(anyMatrixType.NestedElementHasReferences);
        Assert.False(anyMatrixLength.CanCallWith(matrixValue));
        Assert.False(anyMatrixLength.CanCallWith(textMatrixValue));
        var ex = Assert.Throws<ArgumentException>(() => anyMatrixLength.CallInt64(matrixValue));
        Assert.Contains("inner element type", ex.Message);
    }

    [Fact]
    public void Function_copies_nested_dynamic_array_results()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn matrix*(): [][]int {
                return [][]int{[]int{1, 2}, []int{}, []int{3, 4, 5}}
            }

            fn textMatrix*(): [][]str {
                return [][]str{[]str{"a", "b"}, []str{}, []str{"c"}}
            }

            fn anyMatrix*(): [][]any {
                return [][]any{[]any{42}}
            }
            """);

        runtime.Compile();

        var matrix = runtime.GetFunction("matrix");
        var textMatrix = runtime.GetFunction("textMatrix");
        var anyMatrix = runtime.GetFunction("anyMatrix");

        Assert.Equal(UmkaTypeKind.DynamicArray, matrix.ResultType.Kind);
        Assert.Equal(UmkaTypeKind.DynamicArray, matrix.ResultType.ElementKind);
        Assert.Equal("[]int", matrix.ResultType.ElementTypeName);
        Assert.True(matrix.ResultType.ElementHasReferences);
        Assert.Equal(UmkaTypeKind.SignedInteger, matrix.ResultType.NestedElementKind);
        Assert.Equal("int", matrix.ResultType.NestedElementTypeName);
        Assert.Equal(8, matrix.ResultType.NestedElementNativeSize);
        Assert.False(matrix.ResultType.NestedElementHasReferences);
        Assert.False(matrix.ResultType.IsDeferred);
        Assert.True(matrix.CanReadResultAsNestedDynamicArray<long>());
        Assert.False(matrix.CanReadResultAsNestedDynamicArray<int>());
        Assert.False(matrix.CanReadResultAsDynamicArray<IntPtr>());

        var values = matrix.CallNestedDynamicArray<long>();
        Assert.Equal(3, values.Length);
        Assert.Equal([1L, 2L], values[0]);
        Assert.Empty(values[1]);
        Assert.Equal([3L, 4L, 5L], values[2]);
        Assert.True(matrix.TryCallNestedDynamicArray<long>(out var tryValues));
        Assert.NotNull(tryValues);
        Assert.Equal(3, tryValues.Length);
        Assert.Equal([1L, 2L], tryValues[0]);
        Assert.Empty(tryValues[1]);
        Assert.Equal([3L, 4L, 5L], tryValues[2]);
        Assert.False(matrix.TryCallNestedDynamicArray<int>(out var wrongElementSize));
        Assert.Null(wrongElementSize);

        var wrongElementSizeEx = Assert.Throws<InvalidOperationException>(() => matrix.CallNestedDynamicArray<int>());
        Assert.Contains("inner element type", wrongElementSizeEx.Message);

        Assert.Equal(UmkaTypeKind.DynamicArray, textMatrix.ResultType.ElementKind);
        Assert.Equal(UmkaTypeKind.String, textMatrix.ResultType.NestedElementKind);
        Assert.Equal("str", textMatrix.ResultType.NestedElementTypeName);
        Assert.Equal(IntPtr.Size, textMatrix.ResultType.NestedElementNativeSize);
        Assert.True(textMatrix.ResultType.NestedElementHasReferences);
        Assert.False(textMatrix.ResultType.IsDeferred);
        Assert.True(textMatrix.ResultType.CanReadAsNestedStringArray());
        Assert.True(textMatrix.CanReadResultAsNestedStringArray());
        Assert.False(textMatrix.CanReadResultAsNestedDynamicArray<IntPtr>());
        var textValues = textMatrix.CallNestedStringArray();
        Assert.Equal(3, textValues.Length);
        Assert.Collection(
            textValues[0],
            item => Assert.Equal("a", item),
            item => Assert.Equal("b", item));
        Assert.Empty(textValues[1]);
        Assert.Collection(textValues[2], item => Assert.Equal("c", item));
        Assert.True(textMatrix.TryCallNestedStringArray(out var tryTextValues));
        Assert.NotNull(tryTextValues);
        Assert.Collection(
            tryTextValues[0],
            item => Assert.Equal("a", item),
            item => Assert.Equal("b", item));
        Assert.Empty(tryTextValues[1]);
        Assert.Collection(tryTextValues[2], item => Assert.Equal("c", item));

        Assert.Equal(UmkaTypeKind.Interface, anyMatrix.ResultType.NestedElementKind);
        Assert.True(anyMatrix.ResultType.NestedElementHasReferences);
        Assert.True(anyMatrix.ResultType.IsDeferred);
        Assert.False(anyMatrix.ResultType.CanReadAsNestedStringArray());
        Assert.False(anyMatrix.TryCallNestedStringArray(out var anyValues));
        Assert.Null(anyValues);
        var anyEx = Assert.Throws<InvalidOperationException>(() => anyMatrix.CallNestedStringArray());
        Assert.Contains("jagged string array", anyEx.Message);
    }

    [Fact]
    public void Function_marshals_map_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn sumScores*(value: map[int]int): int {
                total := 0
                for _, key in keys(value) {
                    total += value[key]
                }
                return total
            }

            fn textScore*(value: map[str]int): int {
                return value["alpha"] + value["beta"]
            }

            fn labelLength*(value: map[int]str): int {
                return len(value[1]) + len(value[2])
            }

            fn aliasLength*(value: map[str]str): int {
                return len(value["a"]) + len(value["b"])
            }

            fn anyScores*(value: map[int]any): int {
                return len(value)
            }
            """);

        runtime.Compile();

        var sumScores = runtime.GetFunction("sumScores");
        var source = new Dictionary<long, long> { [1] = 10, [2] = 14, [3] = 18 };
        var scores = UmkaValue.FromMap(source);
        source[1] = 99;

        Assert.Equal(UmkaValueKind.Map, scores.Kind);
        Assert.Contains("Map", scores.ToString());
        var diagnostic = Assert.IsType<Dictionary<object, object?>>(scores.Value);
        Assert.Equal(10L, diagnostic[1L]);
        Assert.True(sumScores.CanCallWith(scores));
        Assert.False(sumScores.CanCallWith(UmkaValue.FromStringKeyMap(new Dictionary<string, long> { ["1"] = 10 })));
        Assert.Equal(42, sumScores.CallInt64(scores));
        Assert.Equal(0, sumScores.CallInt64(UmkaValue.FromMap(new Dictionary<long, long>())));

        var textScores = UmkaValue.FromStringKeyMap(new Dictionary<string, long>
        {
            ["alpha"] = 17,
            ["beta"] = 25,
        });
        var textScore = runtime.GetFunction("textScore");
        Assert.True(textScore.CanCallWith(textScores));
        Assert.Equal(42, textScore.CallInt64(textScores));

        var labels = UmkaValue.FromStringValueMap(new Dictionary<long, string?>
        {
            [1] = "one",
            [2] = "two",
        });
        Assert.True(runtime.GetFunction("labelLength").CanCallWith(labels));
        Assert.Equal(6, runtime.GetFunction("labelLength").CallInt64(labels));

        var aliases = UmkaValue.FromStringMap(new Dictionary<string, string?>
        {
            ["a"] = "alpha",
            ["b"] = "beta",
        });
        Assert.True(runtime.GetFunction("aliasLength").CanCallWith(aliases));
        Assert.Equal(9, runtime.GetFunction("aliasLength").CallInt64(aliases));

        var anyScores = runtime.GetFunction("anyScores");
        Assert.False(anyScores.CanCallWith(scores));
        var anyEx = Assert.Throws<ArgumentException>(() => anyScores.CallInt64(scores));
        Assert.Contains("value type", anyEx.Message);
        Assert.Contains("contains Umka-managed references", anyEx.Message);
    }

    [Fact]
    public void Function_rejects_unsupported_non_scalar_argument_kinds()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            type Drawable = interface {
                area(): real
            }

            fn takeInterface*(value: Drawable): int {
                return 0
            }

            fn takeAny*(value: any): int {
                return 0
            }

            fn takeClosure*(value: fn (): int): int {
                return 0
            }

            fn takeWeakPointer*(value: weak ^int): int {
                return 0
            }

            fn takeFiber*(value: fiber): int {
                return 0
            }
            """);

        runtime.Compile();

        var takeInterface = runtime.GetFunction("takeInterface");
        AssertInterfaceMetadata(takeInterface.ParameterTypes[0], expectedItemCount: 3);
        AssertUnsupportedArgument(takeInterface, UmkaTypeKind.Interface);

        var takeAny = runtime.GetFunction("takeAny");
        AssertAnyMetadata(takeAny.ParameterTypes[0]);
        Assert.True(takeAny.CanCallWith(UmkaAnyValue.From(42).ToValue()));
        Assert.False(takeAny.CanCallWith(UmkaValue.FromPointer(IntPtr.Zero)));

        AssertUnsupportedArgument(runtime.GetFunction("takeClosure"), UmkaTypeKind.Closure);

        var takeFiber = runtime.GetFunction("takeFiber");
        AssertFiberMetadata(takeFiber.ParameterTypes[0]);
        AssertUnsupportedArgument(takeFiber, UmkaTypeKind.Fiber);
    }

    [Fact]
    public void Function_roundtrips_opaque_weak_pointer_values()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            weakTarget := 42

            fn weakPointer*(): weak ^int {
                return weak ^int(&weakTarget)
            }

            fn echoWeak*(value: weak ^int): weak ^int {
                return value
            }

            fn sameWeak*(left: weak ^int, right: weak ^int): bool {
                return left == right
            }
            """);

        runtime.Compile();
        var weakPointer = runtime.GetFunction("weakPointer");

        Assert.Equal(UmkaTypeKind.WeakPointer, weakPointer.ResultType.Kind);
        Assert.False(weakPointer.ResultType.IsDeferred);
        Assert.True(weakPointer.ResultType.CanReadAsValue());
        Assert.True(weakPointer.ResultType.CanReadAsWeakPointer());
        Assert.True(weakPointer.CanReadResultAsValue());
        Assert.True(weakPointer.CanReadResultAsScalar<UmkaValue>());
        Assert.True(weakPointer.CanReadResultAsWeakPointer());
        Assert.False(weakPointer.CanReadResultAsScalar<ulong>());

        var handle = weakPointer.CallWeakPointer();
        Assert.NotEqual(0UL, handle);

        var dynamicValue = weakPointer.CallValue();
        Assert.Equal(UmkaValueKind.WeakPointer, dynamicValue.Kind);
        Assert.Equal(handle, dynamicValue.AsWeakPointer());
        Assert.True(dynamicValue.TryAsWeakPointer(out var tryDynamicHandle));
        Assert.Equal(handle, tryDynamicHandle);

        Assert.True(weakPointer.TryCallWeakPointer(out var tryHandle));
        Assert.Equal(handle, tryHandle);

        var echoWeak = runtime.GetFunction("echoWeak");
        var value = UmkaValue.FromWeakPointer(handle);
        Assert.Equal(UmkaValueKind.WeakPointer, value.Kind);
        Assert.Equal(handle, value.Value);
        Assert.Equal(handle, value.AsWeakPointer());
        Assert.True(value.TryAsWeakPointer(out var valueHandle));
        Assert.Equal(handle, valueHandle);
        Assert.Contains("WeakPointer", value.ToString());
        Assert.True(echoWeak.CanCallWith(value));
        Assert.False(echoWeak.CanCallWith(UmkaValue.From(handle)));
        Assert.Equal(handle, echoWeak.CallWeakPointer(value));

        var sameWeak = runtime.GetFunction("sameWeak");
        Assert.True(sameWeak.CallBoolean(value, UmkaValue.FromWeakPointer(handle)));
    }

    [Fact]
    public void Function_marshals_fixed_layout_weak_pointer_aggregates()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            weakTarget := 42

            type WeakBox = struct {
                value: weak ^int
            }

            fn weakPointer*(): weak ^int {
                return weak ^int(&weakTarget)
            }

            fn box*(): WeakBox {
                return WeakBox{weak ^int(&weakTarget)}
            }

            fn sameBox*(box: WeakBox, expected: weak ^int): bool {
                return box.value == expected
            }

            fn weakArray*(): [2]weak ^int {
                value := weak ^int(&weakTarget)
                return [2]weak ^int{value, value}
            }

            fn countSameArray*(values: [2]weak ^int, expected: weak ^int): int {
                total := 0
                for _, value in values {
                    if value == expected {
                        total++
                    }
                }
                return total
            }
            """);

        runtime.Compile();

        var handle = runtime.GetFunction("weakPointer").CallWeakPointer();
        var box = runtime.GetFunction("box");

        Assert.Equal(UmkaTypeKind.Struct, box.ResultType.Kind);
        Assert.Equal(Marshal.SizeOf<WeakBox>(), box.ResultType.NativeSize);
        Assert.False(box.ResultType.HasReferences);
        Assert.True(box.CanReadResultAsStruct<WeakBox>());

        var boxValue = box.CallStruct<WeakBox>();
        Assert.Equal(handle, boxValue.Value);
        Assert.True(runtime.GetFunction("sameBox").CallBoolean(
            UmkaValue.FromStruct(boxValue),
            UmkaValue.FromWeakPointer(handle)));

        var weakArray = runtime.GetFunction("weakArray");
        Assert.Equal(UmkaTypeKind.StaticArray, weakArray.ResultType.Kind);
        Assert.Equal(2, weakArray.ResultType.ItemCount);
        Assert.Equal(2 * Marshal.SizeOf<ulong>(), weakArray.ResultType.NativeSize);
        Assert.False(weakArray.ResultType.HasReferences);
        Assert.True(weakArray.CanReadResultAsArray<ulong>(2));

        var handles = weakArray.CallArray<ulong>(2);
        Assert.Equal([handle, handle], handles);
        Assert.Equal(2, runtime.GetFunction("countSameArray").CallInt64(
            UmkaValue.FromStaticArray(handles),
            UmkaValue.FromWeakPointer(handle)));
    }

    [Fact]
    public void Function_marshals_dynamic_arrays_of_opaque_weak_pointer_handles()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            weakTarget := 42

            fn weakPointer*(): weak ^int {
                return weak ^int(&weakTarget)
            }

            fn weakValues*(): []weak ^int {
                value := weak ^int(&weakTarget)
                return []weak ^int{value, value}
            }

            fn countSame*(values: []weak ^int, expected: weak ^int): int {
                total := 0
                for _, value in values {
                    if value == expected {
                        total++
                    }
                }
                return total
            }
            """);

        runtime.Compile();

        var handle = runtime.GetFunction("weakPointer").CallWeakPointer();
        var weakValues = runtime.GetFunction("weakValues");

        Assert.Equal(UmkaTypeKind.DynamicArray, weakValues.ResultType.Kind);
        Assert.Equal(UmkaTypeKind.WeakPointer, weakValues.ResultType.ElementKind);
        Assert.Equal(8, weakValues.ResultType.ElementNativeSize);
        Assert.False(weakValues.ResultType.ElementHasReferences);
        Assert.True(weakValues.CanReadResultAsDynamicArray<ulong>());

        var values = weakValues.CallDynamicArray<ulong>();
        Assert.Equal([handle, handle], values);
        Assert.True(weakValues.TryCallDynamicArray<ulong>(out var tryValues));
        Assert.NotNull(tryValues);
        Assert.Equal([handle, handle], tryValues);
        Assert.False(weakValues.TryCallDynamicArray<uint>(out var wrongElementSize));
        Assert.Null(wrongElementSize);

        var countSame = runtime.GetFunction("countSame");
        var parameterType = countSame.ParameterTypes[0];
        Assert.Equal(UmkaTypeKind.DynamicArray, parameterType.Kind);
        Assert.Equal(UmkaTypeKind.WeakPointer, parameterType.ElementKind);
        Assert.False(parameterType.ElementHasReferences);

        var arrayValue = UmkaValue.FromDynamicArray(values);
        Assert.True(countSame.CanCallWith(arrayValue, UmkaValue.FromWeakPointer(handle)));
        Assert.False(countSame.CanCallWith(UmkaValue.FromDynamicArray(1U, 2U), UmkaValue.FromWeakPointer(handle)));
        Assert.Equal(2, countSame.CallInt64(arrayValue, UmkaValue.FromWeakPointer(handle)));
    }

    [Fact]
    public void Function_copies_reference_free_map_results()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            weakTarget := 42

            fn scores*(): map[int]int {
                return map[int]int{1: 10, 2: 20, 3: 30}
            }

            fn empty*(): map[int]int {
                return map[int]int{}
            }

            fn weakPointer*(): weak ^int {
                return weak ^int(&weakTarget)
            }

            fn weakKeyScores*(): map[weak ^int]int {
                key := weak ^int(&weakTarget)
                return map[weak ^int]int{key: 7}
            }

            fn weakValueScores*(): map[int]weak ^int {
                value := weak ^int(&weakTarget)
                return map[int]weak ^int{1: value}
            }

            fn textScores*(): map[str]int {
                return map[str]int{"answer": 42}
            }

            fn labels*(): map[int]str {
                return map[int]str{1: "one", 2: "two"}
            }

            fn aliases*(): map[str]str {
                return map[str]str{"a": "alpha", "b": "beta"}
            }

            fn anyScores*(): map[int]any {
                return map[int]any{1: 42}
            }
            """);

        runtime.Compile();
        var scores = runtime.GetFunction("scores");

        Assert.Equal(UmkaTypeKind.Map, scores.ResultType.Kind);
        Assert.Equal(UmkaTypeKind.SignedInteger, scores.ResultType.MapKeyKind);
        Assert.Equal("int", scores.ResultType.MapKeyTypeName);
        Assert.Equal(8, scores.ResultType.MapKeyNativeSize);
        Assert.False(scores.ResultType.MapKeyHasReferences);
        Assert.Equal(UmkaTypeKind.SignedInteger, scores.ResultType.MapValueKind);
        Assert.Equal("int", scores.ResultType.MapValueTypeName);
        Assert.Equal(8, scores.ResultType.MapValueNativeSize);
        Assert.False(scores.ResultType.MapValueHasReferences);
        Assert.False(scores.ResultType.IsDeferred);
        Assert.True(scores.CanReadResultAsMap<long, long>());
        Assert.False(scores.CanReadResultAsMap<int, long>());

        var values = scores.CallMap<long, long>();
        Assert.Equal(3, values.Count);
        Assert.Equal(10, values[1]);
        Assert.Equal(20, values[2]);
        Assert.Equal(30, values[3]);

        Assert.True(scores.TryCallMap<long, long>(out var tryValues));
        Assert.NotNull(tryValues);
        Assert.Equal(20, tryValues[2]);
        Assert.False(scores.TryCallMap<int, long>(out var wrongKeySize));
        Assert.Null(wrongKeySize);
        Assert.Throws<InvalidOperationException>(() => scores.CallMap<int, long>());

        Assert.Empty(runtime.GetFunction("empty").CallMap<long, long>());

        var weakHandle = runtime.GetFunction("weakPointer").CallWeakPointer();
        var weakKeyScores = runtime.GetFunction("weakKeyScores");
        Assert.Equal(UmkaTypeKind.WeakPointer, weakKeyScores.ResultType.MapKeyKind);
        Assert.Equal(8, weakKeyScores.ResultType.MapKeyNativeSize);
        Assert.False(weakKeyScores.ResultType.MapKeyHasReferences);
        Assert.True(weakKeyScores.CanReadResultAsMap<ulong, long>());
        var weakKeyMap = weakKeyScores.CallMap<ulong, long>();
        Assert.Equal(7, weakKeyMap[weakHandle]);

        var weakValueScores = runtime.GetFunction("weakValueScores");
        Assert.Equal(UmkaTypeKind.WeakPointer, weakValueScores.ResultType.MapValueKind);
        Assert.Equal(8, weakValueScores.ResultType.MapValueNativeSize);
        Assert.False(weakValueScores.ResultType.MapValueHasReferences);
        Assert.True(weakValueScores.CanReadResultAsMap<long, ulong>());
        var weakValueMap = weakValueScores.CallMap<long, ulong>();
        Assert.Equal(weakHandle, weakValueMap[1]);

        var textScores = runtime.GetFunction("textScores");
        Assert.True(textScores.ResultType.MapKeyHasReferences);
        Assert.False(textScores.ResultType.IsDeferred);
        Assert.False(textScores.CanReadResultAsMap<IntPtr, long>());
        Assert.True(textScores.CanReadResultAsStringKeyMap<long>());
        Assert.False(textScores.CanReadResultAsStringKeyMap<int>());
        var textMap = textScores.CallStringKeyMap<long>();
        Assert.Equal(42, textMap["answer"]);
        Assert.True(textScores.TryCallStringKeyMap<long>(out var tryTextMap));
        Assert.NotNull(tryTextMap);
        Assert.Equal(42, tryTextMap["answer"]);
        Assert.False(textScores.TryCallStringKeyMap<int>(out var wrongStringKeyValueSize));
        Assert.Null(wrongStringKeyValueSize);
        var ex = Assert.Throws<InvalidOperationException>(() => textScores.CallMap<IntPtr, long>());
        Assert.Contains("key type 'str' contains Umka-managed references", ex.Message);

        var labels = runtime.GetFunction("labels");
        Assert.True(labels.ResultType.MapValueHasReferences);
        Assert.False(labels.ResultType.IsDeferred);
        Assert.True(labels.CanReadResultAsStringValueMap<long>());
        var labelMap = labels.CallStringValueMap<long>();
        Assert.Equal("one", labelMap[1]);
        Assert.Equal("two", labelMap[2]);
        Assert.True(labels.TryCallStringValueMap<long>(out var tryLabelMap));
        Assert.NotNull(tryLabelMap);
        Assert.Equal("two", tryLabelMap[2]);

        var aliases = runtime.GetFunction("aliases");
        Assert.True(aliases.ResultType.MapKeyHasReferences);
        Assert.True(aliases.ResultType.MapValueHasReferences);
        Assert.False(aliases.ResultType.IsDeferred);
        Assert.True(aliases.CanReadResultAsStringMap());
        var aliasMap = aliases.CallStringMap();
        Assert.Equal("alpha", aliasMap["a"]);
        Assert.Equal("beta", aliasMap["b"]);
        Assert.True(aliases.TryCallStringMap(out var tryAliasMap));
        Assert.NotNull(tryAliasMap);
        Assert.Equal("beta", tryAliasMap["b"]);

        var anyScores = runtime.GetFunction("anyScores");
        Assert.Equal(UmkaTypeKind.Map, anyScores.ResultType.Kind);
        Assert.Equal(UmkaTypeKind.SignedInteger, anyScores.ResultType.MapKeyKind);
        Assert.False(anyScores.ResultType.MapKeyHasReferences);
        Assert.Equal(UmkaTypeKind.Interface, anyScores.ResultType.MapValueKind);
        Assert.True(anyScores.ResultType.MapValueHasReferences);
        Assert.True(anyScores.ResultType.IsDeferred);
        Assert.False(anyScores.CanReadResultAsMap<long, IntPtr>());
        Assert.False(anyScores.TryCallMap<long, IntPtr>(out var anyMap));
        Assert.Null(anyMap);
        var anyMapEx = Assert.Throws<InvalidOperationException>(() => anyScores.CallMap<long, IntPtr>());
        Assert.Contains("value type", anyMapEx.Message);
        Assert.Contains("contains Umka-managed references", anyMapEx.Message);
    }

    [Fact]
    public void Function_copies_dynamic_array_value_map_results()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn rows*(): map[int][]int {
                return map[int][]int{1: []int{1, 2}, 2: []int{}, 3: []int{3, 4, 5}}
            }

            fn namedRows*(): map[str][]int {
                return map[str][]int{"left": []int{10, 11}, "empty": []int{}}
            }

            fn textRows*(): map[int][]str {
                return map[int][]str{1: []str{"a", "b"}, 2: []str{}}
            }

            fn namedTextRows*(): map[str][]str {
                return map[str][]str{"left": []str{"x", "y"}, "empty": []str{}}
            }

            fn anyRows*(): map[int][]any {
                return map[int][]any{1: []any{42}}
            }
            """);

        runtime.Compile();

        var rows = runtime.GetFunction("rows");
        Assert.Equal(UmkaTypeKind.Map, rows.ResultType.Kind);
        Assert.Equal(UmkaTypeKind.DynamicArray, rows.ResultType.MapValueKind);
        Assert.Equal("[]int", rows.ResultType.MapValueTypeName);
        Assert.Equal(IntPtr.Size * 3, rows.ResultType.MapValueNativeSize);
        Assert.True(rows.ResultType.MapValueHasReferences);
        Assert.Equal(UmkaTypeKind.SignedInteger, rows.ResultType.MapValueElementKind);
        Assert.Equal("int", rows.ResultType.MapValueElementTypeName);
        Assert.Equal(8, rows.ResultType.MapValueElementNativeSize);
        Assert.False(rows.ResultType.MapValueElementHasReferences);
        Assert.False(rows.ResultType.IsDeferred);
        Assert.True(rows.CanReadResultAsDynamicArrayValueMap<long, long>());
        Assert.False(rows.CanReadResultAsDynamicArrayValueMap<int, long>());
        Assert.False(rows.CanReadResultAsMap<long, IntPtr>());

        var rowValues = rows.CallDynamicArrayValueMap<long, long>();
        Assert.Equal([1L, 2L], rowValues[1]);
        Assert.Empty(rowValues[2]);
        Assert.Equal([3L, 4L, 5L], rowValues[3]);
        Assert.True(rows.TryCallDynamicArrayValueMap<long, long>(out var tryRowValues));
        Assert.NotNull(tryRowValues);
        Assert.Equal([3L, 4L, 5L], tryRowValues[3]);
        Assert.False(rows.TryCallDynamicArrayValueMap<int, long>(out var wrongKeySize));
        Assert.Null(wrongKeySize);

        var namedRows = runtime.GetFunction("namedRows");
        Assert.True(namedRows.ResultType.MapKeyHasReferences);
        Assert.Equal(UmkaTypeKind.DynamicArray, namedRows.ResultType.MapValueKind);
        Assert.False(namedRows.ResultType.IsDeferred);
        Assert.True(namedRows.CanReadResultAsStringKeyDynamicArrayValueMap<long>());
        Assert.False(namedRows.CanReadResultAsStringKeyDynamicArrayValueMap<int>());

        var namedValues = namedRows.CallStringKeyDynamicArrayValueMap<long>();
        Assert.Equal([10L, 11L], namedValues["left"]);
        Assert.Empty(namedValues["empty"]);
        Assert.True(namedRows.TryCallStringKeyDynamicArrayValueMap<long>(out var tryNamedValues));
        Assert.NotNull(tryNamedValues);
        Assert.Equal([10L, 11L], tryNamedValues["left"]);
        Assert.False(namedRows.TryCallStringKeyDynamicArrayValueMap<int>(out var wrongElementSize));
        Assert.Null(wrongElementSize);

        var textRows = runtime.GetFunction("textRows");
        Assert.Equal(UmkaTypeKind.DynamicArray, textRows.ResultType.MapValueKind);
        Assert.Equal(UmkaTypeKind.String, textRows.ResultType.MapValueElementKind);
        Assert.Equal("str", textRows.ResultType.MapValueElementTypeName);
        Assert.Equal(IntPtr.Size, textRows.ResultType.MapValueElementNativeSize);
        Assert.True(textRows.ResultType.MapValueElementHasReferences);
        Assert.False(textRows.ResultType.IsDeferred);
        Assert.True(textRows.CanReadResultAsStringArrayValueMap<long>());
        Assert.False(textRows.CanReadResultAsStringArrayValueMap<int>());
        Assert.False(textRows.CanReadResultAsDynamicArrayValueMap<long, IntPtr>());

        var textValues = textRows.CallStringArrayValueMap<long>();
        Assert.Collection(
            textValues[1],
            value => Assert.Equal("a", value),
            value => Assert.Equal("b", value));
        Assert.Empty(textValues[2]);
        Assert.True(textRows.TryCallStringArrayValueMap<long>(out var tryTextValues));
        Assert.NotNull(tryTextValues);
        Assert.Collection(
            tryTextValues[1],
            value => Assert.Equal("a", value),
            value => Assert.Equal("b", value));
        Assert.False(textRows.TryCallStringArrayValueMap<int>(out var wrongTextKeySize));
        Assert.Null(wrongTextKeySize);

        var namedTextRows = runtime.GetFunction("namedTextRows");
        Assert.True(namedTextRows.CanReadResultAsStringKeyStringArrayValueMap());
        var namedTextValues = namedTextRows.CallStringKeyStringArrayValueMap();
        Assert.Collection(
            namedTextValues["left"],
            value => Assert.Equal("x", value),
            value => Assert.Equal("y", value));
        Assert.Empty(namedTextValues["empty"]);
        Assert.True(namedTextRows.TryCallStringKeyStringArrayValueMap(out var tryNamedTextValues));
        Assert.NotNull(tryNamedTextValues);
        Assert.Collection(
            tryNamedTextValues["left"],
            value => Assert.Equal("x", value),
            value => Assert.Equal("y", value));

        var anyRows = runtime.GetFunction("anyRows");
        Assert.Equal(UmkaTypeKind.Interface, anyRows.ResultType.MapValueElementKind);
        Assert.True(anyRows.ResultType.MapValueElementHasReferences);
        Assert.True(anyRows.ResultType.IsDeferred);
        Assert.False(anyRows.CanReadResultAsStringArrayValueMap<long>());
        Assert.False(anyRows.TryCallStringArrayValueMap<long>(out var anyValues));
        Assert.Null(anyValues);
        Assert.Throws<InvalidOperationException>(() => anyRows.CallStringArrayValueMap<long>());
    }

    [Fact]
    public void Function_exposes_deferred_result_metadata_and_rejects_readers()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            type IntFn = fn (): int

            fn mapValue*(): map[str]int {
                return map[str]int{"answer": 42}
            }

            type Speaker = interface {
                speak(): str
            }

            type Dog = struct {
                woofCount: int
            }

            fn (dog: ^Dog) speak(): str {
                dog.woofCount++
                return "woof"
            }

            fn interfaceValue*(): Speaker {
                return Dog{woofCount: 0}
            }

            fn closureValue*(): IntFn {
                return fn (): int {
                    return 42
                }
            }

            fn fiberValue*(): fiber {
                return make(fiber, fn() {})
            }

            weakTarget := 42

            fn weakPointer*(): weak ^int {
                return weak ^int(&weakTarget)
            }

            fn anyValue*(): any {
                return 42
            }
            """);

        runtime.Compile();

        AssertUnsupportedResult(runtime.GetFunction("mapValue"), UmkaTypeKind.Map);

        var interfaceValue = runtime.GetFunction("interfaceValue");
        AssertInterfaceMetadata(interfaceValue.ResultType, expectedItemCount: 3);
        AssertUnsupportedResult(interfaceValue, UmkaTypeKind.Interface);

        var closureValue = runtime.GetFunction("closureValue");
        Assert.Equal(UmkaTypeKind.Closure, closureValue.ResultType.Kind);
        Assert.True(closureValue.ResultType.HasReferences);
        Assert.True(closureValue.ResultType.IsCallable);
        Assert.False(closureValue.ResultType.IsDeferred);
        Assert.True(closureValue.ResultType.NativeSize >= IntPtr.Size * 2);
        Assert.True(closureValue.CanReadResultAsNativeValue());
        using (var retainedClosure = closureValue.CallNativeValue())
        {
            var callable = retainedClosure.AsCallable();
            Assert.Equal(42, callable.CallInt64());
        }

        var fiberValue = runtime.GetFunction("fiberValue");
        AssertFiberMetadata(fiberValue.ResultType);
        AssertUnsupportedResult(fiberValue, UmkaTypeKind.Fiber);

        var anyValue = runtime.GetFunction("anyValue");
        AssertAnyMetadata(anyValue.ResultType);
        Assert.True(anyValue.CanReadResultAsAny());
        Assert.Equal(42, anyValue.CallAny().Payload.AsInt64());
    }

    [Fact]
    public void Function_rejects_result_reader_mismatches()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn intResult*(): int {
                return 42
            }

            fn stringResult*(): str {
                return "value"
            }

            fn pair*(): [2]real {
                return [2]real{1.5, 2.5}
            }
            """);

        runtime.Compile();

        var intResult = runtime.GetFunction("intResult");
        var stringResult = runtime.GetFunction("stringResult");
        var pair = runtime.GetFunction("pair");

        Assert.Equal(42, intResult.CallInt64());
        Assert.Equal("value", stringResult.CallString());
        Assert.Equal(1.5, pair.CallStruct<RealPair>().X);

        Assert.Throws<InvalidOperationException>(() => intResult.CallString());
        Assert.Throws<InvalidOperationException>(() => intResult.CallStruct<RealPair>());
        Assert.Throws<InvalidOperationException>(() => stringResult.CallInt64());
        Assert.Throws<InvalidOperationException>(() => pair.CallVoid());
        Assert.False(pair.TryCallVoid());
        Assert.Throws<InvalidOperationException>(() => pair.CallValue());
        Assert.Throws<InvalidOperationException>(() => pair.CallInt64());
    }

    [Fact]
    public void Function_call_void_executes_void_results_and_value_readers_reject_them()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            ticks := 0

            fn tick*() {
                ticks++
            }

            fn count*(): int {
                return ticks
            }
            """);

        runtime.Compile();

        var tick = runtime.GetFunction("tick");
        var count = runtime.GetFunction("count");

        Assert.Equal(0, tick.ParameterCount);
        Assert.Equal(UmkaTypeKind.Void, tick.ResultType.Kind);
        Assert.Contains("void", tick.ResultType.TypeName);

        Assert.Throws<InvalidOperationException>(() => tick.CallInt64());
        Assert.Throws<InvalidOperationException>(() => tick.CallString());
        Assert.Throws<InvalidOperationException>(() => tick.CallPointer());
        Assert.Throws<InvalidOperationException>(() => tick.CallStruct<IntPair>());
        Assert.Throws<InvalidOperationException>(() => tick.CallArray<long>(1));
        Assert.Equal(0, count.CallInt64());

        tick.CallVoid();
        tick.CallVoid(ReadOnlySpan<UmkaValue>.Empty);
        Assert.True(tick.TryCallVoid());
        Assert.True(tick.TryCallVoid(ReadOnlySpan<UmkaValue>.Empty));
        Assert.True(count.TryCallVoid());

        Assert.Equal(4, count.CallInt64());
    }

    [Fact]
    public void Runtime_marshals_nested_struct_results()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            type Point = struct {
                x, y: real
            }

            type Segment = struct {
                start, finish: Point
            }

            fn segment*(): Segment {
                return Segment{Point{1.5, 2.5}, Point{3.5, 4.5}}
            }
            """);

        runtime.Compile();

        var segment = runtime.GetFunction("segment").CallStruct<Segment>();

        Assert.Equal(1.5, segment.Start.X);
        Assert.Equal(2.5, segment.Start.Y);
        Assert.Equal(3.5, segment.Finish.X);
        Assert.Equal(4.5, segment.Finish.Y);
    }

    [Fact]
    public void Runtime_marshals_multiple_return_values_as_fixed_layout_structs()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn split*(): (int, int) {
                return 19, 23
            }
            """);

        runtime.Compile();

        var split = runtime.GetFunction("split");
        var result = split.CallStruct<IntPair>();

        Assert.Equal(UmkaTypeKind.Struct, split.ResultType.Kind);
        Assert.Equal(19, result.X);
        Assert.Equal(23, result.Y);

        Assert.True(split.TryCallStruct<IntPair>(out var tryResult));
        Assert.Equal(19, tryResult.X);
        Assert.Equal(23, tryResult.Y);
        Assert.Throws<ArgumentException>(() => split.TryCallStruct<IntPair>(out _, UmkaValue.From(1)));
    }

    [Fact]
    public void Function_structured_result_readers_require_exact_unmanaged_layouts()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            type Pair = struct {
                x, y: int
            }

            fn pair*(): Pair {
                return Pair{19, 23}
            }

            fn ints*(): [1]int {
                return [1]int{42}
            }
            """);

        runtime.Compile();

        var pair = runtime.GetFunction("pair");
        var ints = runtime.GetFunction("ints");

        Assert.False(pair.TryCallStruct<IntTriple>(out var wrongSizePair));
        Assert.Equal(default, wrongSizePair);
        Assert.False(pair.TryCallStruct<ManagedStringBox>(out var wrongManagedPair));
        Assert.Equal(default, wrongManagedPair);
        Assert.False(ints.TryCallArray<ManagedStringBox>(1, out var wrongManagedArray));
        Assert.Null(wrongManagedArray);

        var sizeEx = Assert.Throws<InvalidOperationException>(() => pair.CallStruct<IntTriple>());
        Assert.Contains("24 bytes", sizeEx.Message);
        Assert.Contains("16 bytes", sizeEx.Message);

        var structRefEx = Assert.Throws<ArgumentException>(() => pair.CallStruct<ManagedStringBox>());
        Assert.Contains("managed references", structRefEx.Message);

        var arrayRefEx = Assert.Throws<ArgumentException>(() => ints.CallArray<ManagedStringBox>(1));
        Assert.Contains("managed references", arrayRefEx.Message);
    }

    [Fact]
    public void Function_structured_result_readers_reject_reference_bearing_umka_results_before_calling()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            type TextBox = struct {
                value: str
            }

            fn textBox*(): TextBox {
                host::mark()
                return TextBox{"value"}
            }

            fn textArray*(): [2]str {
                host::mark()
                return [2]str{"a", "b"}
            }
            """);

        var called = false;
        runtime.AddModule("host.um", "fn mark*()");
        runtime.Register("mark", _ =>
        {
            called = true;
            return UmkaValue.Void;
        });

        runtime.Compile();

        Assert.False(runtime.GetFunction("textBox").TryCallStruct<NativeStringBox>(out var textBox));
        Assert.Equal(default, textBox);
        Assert.False(called);

        Assert.False(runtime.GetFunction("textArray").TryCallArray<IntPtr>(2, out var textArray));
        Assert.Null(textArray);
        Assert.False(called);

        var structEx = Assert.Throws<InvalidOperationException>(() => runtime.GetFunction("textBox").CallStruct<NativeStringBox>());
        var arrayEx = Assert.Throws<InvalidOperationException>(() => runtime.GetFunction("textArray").CallArray<IntPtr>(2));

        Assert.Contains("contains Umka-managed references", structEx.Message);
        Assert.Contains("contains Umka-managed references", arrayEx.Message);
        Assert.False(called);
    }

    [Fact]
    public void Runtime_marshals_static_array_results()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn ints*(): [3]int {
                return [3]int{1, 2, 3}
            }

            fn reals*(x, y: real): [2]real {
                return [2]real{x, y}
            }

            type Point = struct {
                x, y: real
            }

            fn point*(): Point {
                return Point{1.5, 2.5}
            }
            """);

        runtime.Compile();

        var ints = runtime.GetFunction("ints");
        var reals = runtime.GetFunction("reals");
        var point = runtime.GetFunction("point");

        Assert.Collection(
            ints.CallArray<long>(3),
            value => Assert.Equal(1L, value),
            value => Assert.Equal(2L, value),
            value => Assert.Equal(3L, value));

        Assert.True(ints.TryCallArray<long>(3, out var tryInts));
        Assert.NotNull(tryInts);
        Assert.Collection(
            tryInts,
            value => Assert.Equal(1L, value),
            value => Assert.Equal(2L, value),
            value => Assert.Equal(3L, value));

        Assert.Collection(
            reals.CallArray<double>(
                2,
                UmkaValue.From(2.5),
                UmkaValue.From(7.5)),
            value => Assert.Equal(2.5, value),
            value => Assert.Equal(7.5, value));

        Assert.True(reals.TryCallArray<double>(
            2,
            out var tryReals,
            UmkaValue.From(2.5),
            UmkaValue.From(7.5)));
        Assert.NotNull(tryReals);
        Assert.Collection(
            tryReals,
            value => Assert.Equal(2.5, value),
            value => Assert.Equal(7.5, value));

        Assert.False(ints.TryCallArray<int>(3, out var wrongElementSize));
        Assert.Null(wrongElementSize);
        Assert.False(ints.TryCallArray<long>(2, out var wrongLength));
        Assert.Null(wrongLength);
        Assert.False(point.TryCallArray<double>(2, out var wrongKind));
        Assert.Null(wrongKind);

        Assert.Throws<InvalidOperationException>(() => ints.CallArray<int>(3));
        var lengthEx = Assert.Throws<InvalidOperationException>(() => ints.CallArray<long>(2));
        Assert.Contains("3 item", lengthEx.Message);
        Assert.Contains("2 item", lengthEx.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => ints.CallArray<long>(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ints.TryCallArray<long>(-1, out _));
        Assert.Throws<ArgumentException>(() => reals.TryCallArray<double>(2, out _, UmkaValue.From(2.5)));
        Assert.Throws<InvalidOperationException>(() => point.CallArray<double>(2));
    }

    [Fact]
    public void Runtime_marshals_callback_result_kinds()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn getInt*(): int {
                return host::getInt()
            }

            fn getUint*(): uint {
                return host::getUint()
            }

            fn getReal*(): real {
                return host::getReal()
            }

            fn getBool*(): bool {
                return host::getBool()
            }

            fn getString*(): str {
                return host::getString()
            }

            fn getPointer*(): ^int {
                return host::getPointer()
            }

            fn getChar*(): char {
                return 'A'
            }

            fn tick*() {
            }

            fn pair*(): [2]real {
                return [2]real{1.0, 2.0}
            }
            """);

        runtime.AddModule("host.um", """
            fn getInt*(): int
            fn getUint*(): uint
            fn getReal*(): real
            fn getBool*(): bool
            fn getString*(): str
            fn getPointer*(): ^int
            """);

        var pointer = new IntPtr(0x654321);
        runtime.Register("getInt", _ => UmkaValue.From(-42L));
        runtime.Register("getUint", _ => UmkaValue.From(42UL));
        runtime.Register("getReal", _ => UmkaValue.From(12.25));
        runtime.Register("getBool", _ => UmkaValue.From(true));
        runtime.Register("getString", _ => UmkaValue.From("zażółć"));
        runtime.Register("getPointer", _ => UmkaValue.FromPointer(pointer));

        runtime.Compile();

        Assert.Equal(-42, runtime.GetFunction("getInt").CallInt64());
        Assert.Equal(42UL, runtime.GetFunction("getUint").CallUInt64());
        Assert.Equal(12.25, runtime.GetFunction("getReal").CallDouble());
        Assert.True(runtime.GetFunction("getBool").CallBoolean());
        Assert.Equal("zażółć", runtime.GetFunction("getString").CallString());
        Assert.Equal(pointer, runtime.GetFunction("getPointer").CallPointer());

        Assert.Equal(-42, runtime.GetFunction("getInt").CallValue().AsInt64());
        Assert.Equal(42UL, runtime.GetFunction("getUint").CallValue().AsUInt64());
        Assert.Equal(12.25, runtime.GetFunction("getReal").CallValue().AsDouble());
        Assert.True(runtime.GetFunction("getBool").CallValue().AsBoolean());
        Assert.Equal("zażółć", runtime.GetFunction("getString").CallValue().AsString());
        Assert.Equal(pointer, runtime.GetFunction("getPointer").CallValue().AsPointer());
        Assert.Equal('A', runtime.GetFunction("getChar").CallValue().AsChar());
        Assert.Equal(UmkaValueKind.Void, runtime.GetFunction("tick").CallValue().Kind);

        Assert.True(runtime.GetFunction("getInt").TryCallValue(out var dynamicInt));
        Assert.Equal(-42, dynamicInt.AsInt64());
        Assert.True(runtime.GetFunction("getString").TryCallValue(ReadOnlySpan<UmkaValue>.Empty, out var dynamicString));
        Assert.Equal("zażółć", dynamicString.AsString());
        Assert.True(runtime.GetFunction("tick").TryCallValue(out var dynamicVoid));
        Assert.Equal(UmkaValueKind.Void, dynamicVoid.Kind);
        Assert.False(runtime.GetFunction("pair").TryCallValue(out var unsupportedDynamic));
        Assert.Equal(UmkaValueKind.Void, unsupportedDynamic.Kind);
        Assert.Throws<ArgumentException>(() => runtime.GetFunction("pair").TryCallValue(out _, UmkaValue.From(1)));
    }

    [Fact]
    public void UmkaValue_rejects_wrong_kind_reads()
    {
        Assert.Throws<InvalidOperationException>(() => UmkaValue.From(1L).AsDouble());
        Assert.Throws<InvalidOperationException>(() => UmkaValue.From("value").AsInt64());
        Assert.Throws<InvalidOperationException>(() => UmkaValue.From("value").AsChar());
        Assert.Throws<InvalidOperationException>(() => UmkaValue.From("value").AsSingle());
        Assert.Throws<InvalidOperationException>(() => UmkaValue.FromPointer(IntPtr.Zero).AsString());
    }

    private static void AssertValue(UmkaValue value, UmkaValueKind expectedKind, long expectedValue)
    {
        Assert.Equal(expectedKind, value.Kind);
        Assert.Equal(expectedValue, value.AsInt64());
    }

    private static void AssertValue(UmkaValue value, UmkaValueKind expectedKind, ulong expectedValue)
    {
        Assert.Equal(expectedKind, value.Kind);
        Assert.Equal(expectedValue, value.AsUInt64());
    }

    private static void AssertStaticArraySnapshot(long[] values)
    {
        Assert.Collection(
            values,
            value => Assert.Equal(1L, value),
            value => Assert.Equal(2L, value),
            value => Assert.Equal(3L, value));
    }

    private static void AssertStringArraySnapshot(string?[] values)
    {
        Assert.Collection(
            values,
            value => Assert.Equal("a", value),
            Assert.Null,
            value => Assert.Equal("b", value));
    }

    private static void AssertNestedArraySnapshot(long[][] values)
    {
        Assert.Equal(3, values.Length);
        Assert.Equal([1L, 2L], values[0]);
        Assert.Empty(values[1]);
        Assert.Equal([3L], values[2]);
    }

    private static void AssertNestedStringArraySnapshot(string?[][] values)
    {
        Assert.Equal(3, values.Length);
        Assert.Collection(
            values[0],
            value => Assert.Equal("a", value),
            Assert.Null);
        Assert.Empty(values[1]);
        Assert.Collection(values[2], value => Assert.Equal("b", value));
    }

    private static void AssertUnsupportedArgument(
        UmkaFunction function,
        UmkaTypeKind expectedKind,
        string? expectedTypeName = null,
        string? expectedMessage = null)
    {
        Assert.Single(function.ParameterTypes);
        Assert.Equal(expectedKind, function.ParameterTypes[0].Kind);
        if (expectedTypeName is not null)
            Assert.Contains(expectedTypeName, function.ParameterTypes[0].TypeName);

        var ex = Assert.Throws<ArgumentException>(() => function.CallInt64(UmkaValue.FromPointer(IntPtr.Zero)));

        Assert.Contains(expectedMessage ?? "does not support", ex.Message);
    }

    private static void AssertUnsupportedResult(
        UmkaFunction function,
        UmkaTypeKind expectedKind,
        string? expectedTypeName = null)
    {
        Assert.Equal(expectedKind, function.ResultType.Kind);
        if (expectedTypeName is not null)
            Assert.Contains(expectedTypeName, function.ResultType.TypeName);

        Assert.Throws<InvalidOperationException>(() => function.CallInt64());
        Assert.Throws<InvalidOperationException>(() => function.CallString());
        Assert.Throws<InvalidOperationException>(() => function.CallPointer());
        Assert.Throws<InvalidOperationException>(() => function.CallValue());
        Assert.Throws<InvalidOperationException>(() => function.CallStruct<IntPair>());
    }

    private static void AssertInterfaceMetadata(UmkaTypeInfo type, int expectedItemCount)
    {
        Assert.Equal(UmkaTypeKind.Interface, type.Kind);
        Assert.True(type.HasReferences);
        Assert.True(type.IsDeferred);
        Assert.Equal(expectedItemCount, type.ItemCount);
        Assert.True(type.NativeSize >= IntPtr.Size * 2);
        Assert.False(type.CanReadAsValue());
    }

    private static void AssertAnyMetadata(UmkaTypeInfo type)
    {
        Assert.Equal(UmkaTypeKind.Interface, type.Kind);
        Assert.True(type.HasReferences);
        Assert.False(type.IsDeferred);
        Assert.True(type.IsAny);
        Assert.True(type.CanReadAsValue());
        Assert.Contains("interface", type.TypeName);
        Assert.Equal(2, type.ItemCount);
        Assert.Equal(IntPtr.Size * 2, type.NativeSize);
    }

    private static void AssertFiberMetadata(UmkaTypeInfo type)
    {
        Assert.Equal(UmkaTypeKind.Fiber, type.Kind);
        Assert.Equal("fiber", type.TypeName);
        Assert.True(type.HasReferences);
        Assert.True(type.IsDeferred);
        Assert.Equal(IntPtr.Size, type.NativeSize);
        Assert.False(type.CanReadAsValue());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RealPair
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IntPair
    {
        public long X;
        public long Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IntSegment
    {
        public IntPair Start;
        public IntPair Finish;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IntTriple
    {
        public long X;
        public long Y;
        public long Z;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ManagedStringBox
    {
        public string? Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeStringBox
    {
        public IntPtr Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WeakBox
    {
        public ulong Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Segment
    {
        public Point Start;
        public Point Finish;
    }

    private enum HostColor
    {
        Red,
        Green,
        Blue
    }

    private enum HostMode : byte
    {
        Draw = 74,
        Select,
        Remove = 8,
        Edit
    }

    private enum HostSignedTiny : sbyte
    {
        Low = -8,
        High = 12
    }

    private static void AssertEnumMember(UmkaEnumMemberInfo member, string name, long signedValue, ulong unsignedValue)
    {
        Assert.Equal(name, member.Name);
        Assert.Equal(signedValue, member.SignedValue);
        Assert.Equal(unsignedValue, member.UnsignedValue);
    }
}
