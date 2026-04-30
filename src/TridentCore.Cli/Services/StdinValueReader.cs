using System.Text.Json;

namespace TridentCore.Cli.Services;

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

        try
        {
            using var document = JsonDocument.Parse(input);
            return ReadElement(document.RootElement).ToArray();
        }
        catch (JsonException ex)
        {
            throw new CliException($"stdin is not valid JSON: {ex.Message}", ExitCodes.Usage);
        }
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

                foreach (var property in new[] { "items", "packages", "package", "dependencies", "versions", "results", "account" })
                {
                    if (!element.TryGetProperty(property, out var nested))
                    {
                        continue;
                    }

                    foreach (var item in ReadElement(nested))
                    {
                        yield return item;
                    }
                }

                break;
        }
    }
}
