using System.Text.RegularExpressions;
using Trident.Core.Engines.Launching;

namespace Trident.Core.Utilities;

public static partial class ScrapHelper
{
    [GeneratedRegex(
        @"^\[(?:(?<date>.+?)\s+)?(?<time>\d{2}:\d{2}:\d{2}(?:\.\d{3})?)\] \[(?<thread>[^\]/]+)\/(?<level>[A-Z]+)\](?: \[(?<source>[^\]]+)\])?: (?<message>.*)$"
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

            string? parsedDate = null;
            string? parsedTime = null;
            if (match.Groups.TryGetValue("date", out var date) && !string.IsNullOrEmpty(date.Value))
            {
                parsedDate = date.Value;
            }
            if (
                match.Groups.TryGetValue("time", out var time)
                && !string.IsNullOrEmpty(time.Value)
            )
            {
                parsedTime = time.Value;
            }

            return new(
                message.Value,
                level.Value.ToUpper() switch
                {
                    "INFO" => ScrapLevel.Information,
                    "WARN" => ScrapLevel.Warning,
                    "ERROR" => ScrapLevel.Error,
                    _ => ScrapLevel.Information,
                },
                parsedDate,
                parsedTime,
                thread.Value,
                sender?.Value
            );
        }

        return new(data);
    }
}
