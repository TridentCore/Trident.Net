using TridentCore.Abstractions.Extensions;
using TridentCore.Cli.Commands.Config;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Operations;

internal static class ConfigOperation
{
    public static ConfigResult Get(
        InstanceContextResolver resolver,
        CliConfigurationStore configuration,
        string name,
        string? instance,
        string? profile)
    {
        var scope = ResolveScope(resolver, instance, profile);
        var values = scope.IsGlobal
            ? configuration.Load()
            : scope.Instance!.Profile.Overrides.ToDictionary();

        if (!values.TryGetValue(name, out var value))
        {
            throw new CliException($"Configuration '{name}' was not found in {scope.DisplayName}.", ExitCodes.NOT_FOUND);
        }

        return ConfigResult.From(scope, name, value);
    }

    public static ConfigResult Set(
        InstanceContextResolver resolver,
        CliConfigurationStore configuration,
        ProfileManager profileManager,
        string name,
        string value,
        string? type,
        string? instance,
        string? profile)
    {
        var parsed = ConfigValueParser.Parse(value, type);
        var scope = ResolveScope(resolver, instance, profile);
        if (scope.IsGlobal)
        {
            configuration.Set(name, parsed);
        }
        else
        {
            var guard = profileManager.GetMutable(scope.Instance!.Key);
            guard.Value.SetOverride(name, parsed);
            guard.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        return ConfigResult.From(scope, name, parsed, "config.set");
    }

    public static void Unset(
        InstanceContextResolver resolver,
        CliConfigurationStore configuration,
        ProfileManager profileManager,
        string name,
        string? instance,
        string? profile)
    {
        var scope = ResolveScope(resolver, instance, profile);
        bool removed;
        if (scope.IsGlobal)
        {
            removed = configuration.Remove(name);
        }
        else
        {
            var guard = profileManager.GetMutable(scope.Instance!.Key);
            removed = guard.Value.Overrides.Remove(name);
            guard.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (!removed)
        {
            throw new CliException($"Configuration '{name}' was not found in {scope.DisplayName}.", ExitCodes.NOT_FOUND);
        }
    }

    public static ConfigListResult List(
        InstanceContextResolver resolver,
        CliConfigurationStore configuration,
        string? instance,
        string? profile)
    {
        var scope = ResolveScope(resolver, instance, profile);
        var values = scope.IsGlobal
            ? configuration.Load()
            : scope.Instance!.Profile.Overrides.ToDictionary();

        var items = values
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => ConfigResult.From(scope, x.Key, x.Value))
            .ToArray();

        return new(scope.ScopeName, scope.Instance?.Key, items);
    }

    private static ConfigScope ResolveScope(InstanceContextResolver resolver, string? instance, string? profile)
    {
        if (string.IsNullOrWhiteSpace(instance) && string.IsNullOrWhiteSpace(profile))
        {
            return ConfigScope.Global;
        }

        return ConfigScope.ForInstance(resolver.Resolve(instance, profile));
    }
}

public sealed record ConfigListResult(string Scope, string? Key, IReadOnlyList<ConfigResult> Values);
