using Spectre.Console;
using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;
using TridentCore.Cli.Services;
using TridentCore.Pref;
using Profile = TridentCore.Abstractions.FileModels.Profile;

namespace TridentCore.Cli.Utilities;

public static class PackageCliHelper
{
    public static PackageIdentifier ParsePref(string pref) =>
        PackageHelper.TryParse(pref, out var parsed)
            ? parsed
            : throw new CliException($"'{pref}' is not a valid package pref.", ExitCodes.USAGE);

    public static Filter BuildFilter(
        string? gameVersion,
        string? loader,
        ResourceKind? kind,
        ResolvedInstanceContext? instance = null)
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
                   : throw new CliException($"Package kind '{kind}' is not supported.", ExitCodes.USAGE);
    }

    public static Profile.Rice.Entry FindEntry(Profile profile, string pref)
    {
        var parsed = ParsePref(pref);
        return profile.Setup.Packages.FirstOrDefault(x => PackageHelper.IsMatched(x.Pref,
                                                         parsed.Repository,
                                                         parsed.Namespace,
                                                         parsed.Identity))
            ?? throw new CliException($"Package '{pref}' is not installed.", ExitCodes.NOT_FOUND);
    }

    public static bool ContainsProject(Profile profile, string pref)
    {
        var parsed = ParsePref(pref);
        return profile.Setup.Packages.Any(x => PackageHelper.IsMatched(x.Pref,
                                                                       parsed.Repository,
                                                                       parsed.Namespace,
                                                                       parsed.Identity));
    }

    public static string ToPref(Dependency dependency) =>
        PackageHelper.ToPref(dependency.Label, dependency.Namespace, dependency.ProjectId, dependency.VersionId);

    public static Table CreatePackageTable(string title, IEnumerable<IPackageTableRow> packages)
    {
        var table = new Table().RoundedBorder();
        table.Title = new($"[bold]{Markup.Escape(title)}[/]");
        table.AddColumn("Name");
        table.AddColumn("Author");
        table.AddColumn("Kind");
        table.AddColumn("Enabled");
        table.AddColumn("PREF");
        foreach (var package in packages)
        {
            table.AddMarkupRow(CliOutput.FormatValue(package.ProjectName),
                               CliOutput.FormatValue(package.Author),
                               package.Kind?.ToString() is string k ? CliOutput.FormatStatus(k, "blue") : "[dim]-[/]",
                               CliOutput.FormatBoolean(package.Enabled, "enabled", "disabled"),
                               Markup.Escape(package.Pref));
        }

        return table;
    }

    public static Table CreateDependencyTable(string title, IEnumerable<IDependencyTableRow> dependencies)
    {
        var table = new Table().RoundedBorder();
        table.Title = new($"[bold]{Markup.Escape(title)}[/]");
        table.AddColumn("PREF");
        table.AddColumn("Required");
        foreach (var dependency in dependencies)
        {
            table.AddMarkupRow(Markup.Escape(dependency.Pref),
                               CliOutput.FormatBoolean(dependency.IsRequired, "required", "optional"));
        }

        return table;
    }
}

public interface IPackageTableRow
{
    string Pref { get; }
    bool Enabled { get; }
    string? ProjectName { get; }
    string? Author { get; }
    ResourceKind? Kind { get; }
}

public interface IDependencyTableRow
{
    string Pref { get; }
    bool IsRequired { get; }
}
