using System.Globalization;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using TridentCore.Abstractions.Extensions;
using TridentCore.Cli.Operations;
using TridentCore.Cli.Services;
using TridentCore.Core.Services;

namespace TridentCore.Cli.Commands.Config;

public abstract class ConfigCommandBase<T>(InstanceContextResolver resolver) : Command<T>
    where T : ConfigScopeArguments
{
    protected ConfigScope ResolveScope(T settings)
    {
        if (
            string.IsNullOrWhiteSpace(settings.Instance)
            && string.IsNullOrWhiteSpace(settings.Profile)
        )
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
        var result = ConfigOperation.Get(resolver, configuration, settings.Name, settings.Instance, settings.Profile);

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteKeyValueTable(
                "Configuration value",
                ("Scope", result.Scope),
                ("Name", settings.Name),
                ("Type", ConfigValueParser.Describe(result.Value)),
                ("Value", ConfigValueParser.Format(result.Value))
            );
        }

        return ExitCodes.SUCCESS;
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
        var result = ConfigOperation.Set(resolver, configuration, profileManager,
            settings.Name, settings.Value, settings.Type, settings.Instance, settings.Profile);

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
        }
        else
        {
            output.WriteKeyValueTable(
                "Configuration saved",
                ("Scope", result.Scope),
                ("Name", settings.Name),
                ("Type", ConfigValueParser.Describe(result.Value)),
                ("Value", ConfigValueParser.Format(result.Value))
            );
            output.WriteSuccess($"Configuration {settings.Name} saved.");
        }

        return ExitCodes.SUCCESS;
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
        ConfigOperation.Unset(resolver, configuration, profileManager, settings.Name, settings.Instance, settings.Profile);

        if (output.UseStructuredOutput)
        {
            output.WriteData(new { action = "config.unset", name = settings.Name });
        }
        else
        {
            output.WriteKeyValueTable(
                "Configuration removed",
                ("Name", settings.Name)
            );
            output.WriteSuccess($"Configuration {settings.Name} removed.");
        }

        return ExitCodes.SUCCESS;
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
        var result = ConfigOperation.List(resolver, configuration, settings.Instance, settings.Profile);

        if (output.UseStructuredOutput)
        {
            output.WriteData(result);
            return ExitCodes.SUCCESS;
        }

        if (result.Values.Count == 0)
        {
            output.WriteEmptyState(
                "No configuration values",
                "Set values with: trident config set --name <key> --value <value>"
            );
            return ExitCodes.SUCCESS;
        }

        var table = new Table().RoundedBorder();
        table.Title = new($"[bold]Configuration: {Markup.Escape(result.Scope)}[/]");
        table.AddColumn("Name");
        table.AddColumn("Type");
        table.AddColumn("Value");
        foreach (var item in result.Values)
        {
            table.AddRow(
                Markup.Escape(item.Name),
                Markup.Escape(ConfigValueParser.Describe(item.Value)),
                Markup.Escape(ConfigValueParser.Format(item.Value))
            );
        }

        output.WriteTable(table);
        return ExitCodes.SUCCESS;
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
                ExitCodes.USAGE
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
        if (bool.TryParse(value, out var boolean)) return boolean;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
        return value;
    }

    private static bool ParseBoolean(string value) =>
        bool.TryParse(value, out var result)
            ? result
            : throw new CliException($"'{value}' is not a valid bool value. Use true or false.", ExitCodes.USAGE);

    private static long ParseInteger(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new CliException($"'{value}' is not a valid integer value.", ExitCodes.USAGE);

    private static double ParseNumber(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new CliException($"'{value}' is not a valid number value.", ExitCodes.USAGE);
}
