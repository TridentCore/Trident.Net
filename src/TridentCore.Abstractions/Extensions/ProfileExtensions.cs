using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using TridentCore.Abstractions.FileModels;

namespace TridentCore.Abstractions.Extensions;

public static class ProfileExtensions
{
    public static Profile.Rice Clone(this Profile.Rice self)
    {
        var rules = new List<Profile.Rice.Rule>(
            self.Rules.Select(x => new Profile.Rice.Rule()
            {
                Enabled = x.Enabled,
                Selector = x.Selector,
                Destination = x.Destination,
                Skipping = x.Skipping,
                Normalizing = x.Normalizing,
            })
        );
        var packages = new List<Profile.Rice.Entry>(
            self.Packages.Select(x => new Profile.Rice.Entry
            {
                Enabled = x.Enabled,
                Purl = x.Purl,
                Source = x.Source,
                Tags = x.Tags,
            })
        );
        return new()
        {
            Version = self.Version,
            Loader = self.Loader,
            Source = self.Source,
            Packages = packages,
            Rules = self.Rules,
        };
    }

    #region Nested type: $extension

    extension(Profile profile)
    {
        public bool TryGetOverride<T>(
            string key,
            [MaybeNullWhen(false)] out T value,
            T? defaultValue = default
        )
        {
            if (profile.Overrides.TryGetValue(key, out var result) && result is T rv)
            {
                value = rv;
                return true;
            }

            if (result is not null && TryConvertNumericOverride(result, out T converted))
            {
                value = converted;
                return true;
            }

            value = defaultValue;
            return false;
        }

        public T? GetOverride<T>(string key, T? defaultValue = default) =>
            profile.TryGetOverride<T>(key, out var result) ? result : defaultValue;

        public void SetOverride<T>(string key, T? value)
        {
            if (value is null or "")
            {
                profile.Overrides.Remove(key);
            }
            else
            {
                profile.Overrides[key] = value;
            }
        }

        public void RemoveOverride(string key) => profile.Overrides.Remove(key);

        public Profile Clone()
        {
            var overrides = new Dictionary<string, object>(profile.Overrides);

            return new()
            {
                Name = profile.Name,
                Setup = profile.Setup.Clone(),
                Overrides = overrides,
            };
        }
    }

    #endregion

    private static bool TryConvertNumericOverride<T>(object result, out T value)
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

    private static bool IsNumericType(Type type) =>
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
}
