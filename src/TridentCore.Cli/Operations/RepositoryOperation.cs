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
}

public sealed record RepositoryStatusItem(
    string Label,
    IReadOnlyList<string> SupportedLoaders,
    int VersionCount,
    IReadOnlyList<TridentCore.Abstractions.Repositories.Resources.ResourceKind> SupportedKinds
);
