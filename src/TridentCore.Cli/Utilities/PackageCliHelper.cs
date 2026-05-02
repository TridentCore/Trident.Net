using Spectre.Console;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;
using TridentCore.Cli.Services;
using Profile = TridentCore.Abstractions.FileModels.Profile;

namespace TridentCore.Cli.Utilities;

public static class PackageCliHelper
{
    public static (string Label, string? Namespace, string Pid, string? Vid) ParsePurl(
        string purl
    ) =>
        PackageHelper.TryParse(purl, out var parsed)
            ? parsed
            : throw new CliException($"'{purl}' is not a valid package purl.", ExitCodes.Usage);

    public static Filter BuildFilter(
        string? gameVersion,
        string? loader,
        ResourceKind? kind,
        ResolvedInstanceContext? instance = null
    )
    {
        gameVersion ??= instance?.Profile.Setup.Version;
        if (loader is null && instance?.Profile.Setup.Loader is { } lurl)
        {
            loader = LoaderHelper.TryParse(lurl, out var parsed) ? parsed.Identity : null;
        }

        return new(gameVersion, loader, kind);
    }

    public static ResourceKind? ParseKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return null;
        }

        return Enum.TryParse<ResourceKind>(kind, true, out var parsed)
            ? parsed
            : throw new CliException($"Package kind '{kind}' is not supported.", ExitCodes.Usage);
    }

    public static Profile.Rice.Entry FindEntry(Profile profile, string purl)
    {
        var parsed = ParsePurl(purl);
        return profile.Setup.Packages.FirstOrDefault(x =>
                PackageHelper.IsMatched(x.Purl, parsed.Label, parsed.Namespace, parsed.Pid)
            ) ?? throw new CliException($"Package '{purl}' is not installed.", ExitCodes.NotFound);
    }

    public static bool ContainsProject(Profile profile, string purl)
    {
        var parsed = ParsePurl(purl);
        return profile.Setup.Packages.Any(x =>
            PackageHelper.IsMatched(x.Purl, parsed.Label, parsed.Namespace, parsed.Pid)
        );
    }

    public static string ToPurl(Dependency dependency) =>
        PackageHelper.ToPurl(
            dependency.Label,
            dependency.Namespace,
            dependency.ProjectId,
            dependency.VersionId
        );

    public static Table CreatePackageTable(string title, IEnumerable<IPackageTableRow> packages)
    {
        var table = new Table().RoundedBorder();
        table.Title = new TableTitle($"[bold]{Markup.Escape(title)}[/]");
        table.AddColumn("Name");
        table.AddColumn("Author");
        table.AddColumn("Kind");
        table.AddColumn("Enabled");
        table.AddColumn("PURL");
        foreach (var package in packages)
        {
            table.AddMarkupRow(
                CliOutput.FormatValue(package.ProjectName),
                CliOutput.FormatValue(package.Author),
                package.Kind?.ToString() is string k
                    ? CliOutput.FormatStatus(k, "blue")
                    : "[dim]-[/]",
                CliOutput.FormatBoolean(package.Enabled, "enabled", "disabled"),
                Markup.Escape(package.Purl)
            );
        }

        return table;
    }

    public static Table CreateDependencyTable(
        string title,
        IEnumerable<IDependencyTableRow> dependencies
    )
    {
        var table = new Table().RoundedBorder();
        table.Title = new TableTitle($"[bold]{Markup.Escape(title)}[/]");
        table.AddColumn("PURL");
        table.AddColumn("Required");
        foreach (var dependency in dependencies)
        {
            table.AddMarkupRow(
                Markup.Escape(dependency.Purl),
                CliOutput.FormatBoolean(dependency.IsRequired, "required", "optional")
            );
        }

        return table;
    }
}

public interface IPackageTableRow
{
    string Purl { get; }
    bool Enabled { get; }
    string? ProjectName { get; }
    string? Author { get; }
    ResourceKind? Kind { get; }
}

public interface IDependencyTableRow
{
    string Purl { get; }
    bool IsRequired { get; }
}
