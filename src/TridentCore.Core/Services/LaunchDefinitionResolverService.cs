using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Launching;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Services;

public sealed class LaunchDefinitionResolverService(ILaunchComponentProvider provider)
{
    public async Task<LaunchResolution> ResolveAsync(Profile.Rice setup, LaunchDefinitionSnapshot snapshot, CancellationToken token)
    {
        var selections = snapshot.Definition?.Components ??
            [new() { From = LaunchSelection.Binding.Minecraft }, new() { From = LaunchSelection.Binding.Loader }];
        var components = new List<ResolvedLaunchComponent>();
        var explicitIds = new HashSet<string>(StringComparer.Ordinal);
        var disabled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selection in selections)
        {
            var bound = Bind(selection, setup);
            if (bound is null) continue;
            var (id, version) = bound.Value;
            if (!explicitIds.Add(id)) throw new FormatException($"Duplicate component selection '{id}'");
            if (!selection.Enabled)
            {
                disabled.Add(id);
                continue;
            }
            components.Add(await Resolve(id, version).ConfigureAwait(false));
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var state = HashHelper.ComputeObjectHash(components.Select(x => new { x.Definition.Id, x.Definition.Version }).ToArray());
            if (!visited.Add(state)) throw new FormatException("Component requirements cannot be resolved consistently");
            var changed = false;
            var requirements = components.SelectMany(x => x.Definition.Requires).GroupBy(x => x.Id).ToArray();
            foreach (var group in requirements)
            {
                var exact = group.Where(x => x.Version is not null).Select(x => x.Version!).Distinct(StringComparer.Ordinal).ToArray();
                if (exact.Length > 1) throw new FormatException($"Conflicting version requirements for component '{group.Key}': {string.Join(", ", exact)}");
                if (disabled.Contains(group.Key)) throw new FormatException($"Required component '{group.Key}' is disabled");
                var version = exact.FirstOrDefault() ?? group.Select(x => x.SuggestedVersion).FirstOrDefault(x => x is not null);
                var position = components.FindIndex(x => x.Definition.Id == group.Key);
                if (position >= 0)
                {
                    if (exact.Length == 0 || components[position].Definition.Version == exact[0]) continue;
                    if (explicitIds.Contains(group.Key))
                    {
                        throw new FormatException($"Component '{group.Key}' requires version '{exact[0]}', but '{components[position].Definition.Version}' is explicitly selected");
                    }
                    components[position] = await Resolve(group.Key, version).ConfigureAwait(false);
                }
                else
                {
                    var component = await Resolve(group.Key, version).ConfigureAwait(false);
                    var firstDependant = components.FindIndex(x => x.Definition.Requires.Any(y => y.Id == group.Key));
                    components.Insert(firstDependant, component);
                }
                changed = true;
            }
            if (!changed) break;
        }

        foreach (var entry in components)
        foreach (var conflict in entry.Definition.Conflicts)
        {
            if (components.Any(x => x.Definition.Id == conflict.Id
                                    && (conflict.Version is null || x.Definition.Version == conflict.Version)))
            {
                throw new FormatException($"Component '{entry.Definition.Id}' conflicts with '{conflict.Id}' ({entry.Source})");
            }
        }

        List<uint>? javaMajors = null;
        foreach (var entry in components.Where(x => x.Definition.JavaMajors is not null))
        {
            var majors = entry.Definition.JavaMajors!;
            javaMajors = javaMajors is null ? majors.Distinct().ToList()
                : javaMajors.Where(majors.Contains).ToList();
            if (javaMajors.Count == 0)
            {
                throw new FormatException($"Component '{entry.Definition.Id}' has incompatible Java requirements ({entry.Source})");
            }
        }
        if (javaMajors is null) throw new FormatException("No selected component declares compatible Java versions");
        return new([.. components], [.. javaMajors],
                    HashHelper.ComputeObjectHash(new { snapshot.Fingerprint, Components = components.ToArray() }));

        async Task<ResolvedLaunchComponent> Resolve(string id, string? version)
        {
            _ = LaunchDefinitionHelper.ComponentFile(id);
            ResolvedLaunchComponent result;
            if (snapshot.Components.TryGetValue(id, out var local))
            {
                result = snapshot.Resolve(local);
            }
            else
            {
                if (string.IsNullOrEmpty(version))
                {
                    throw new FormatException($"Component '{id}' has neither a local definition nor a resolved version");
                }
                var definition = await provider.ResolveAsync(id, version, setup.Version, token).ConfigureAwait(false);
                result = new(definition, $"platform:{id}@{version}");
            }
            LaunchDefinitionHelper.Validate(result.Definition, result.Source);
            if (result.Definition.Id != id || (version is not null && result.Definition.Version != version))
            {
                throw new FormatException($"Component '{id}' from '{result.Source}' does not match selected version '{version}'");
            }
            return result;
        }
    }

    public static (string Id, string? Version)? Bind(LaunchSelection selection, Profile.Rice setup)
    {
        if (selection.From != LaunchSelection.Binding.Component && (selection.Id is not null || selection.Version is not null))
        {
            throw new FormatException("Profile-bound component selections cannot override their identity or version");
        }
        switch (selection.From)
        {
            case LaunchSelection.Binding.Minecraft:
                return (PrismLauncherService.UID_MINECRAFT, setup.Version);
            case LaunchSelection.Binding.Loader:
                if (setup.Loader is null) return null;
                if (!LoaderHelper.TryParse(setup.Loader, out var loader)
                 || !PrismLauncherService.UidMappings.TryGetValue(loader.Identity, out var id))
                {
                    throw new FormatException($"Invalid loader '{setup.Loader}'");
                }
                return (id, loader.Version);
            case LaunchSelection.Binding.Component:
                return (selection.Id ?? throw new FormatException("Component selection has no identity"), selection.Version);
            default:
                throw new FormatException($"Unknown component binding '{selection.From}'");
        }
    }
}
