using System.Text.Json;

namespace Trident.Cli.Services;

public class StdinValueReader(CliContext context)
{
    public IReadOnlyList<string> ReadValuesIfRedirected()
    {
        if (!context.InputRedirected)
        {
            return [];
        }

        var input = Console.In.ReadToEnd();
        return ParseValues(input);
    }

    public static IReadOnlyList<string> ParseValues(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        input = input.Trim();
        if (!input.StartsWith('{') && !input.StartsWith('[') && !input.StartsWith('"'))
        {
            return input
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
        }

        using var document = JsonDocument.Parse(input);
        return ReadElement(document.RootElement).ToArray();
    }

    private static IEnumerable<string> ReadElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString()!;
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray().SelectMany(ReadElement))
                {
                    yield return item;
                }

                break;
            case JsonValueKind.Object:
                if (element.TryGetProperty("purl", out var purl) && purl.ValueKind == JsonValueKind.String)
                {
                    yield return purl.GetString()!;
                }
                else if (element.TryGetProperty("items", out var items))
                {
                    foreach (var item in ReadElement(items))
                    {
                        yield return item;
                    }
                }

                break;
        }
    }
}
