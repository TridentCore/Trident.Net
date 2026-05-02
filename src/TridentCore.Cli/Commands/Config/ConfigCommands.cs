using System.Globalization;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Abstractions.Extensions;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Config;

public abstract class ConfigCommandBase<T>(InstanceContextResolver resolver) : Command<T>
    where T : ConfigScopeArguments
{
    protected ConfigScope ResolveScope(T settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Instance) && string.IsNullOrWhiteSpace(settings.Profile))
        {
            return ConfigScope.Global;
        }

        return ConfigScope.ForInstance(resolver.Resolve(settings.Instance, settings.Profile));
    }
}

public class ConfigGetCommand(
    InstanceContextResolver resolver,
    CliConfigurationStore configuration,
    CliOutput output
) : ConfigCommandBase<ConfigGetCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var scope = ResolveScope(settings);
        IReadOnlyDictionary<string, object> values = scope.IsGlobal
            ? configuration.Load()
            : scope.Instance!.Profile.Overrides.ToDictionary();
        if (!values.TryGetValue(settings.Name, out var value))
        {
            throw new CliException(
                $"Configuration '{settings.Name}' was not found in {scope.DisplayName}.",
                ExitCodes.NotFound
            );
        }

        var result = ConfigResult.From(scope, settings.Name, value);
        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteKeyValueTable(
                "Configuration value",
                ("Scope", scope.DisplayName),
                ("Name", settings.Name),
                ("Type", ConfigValueParser.Describe(value)),
                ("Value", ConfigValueParser.Format(value))
            );
        }

        return ExitCodes.Success;
    }

    public class Arguments : ConfigScopeArguments
    {
        [CommandOption("--name <KEY>", true)]
        public required string Name { get; set; }
    }
}

public class ConfigSetCommand(
    InstanceContextResolver resolver,
    ProfileManager profileManager,
    CliConfigurationStore configuration,
    CliOutput output
) : ConfigCommandBase<ConfigSetCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var value = ConfigValueParser.Parse(settings.Value, settings.Type);
        var scope = ResolveScope(settings);
        if (scope.IsGlobal)
        {
            configuration.Set(settings.Name, value);
        }
        else
        {
            var guard = profileManager.GetMutable(scope.Instance!.Key);
            guard.Value.SetOverride(settings.Name, value);
            guard.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        var result = ConfigResult.From(scope, settings.Name, value, "config.set");
        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteKeyValueTable(
                "Configuration saved",
                ("Scope", scope.DisplayName),
                ("Name", settings.Name),
                ("Type", ConfigValueParser.Describe(value)),
                ("Value", ConfigValueParser.Format(value))
            );
            output.WriteSuccess($"Configuration {settings.Name} saved in {scope.DisplayName}.");
        }

        return ExitCodes.Success;
    }

    public class Arguments : ConfigScopeArguments
    {
        [CommandOption("--name <KEY>", true)]
        public required string Name { get; set; }

        [CommandOption("--value <VALUE>", true)]
        public required string Value { get; set; }

        [CommandOption("--type <TYPE>")]
        public string? Type { get; set; }
    }
}

public class ConfigUnsetCommand(
    InstanceContextResolver resolver,
    ProfileManager profileManager,
    CliConfigurationStore configuration,
    CliOutput output
) : ConfigCommandBase<ConfigUnsetCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var scope = ResolveScope(settings);
        var removed = false;
        if (scope.IsGlobal)
        {
            removed = configuration.Remove(settings.Name);
        }
        else
        {
            var guard = profileManager.GetMutable(scope.Instance!.Key);
            removed = guard.Value.Overrides.Remove(settings.Name);
            guard.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (!removed)
        {
            throw new CliException(
                $"Configuration '{settings.Name}' was not found in {scope.DisplayName}.",
                ExitCodes.NotFound
            );
        }

        var result = new
        {
            action = "config.unset",
            scope = scope.ScopeName,
            key = scope.Instance?.Key,
            name = settings.Name,
        };
        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteKeyValueTable(
                "Configuration removed",
                ("Scope", scope.DisplayName),
                ("Name", settings.Name)
            );
            output.WriteSuccess($"Configuration {settings.Name} removed from {scope.DisplayName}.");
        }

        return ExitCodes.Success;
    }

    public class Arguments : ConfigScopeArguments
    {
        [CommandOption("--name <KEY>", true)]
        public required string Name { get; set; }
    }
}

