using System.Text.Json;
using System.Text.Json.Serialization;
using TridentCore.Abstractions.Launching;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Core.Utilities;

public static class LaunchDefinitionHelper
{
    public const string IMPORT_DIRECTORY = "import";
    public const string USER_DIRECTORY = "user";
    public const string COMPONENT_DIRECTORY = "components";
    public const string DEFINITION_FILE = "definition.json";
    public const string IMPORT_PREFIX = "import/";
    public const string PACK_IMPORT_PREFIX = "launch/import/";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

    public static T Deserialize<T>(string text, string source) =>
        JsonSerializer.Deserialize<T>(text, JsonOptions)
        ?? throw new FormatException($"Launch definition '{source}' is empty");

    public static string ComponentFile(string id)
    {
        if (string.IsNullOrEmpty(id) || id is "." or ".."
         || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')))
        {
            throw new FormatException($"Invalid component identity '{id}'");
        }
        return $"{COMPONENT_DIRECTORY}/{id}.json";
    }

    public static void Validate(LaunchComponent component, string source)
    {
        if (component.FormatVersion != 1)
        {
            throw new FormatException($"Component '{source}' has unsupported format version {component.FormatVersion}");
        }
        _ = ComponentFile(component.Id);
        if (component.Requires is null || component.Conflicts is null || component.Libraries is null
         || component.GameArguments?.Append is null || component.JavaArguments?.Append is null)
        {
            throw new FormatException($"Component '{component.Id}' contains null declaration collections");
        }
        if (component.JavaMajors is { } majors && (majors.Count == 0 || majors.Contains(0u)))
        {
            throw new FormatException($"Component '{component.Id}' has invalid Java requirements");
        }
        foreach (var library in component.Libraries)
        {
            if (library?.Artifact?.Id is null || library.Artifact.Url is null
             || library.Rules is null || library.ExtractExcludes is null || !Enum.IsDefined(library.Use))
            {
                throw new FormatException($"Component '{component.Id}' contains an invalid artifact binding");
            }
            ArtifactHelper.Validate(library.Artifact.Id);
        }
    }
}
