using System.Text.Json.Nodes;
using TridentCore.Abstractions.FileModels;

namespace TridentCore.Core.Utilities;

public static class PatchFormatHelper
{
    // Format 2 readers also accepted legacy Java fields; 2 -> 3 canonicalizes those files before typed loading.
    public static readonly JsonMigrationHelper.Format<PatchDocument> Document = new(
        PatchDocument.FORMAT,
        EstablishBaseline,
        MigrateJavaMajors,
        MigrateJavaMajors);

    public static readonly JsonMigrationHelper.Format<PatchIndex> Index = new(PatchIndex.FORMAT, EstablishBaseline);
    public static readonly JsonMigrationHelper.Format<PackPatchIndex> PackIndex = new(PackPatchIndex.FORMAT, EstablishBaseline);

    private static void EstablishBaseline(JsonObject document) { }

    private static void MigrateJavaMajors(JsonObject document)
    {
        if (PropertyName(document, "operations") is not { } operationsKey || document[operationsKey] is not JsonArray operations)
        {
            return;
        }
        foreach (var operation in operations.OfType<JsonObject>())
        {
            if (PropertyName(operation, "target") is not { } targetKey
             || operation[targetKey] is not JsonValue targetValue || !targetValue.TryGetValue<string>(out var target))
            {
                continue;
            }
            var valueKey = PropertyName(operation, "value");
            if (target.EndsWith(".javaMajor", StringComparison.Ordinal))
            {
                operation[targetKey] = target[..^"javaMajor".Length] + "compatibleJavaMajors";
                if (valueKey is not null && operation[valueKey] is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.Number)
                {
                    operation[valueKey] = new JsonArray(value.DeepClone());
                }
            }
            else if (target is "vanilla" or "loader" or "launch"
                  && valueKey is not null && operation[valueKey] is JsonObject artifact)
            {
                MigrateArtifactJavaMajor(artifact);
            }
        }
    }

    private static void MigrateArtifactJavaMajor(JsonObject artifact)
    {
        if (PropertyName(artifact, "javaMajor") is not { } legacyKey)
        {
            return;
        }
        if (PropertyName(artifact, "compatibleJavaMajors") is null)
        {
            artifact["compatibleJavaMajors"] = artifact[legacyKey] is { } major ? new JsonArray(major.DeepClone()) : new JsonArray();
        }
        foreach (var key in artifact.Select(x => x.Key).Where(x => string.Equals(x, "javaMajor", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            artifact.Remove(key);
        }
    }

    private static string? PropertyName(JsonObject document, string name) =>
        document.Select(x => x.Key).LastOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
}