public class ConfigListCommand(
    InstanceContextResolver resolver,
    CliConfigurationStore configuration,
    CliOutput output
) : ConfigCommandBase<ConfigListCommand.Arguments>(resolver)
{
    protected override int Execute(
        CommandContext context,
        Arguments settings,
        CancellationToken cancellationToken
    )
    {
        var scope = ResolveScope(settings);
        IReadOnlyDictionary<string, object> values = scope.IsGlobal
            ? configuration.Load()
            : scope.Instance!.Profile.Overrides.ToDictionary();
        var items = values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToArray();

        if (output.UseStructuredOutput)
        {
            output.WriteData(
                new
                {
                    scope = scope.ScopeName,
                    key = scope.Instance?.Key,
                    values = items.Select(x => ConfigResult.From(scope, x.Key, x.Value)).ToArray(),
                }
            );
            return ExitCodes.Success;
        }

        if (items.Length == 0)
        {
            output.WriteEmptyState(
                "No configuration values",
                $"Set values with: trident config set --name <key> --value <value>{scope.InstanceSuffix}"
            );
            return ExitCodes.Success;
        }

        var table = new Table().RoundedBorder();
        table.Title = new($"[bold]Configuration: {Markup.Escape(scope.DisplayName)}[/]");
        table.AddColumn("Name");
        table.AddColumn("Type");
        table.AddColumn("Value");
        foreach (var item in items)
        {
            table.AddRow(
                Markup.Escape(item.Key),
                Markup.Escape(ConfigValueParser.Describe(item.Value)),
                Markup.Escape(ConfigValueParser.Format(item.Value))
            );
        }

        output.WriteTable(table);
        return ExitCodes.Success;
    }

    public class Arguments : ConfigScopeArguments { }
}

public abstract class ConfigScopeArguments : CommandSettings
{
    [CommandOption("--profile <PATH>")]
    public string? Profile { get; set; }

    [CommandOption("-I|--instance <KEY>")]
    public string? Instance { get; set; }
}

public sealed record ConfigScope(ResolvedInstanceContext? Instance)
{
    public static ConfigScope Global { get; } = new((ResolvedInstanceContext?)null);
    public bool IsGlobal => Instance is null;
    public string ScopeName => IsGlobal ? "global" : "instance";
    public string DisplayName => IsGlobal ? "global" : $"instance {Instance!.Key}";
    public string InstanceSuffix => IsGlobal ? string.Empty : $" --instance {Instance!.Key}";

    public static ConfigScope ForInstance(ResolvedInstanceContext instance) => new(instance);
}

public sealed record ConfigResult(
    string? Action,
    string Scope,
    string? Key,
    string Name,
    string Type,
    object? Value
)
{
    public static ConfigResult From(
        ConfigScope scope,
        string name,
        object? value,
        string? action = null
    ) =>
        new(
            action,
            scope.ScopeName,
            scope.Instance?.Key,
            name,
            ConfigValueParser.Describe(value),
            value
        );
}

public static class ConfigValueParser
{
    public static object Parse(string value, string? type)
    {
        var normalizedType = string.IsNullOrWhiteSpace(type)
            ? "auto"
            : type.Trim().ToLowerInvariant();
        return normalizedType switch
        {
            "auto" => ParseAuto(value),
            "string" => value,
            "str" => value,
            "bool" or "boolean" => ParseBoolean(value),
            "int" or "integer" => ParseInteger(value),
            "number" or "float" or "double" => ParseNumber(value),
            _ => throw new CliException(
                $"Configuration value type '{type}' is not supported. Use auto, string, bool, integer, or number.",
                ExitCodes.Usage
            ),
        };
    }

    public static string Describe(object? value) =>
        value switch
        {
            null => "null",
            bool => "bool",
            byte or sbyte or short or ushort or int or uint or long or ulong => "integer",
            float or double or decimal => "number",
            JsonElement element => element.ValueKind.ToString().ToLowerInvariant(),
            _ => "string",
        };

    public static string Format(object? value) =>
        value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            JsonElement element => element.ToString(),
            _ => value.ToString() ?? string.Empty,
        };

    private static object ParseAuto(string value)
    {
        if (bool.TryParse(value, out var boolean))
        {
            return boolean;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return value;
    }

    private static bool ParseBoolean(string value) =>
        bool.TryParse(value, out var result)
            ? result
            : throw new CliException(
                $"'{value}' is not a valid bool value. Use true or false.",
                ExitCodes.Usage
            );

    private static long ParseInteger(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new CliException(
                $"'{value}' is not a valid integer value.",
                ExitCodes.Usage
            );

    private static double ParseNumber(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new CliException($"'{value}' is not a valid number value.", ExitCodes.Usage);
}
