using TridentCore.Cli.Commands.Repository;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Operations;

internal static class RepositoryOperation
{
    public static IReadOnlyList<RepositoryProfileDto> List(
        UserRepositoryStore userRepositories,
        CliRepositoryProviderAccessor combined)
    {
        var userLabels = userRepositories
            .Load()
            .Select(x => x.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return combined
            .Build()
            .Select(x => RepositoryDtos.FromProvider(x, userLabels.Contains(x.Label)))
            .ToArray();
    }

    public static async Task<IReadOnlyList<RepositoryStatusItem>> Status(
        RepositoryAgent repositories,
        string? label)
    {
        var labels = label is not null ? [label] : repositories.Labels.ToArray();
        var results = new List<RepositoryStatusItem>();

        foreach (var l in labels)
        {
            var status = await repositories.CheckStatusAsync(l).ConfigureAwait(false);
            results.Add(new(l, status.SupportedLoaders, status.SupportedVersions.Count, status.SupportedKinds));
        }

        return results;
    }

    public static RepositoryAddResult Add(
        UserRepositoryStore userRepositories,
        string label,
        string? driver,
        string endpoint,
        string? apiKey,
        string? userAgent)
    {
        var resolvedDriver = driver ?? label;
        UserRepositoryStore.ParseDriver(resolvedDriver);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
        {
            throw new CliException("--endpoint must be an absolute URI.", ExitCodes.USAGE);
        }

        var repository = new UserRepositoryProfile(label, resolvedDriver, endpoint, apiKey, userAgent);
        userRepositories.AddOrReplace(repository);
        return new(
            repository.Label,
            repository.Driver,
            repository.Endpoint,
            !string.IsNullOrWhiteSpace(repository.ApiKey),
            repository.UserAgent
        );
    }

    public static RepositoryRemoveResult Remove(
        UserRepositoryStore userRepositories,
        string label)
    {
        if (!userRepositories.Remove(label))
        {
            throw new CliException($"User repository '{label}' was not found.", ExitCodes.NOT_FOUND);
        }

        return new(label);
    }
}

public sealed record RepositoryStatusItem(
    string Label,
    IReadOnlyList<string> SupportedLoaders,
    int VersionCount,
    IReadOnlyList<TridentCore.Abstractions.Repositories.Resources.ResourceKind> SupportedKinds
);

internal sealed record RepositoryAddResult(
    string Label,
    string Driver,
    string Endpoint,
    bool HasAuthorization,
    string? UserAgent
);

internal sealed record RepositoryRemoveResult(string Label);
