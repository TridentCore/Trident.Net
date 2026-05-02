using System.Globalization;
using System.Text.Json;

namespace TridentCore.Abstractions.Utilities;

public static class NumericConversionHelper
{
    public static bool TryConvert<T>(object result, out T value)
    {
        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (!IsNumericType(targetType))
        {
            value = default!;
            return false;
        }

        if (result is JsonElement element)
        {
            return TryConvertJsonNumber(element, targetType, out value);
        }

        if (!IsNumericType(result.GetType()))
        {
            value = default!;
            return false;
        }

        try
        {
            var converted = Convert.ChangeType(result, targetType, CultureInfo.InvariantCulture);
            if (converted is T typed)
            {
                value = typed;
                return true;
            }
        }
        catch (Exception ex) when (ex is InvalidCastException or OverflowException or FormatException)
        {
        }

        value = default!;
        return false;
    }

    public static bool IsNumericType(Type type) =>
        Type.GetTypeCode(type) switch
        {
            TypeCode.Byte
            or TypeCode.SByte
            or TypeCode.Int16
            or TypeCode.UInt16
            or TypeCode.Int32
            or TypeCode.UInt32
            or TypeCode.Int64
            or TypeCode.UInt64
            or TypeCode.Single
            or TypeCode.Double
            or TypeCode.Decimal => true,
            _ => false,
        };

    private static bool TryConvertJsonNumber<T>(JsonElement element, Type targetType, out T value)
    {
        if (element.ValueKind != JsonValueKind.Number)
        {
            value = default!;
            return false;
        }

        try
        {
            object? converted = Type.GetTypeCode(targetType) switch
            {
                TypeCode.Byte => element.GetByte(),
                TypeCode.SByte => element.GetSByte(),
                TypeCode.Int16 => element.GetInt16(),
                TypeCode.UInt16 => element.GetUInt16(),
                TypeCode.Int32 => element.GetInt32(),
                TypeCode.UInt32 => element.GetUInt32(),
                TypeCode.Int64 => element.GetInt64(),
                TypeCode.UInt64 => element.GetUInt64(),
                TypeCode.Single => element.GetSingle(),
                TypeCode.Double => element.GetDouble(),
                TypeCode.Decimal => element.GetDecimal(),
                _ => null,
            };

            if (converted is T typed)
            {
                value = typed;
                return true;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException)
        {
        }

        value = default!;
        return false;
    }
}
