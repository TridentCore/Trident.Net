using System.Text.RegularExpressions;
using Trident.Core.Engines.Launching;

namespace Trident.Core.Utilities;

public static partial class ScrapHelper
{
    [GeneratedRegex(
        @"\[(.*)\] \[(?<thread>[a-zA-Z0-9\ \-#@]+)/(?<level>[a-zA-Z]+)\](\ \[(?<source>[a-zA-Z0-9\ \\./\-]+)\])?: (?<message>.*)"
    )]
    private static partial Regex GenerateRegex();

    private static Regex pattern = GenerateRegex();

    public static Scrap Parse(string data)
    {
        var match = pattern.Match(data);
        if (
            match.Success
            && match.Groups.TryGetValue("level", out var level)
            && match.Groups.TryGetValue("thread", out var thread)
            && match.Groups.TryGetValue("message", out var message)
        )
        {
            match.Groups.TryGetValue("source", out var sender);
            return new(
                message.Value,
                level.Value.ToUpper() switch
                {
                    "INFO" => ScrapLevel.Information,
                    "WARN" => ScrapLevel.Warning,
                    "ERROR" => ScrapLevel.Error,
                    _ => ScrapLevel.Information,
                },
                DateTimeOffset.Now,
                thread.Value,
                sender?.Value
            );
        }

        return new(data);
    }
}
