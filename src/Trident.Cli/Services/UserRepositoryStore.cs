using System.Text.Json;
using System.Text.Json.Serialization;
using Trident.Abstractions;
using Trident.Core.Services;

namespace Trident.Cli.Services;

public class UserRepositoryStore
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

    private readonly string _path = CliDataPaths.File("repositories.json");

    public IReadOnlyList<UserRepositoryProfile> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var repositories = JsonSerializer.Deserialize<List<UserRepositoryProfile>>(
            File.ReadAllText(_path),
            SerializerOptions
        );
        return repositories ?? [];
    }

    public void Save(IEnumerable<UserRepositoryProfile> repositories)
    {
        AtomicFileWriter.WriteAllText(_path, JsonSerializer.Serialize(repositories, SerializerOptions));
    }

    public void AddOrReplace(UserRepositoryProfile repository)
    {
        var repositories = Load()
            .Where(x => !string.Equals(x.Label, repository.Label, StringComparison.OrdinalIgnoreCase))
            .Append(repository)
            .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Save(repositories);
    }

    public bool Remove(string label)
    {
        var repositories = Load();
        var next = repositories
            .Where(x => !string.Equals(x.Label, label, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (next.Length == repositories.Count)
        {
            return false;
        }

        Save(next);
        return true;
    }

    public static IRepositoryProviderAccessor.ProviderProfile.DriverType ParseDriver(string driver) =>
        driver.ToLowerInvariant() switch
        {
            "curseforge" => IRepositoryProviderAccessor.ProviderProfile.DriverType.CurseForge,
            "modrinth" => IRepositoryProviderAccessor.ProviderProfile.DriverType.Modrinth,
            _ => throw new CliException(
                $"Repository driver '{driver}' is not supported.",
                ExitCodes.Usage
            ),
        };
}

public sealed record UserRepositoryProfile(
    string Label,
    string Driver,
    string Endpoint,
    string? ApiKey,
    string? UserAgent
);
