using System.Text.Json.Nodes;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Pref;
using TridentCore.Pref.Building;
using TridentCore.Pref.Utilities;

namespace TridentCore.Core.Utilities;

public static class FileFormatHelper
{
    public static readonly JsonMigrationHelper.Format<Profile> Profile =
        new(TridentCore.Abstractions.FileModels.Profile.FORMAT, UpgradeProfileFrom0);

    public static readonly JsonMigrationHelper.Format<PackData> PackData =
        new(TridentCore.Abstractions.FileModels.PackData.FORMAT, _ => { });

    private static void UpgradeProfileFrom0(JsonObject document)
    {
        if (Get(document, "setup") is JsonObject setup)
        {
            MigrateProfileSetup(setup);
        }
    }

    // Snapshot databases store the setup alone; their whole-database migration uses the same conversion.
    public static void MigrateProfileSetup(JsonObject setup)
    {
        MigrateSource(setup);
        if (Get(setup, "packages") is JsonArray packages)
        {
            foreach (var entry in packages.OfType<JsonObject>())
            {
                MigratePref(entry);
                MigrateSource(entry);
            }
        }
        if (Get(setup, "rules") is JsonArray rules)
        {
            foreach (var rule in rules.OfType<JsonObject>())
            {
                if (Get(rule, "selector") is JsonObject selector)
                {
                    MigrateSelector(selector);
                }
            }
        }
        if (Get(setup, "sourceOrders") is JsonArray sources)
        {
            for (var i = 0; i < sources.Count; i++)
            {
                var source = ConvertSource(sources[i]?.GetValue<string>());
                if (source is null)
                {
                    sources.RemoveAt(i--);
                }
                else
                {
                    sources[i] = source;
                }
            }
        }
    }

    private static void MigrateSelector(JsonObject selector)
    {
        MigratePref(selector);
        var typeKey = Key(selector, "type");
        if (typeKey is not null && selector[typeKey] is JsonValue value
            && value.TryGetValue<string>(out var type) && type.Equals("purl", StringComparison.OrdinalIgnoreCase))
        {
            selector[typeKey] = "pref";
        }
        if (Get(selector, "children") is JsonArray children)
        {
            foreach (var child in children.OfType<JsonObject>())
            {
                MigrateSelector(child);
            }
        }
    }

    private static void MigratePref(JsonObject document)
    {
        var oldKey = Key(document, "purl");
        if (oldKey is null)
        {
            return;
        }
        if (Key(document, "pref") is null)
        {
            var value = document[oldKey]?.GetValue<string>();
            var migrated = string.IsNullOrEmpty(value) ? string.Empty : value;
            if (!string.IsNullOrEmpty(value) && PackageHelper.TryParseDescriptor(value, out var parsed))
            {
                try
                {
                    migrated = parsed.Build();
                }
                catch (ArgumentException) { }
            }
            document[char.IsUpper(oldKey[0]) ? "Pref" : "pref"] = migrated;
        }
        document.Remove(oldKey);
    }

    private static void MigrateSource(JsonObject document)
    {
        var key = Key(document, "source");
        if (key is not null)
        {
            document[key] = ConvertSource(document[key]?.GetValue<string>());
        }
    }

    private static string? ConvertSource(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return null;
        }
        return (InternalUriHelper.HasScheme(source, Builder.Scheme) || !source.Contains("://", StringComparison.Ordinal))
               && PackageHelper.TryParseDescriptor(source, out var pref)
                   ? ModpackSourceHelper.FromPref(pref.Build())
                   : source;
    }

    private static JsonNode? Get(JsonObject document, string name) =>
        Key(document, name) is { } key ? document[key] : null;

    private static string? Key(JsonObject document, string name) =>
        document.Select(x => x.Key).FirstOrDefault(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
}
