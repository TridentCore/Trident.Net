using System.Text;
using System.Text.Json;

namespace TridentCore.Text;

public static class MinecraftTextReader
{
    private static readonly Dictionary<char, MinecraftColor> LegacyColors = new()
    {
        ['0'] = new(0x00, 0x00, 0x00),
        ['1'] = new(0x00, 0x00, 0xAA),
        ['2'] = new(0x00, 0xAA, 0x00),
        ['3'] = new(0x00, 0xAA, 0xAA),
        ['4'] = new(0xAA, 0x00, 0x00),
        ['5'] = new(0xAA, 0x00, 0xAA),
        ['6'] = new(0xFF, 0xAA, 0x00),
        ['7'] = new(0xAA, 0xAA, 0xAA),
        ['8'] = new(0x55, 0x55, 0x55),
        ['9'] = new(0x55, 0x55, 0xFF),
        ['a'] = new(0x55, 0xFF, 0x55),
        ['b'] = new(0x55, 0xFF, 0xFF),
        ['c'] = new(0xFF, 0x55, 0x55),
        ['d'] = new(0xFF, 0x55, 0xFF),
        ['e'] = new(0xFF, 0xFF, 0x55),
        ['f'] = new(0xFF, 0xFF, 0xFF)
    };

    public static MinecraftText ParseLegacy(string? text) => ParseLegacy(text, MinecraftTextStyle.Default);

