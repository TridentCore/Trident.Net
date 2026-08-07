using System.Collections.Generic;
using System.Globalization;

namespace TridentCore.Text;

public readonly record struct MinecraftColor(byte R, byte G, byte B)
{
    private static readonly Dictionary<string, MinecraftColor> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = new(0x00, 0x00, 0x00),
        ["dark_blue"] = new(0x00, 0x00, 0xAA),
        ["dark_green"] = new(0x00, 0xAA, 0x00),
        ["dark_aqua"] = new(0x00, 0xAA, 0xAA),
        ["dark_red"] = new(0xAA, 0x00, 0x00),
        ["dark_purple"] = new(0xAA, 0x00, 0xAA),
        ["gold"] = new(0xFF, 0xAA, 0x00),
        ["gray"] = new(0xAA, 0xAA, 0xAA),
        ["dark_gray"] = new(0x55, 0x55, 0x55),
        ["blue"] = new(0x55, 0x55, 0xFF),
        ["green"] = new(0x55, 0xFF, 0x55),
        ["aqua"] = new(0x55, 0xFF, 0xFF),
        ["red"] = new(0xFF, 0x55, 0x55),
        ["light_purple"] = new(0xFF, 0x55, 0xFF),
        ["yellow"] = new(0xFF, 0xFF, 0x55),
        ["white"] = new(0xFF, 0xFF, 0xFF)
    };

    public static IReadOnlyDictionary<string, MinecraftColor> Names => Named;

    public static MinecraftColor? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var span = value.AsSpan().Trim();
        if (span.Length != 0 && span[0] == '#')
        {
            var hex = span[1..];
            return hex.Length == 6
                   && byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
                   && byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
                   && byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
                ? new MinecraftColor(r, g, b)
                : null;
        }

        return Named.TryGetValue(span.ToString(), out var named) ? named : null;
    }
}
