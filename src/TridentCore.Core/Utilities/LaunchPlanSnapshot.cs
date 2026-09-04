using System.Text.Json;
using TridentCore.Abstractions;
using TridentCore.Abstractions.LaunchPlans;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Utilities;

public sealed record LaunchPlanSnapshot(LaunchPlanDocument Document, string Hash)
{
    public static LaunchPlanSnapshot? LoadOrNull(string key)
    {
        var path = PathDef.Default.FileOfLaunchPlan(key);
        if (!File.Exists(path))
        {
            return null;
        }

        var content = File.ReadAllText(path);
        LaunchPlanDocument document;
        try
        {
            document = JsonSerializer.Deserialize<LaunchPlanDocument>(content, JsonSerializerOptions.Web)
                    ?? throw new FormatException("Launch plan document is empty");
        }
        catch (JsonException e)
        {
            throw new FormatException("External launch plan is not valid JSON", e);
        }

        try
        {
            _ = new LaunchPlan().Apply(document);
        }
        catch (FormatException e)
        {
            throw new FormatException("External launch plan contains an invalid operation", e);
        }

        return new(document, HashHelper.ComputeObjectHash(document));
    }
}