    // NOTE: baseStyle is the starting point — used when a JSON component feeds its
    // already-resolved color/style in as the base for the § codes inside its text.
    public static MinecraftText ParseLegacy(string? text, MinecraftTextStyle baseStyle)
    {
        if (string.IsNullOrEmpty(text))
        {
            return MinecraftText.Empty;
        }

        var runs = new List<MinecraftTextRun>();
        var buffer = new StringBuilder();
        var style = baseStyle;

        void Flush()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            runs.Add(new(buffer.ToString(), style.Resolve()));
            buffer.Clear();
        }

        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '§' && i + 1 < text.Length)
            {
                var code = char.ToLowerInvariant(text[i + 1]);
                if (TryNextLegacyStyle(code, style, out var next))
                {
                    Flush();
                    style = next;
                    i += 2;
                    continue;
                }
            }

            buffer.Append(text[i]);
            i++;
        }

        Flush();
        return new(runs);
    }

    public static MinecraftText ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return MinecraftText.Empty;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new MinecraftTextFormatException("Input is not valid JSON.", ex);
        }

        using (document)
        {
            var runs = new List<MinecraftTextRun>();
            WalkComponent(document.RootElement, MinecraftTextStyle.Default, runs);
            return new(runs);
        }
    }

    public static bool TryParse(string? raw, out MinecraftText result)
    {
        if (string.IsNullOrEmpty(raw))
        {
            result = MinecraftText.Empty;
            return true;
        }

        var index = 0;
        while (index < raw.Length && char.IsWhiteSpace(raw[index]))
        {
            index++;
        }

        if (index < raw.Length && (raw[index] == '{' || raw[index] == '['))
        {
            try
            {
                result = ParseJson(raw);
                return true;
            }
            catch (MinecraftTextFormatException)
            {
                result = MinecraftText.Empty;
                return false;
            }
        }

        result = ParseLegacy(raw);
        return true;
    }

    private static bool TryNextLegacyStyle(char code, MinecraftTextStyle current, out MinecraftTextStyle next)
    {
        // NOTE: A color code also clears every style flag — that is why §a must
        // precede §l in practice. §r clears both color and styles.
        if (code is >= '0' and <= '9' or >= 'a' and <= 'f')
        {
            next = new() { Color = LegacyColors[code] };
            return true;
        }

        switch (code)
        {
            case 'k':
                next = current with { Obfuscated = true };
                return true;
            case 'l':
                next = current with { Bold = true };
                return true;
            case 'm':
                next = current with { Strikethrough = true };
                return true;
            case 'n':
                next = current with { Underlined = true };
                return true;
            case 'o':
                next = current with { Italic = true };
                return true;
            case 'r':
                next = MinecraftTextStyle.Default;
                return true;
            default:
                next = current;
                return false;
        }
    }

    private static void WalkComponent(JsonElement element, MinecraftTextStyle inherited, List<MinecraftTextRun> runs)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                AppendFormatted(runs, element.GetString(), inherited);
                break;
            case JsonValueKind.Object:
                WalkObject(element, inherited, runs);
                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    WalkComponent(child, inherited, runs);
                }

                break;
        }
    }

    private static void WalkObject(JsonElement obj, MinecraftTextStyle inherited, List<MinecraftTextRun> runs)
    {
        var style = MergeStyle(obj, inherited);
        EmitContent(obj, style, runs);

        if (obj.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in extra.EnumerateArray())
            {
                WalkComponent(child, style, runs);
            }
        }
    }

    private static MinecraftTextStyle MergeStyle(JsonElement obj, MinecraftTextStyle inherited)
    {
        var style = inherited;

        if (obj.TryGetProperty("color", out var colorEl) && colorEl.ValueKind == JsonValueKind.String)
        {
            style = style with { Color = MinecraftColor.Parse(colorEl.GetString()) };
        }

        if (TryGetBool(obj, "bold", out var bold))
        {
            style = style with { Bold = bold };
        }

        if (TryGetBool(obj, "italic", out var italic))
        {
            style = style with { Italic = italic };
        }

        if (TryGetBool(obj, "underlined", out var underlined))
        {
            style = style with { Underlined = underlined };
        }

        if (TryGetBool(obj, "strikethrough", out var strikethrough))
        {
            style = style with { Strikethrough = strikethrough };
        }

        if (TryGetBool(obj, "obfuscated", out var obfuscated))
        {
            style = style with { Obfuscated = obfuscated };
        }

        return style;
    }

    private static bool TryGetBool(JsonElement obj, string name, out bool value)
    {
        value = false;
        if (!obj.TryGetProperty(name, out var el))
        {
            return false;
        }

        if (el.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        return el.ValueKind == JsonValueKind.False;
    }

    private static void EmitContent(JsonElement obj, MinecraftTextStyle style, List<MinecraftTextRun> runs)
    {
        if (obj.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
        {
            AppendFormatted(runs, textEl.GetString(), style);
            return;
        }

        // NOTE: translate/keybind/score/selector/nbt need client language files or
        // server context the launcher does not have, so degrade to the most
        // readable literal the component still offers.
        if (obj.TryGetProperty("fallback", out var fallbackEl) && fallbackEl.ValueKind == JsonValueKind.String)
        {
            AppendFormatted(runs, fallbackEl.GetString(), style);
            return;
        }

        if (obj.TryGetProperty("translate", out var translateEl) && translateEl.ValueKind == JsonValueKind.String)
        {
            Append(runs, translateEl.GetString(), style);
        }
        else if (obj.TryGetProperty("keybind", out var keybindEl) && keybindEl.ValueKind == JsonValueKind.String)
        {
            Append(runs, keybindEl.GetString(), style);
        }
        else if (obj.TryGetProperty("selector", out var selectorEl) && selectorEl.ValueKind == JsonValueKind.String)
        {
            Append(runs, selectorEl.GetString(), style);
        }
    }

    private static void Append(List<MinecraftTextRun> runs, string? text, MinecraftTextStyle style)
    {
        if (!string.IsNullOrEmpty(text))
        {
            runs.Add(new(text!, style.Resolve()));
        }
    }

    // NOTE: § codes are interpreted even inside JSON text/fallback values, using the
    // component's resolved style as the base — real-world packs routinely embed §
    // alongside JSON color/style and expect it to render, not show up literally.
    private static void AppendFormatted(List<MinecraftTextRun> runs, string? text, MinecraftTextStyle baseStyle)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (var run in ParseLegacy(text, baseStyle).Runs)
        {
            runs.Add(run);
        }
    }
}
