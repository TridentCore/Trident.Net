using TridentCore.Abstractions.Repositories;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.ModrinthApi;
using Version = TridentCore.Abstractions.Repositories.Resources.Version;

namespace TridentCore.Core.Utilities;

public static class ModrinthHelper
{
    public const string LABEL = "modrinth";
    public const string OFFICIAL_ENDPOINT = "https://api.modrinth.com";
    public const string FAKE_ENDPOINT = "https://api.bbsmc.net";
    private const string OFFICIAL_PROJECT_URL = "https://modrinth.com/{0}/{1}";

    public const string RESOURCENAME_MODPACK = "modpack";
    public const string RESOURCENAME_MOD = "mod";
    public const string RESOURCENAME_RESOURCEPACK = "resourcepack";
    public const string RESOURCENAME_SHADERPACK = "shader";
    public const string RESOURCENAME_DATAPACK = "datapack";

    public const string PACK_INDEX_FILE_NAME = "modrinth.index.json";

    public static readonly IReadOnlyDictionary<string, string> ModloaderMappings = new Dictionary<
        string,
        string
    >
    {
        ["forge"] = LoaderHelper.LOADERID_FORGE,
        ["neoforge"] = LoaderHelper.LOADERID_NEOFORGE,
        ["fabric"] = LoaderHelper.LOADERID_FABRIC,
        ["quilt"] = LoaderHelper.LOADERID_QUILT,
    };

    public static string? LoaderIdToName(string? id) =>
        id switch
        {
            LoaderHelper.LOADERID_FORGE => "forge",
            LoaderHelper.LOADERID_NEOFORGE => "neoforge",
            LoaderHelper.LOADERID_FABRIC => "fabric",
            LoaderHelper.LOADERID_QUILT => "quilt",
            _ => null,
        };

    public static string ResourceKindToUrlKind(ResourceKind kind) =>
        kind switch
        {
            ResourceKind.MODPACK => RESOURCENAME_MODPACK,
            ResourceKind.MOD => RESOURCENAME_MOD,
            ResourceKind.RESOURCE_PACK => RESOURCENAME_RESOURCEPACK,
            ResourceKind.SHADER_PACK => RESOURCENAME_SHADERPACK,
            ResourceKind.DATA_PACK => RESOURCENAME_DATAPACK,
            _ => "unknown",
        };

    public static string? ResourceKindToType(ResourceKind? kind) =>
        kind switch
        {
            ResourceKind.MODPACK => RESOURCENAME_MODPACK,
            ResourceKind.MOD => RESOURCENAME_MOD,
            ResourceKind.RESOURCE_PACK => RESOURCENAME_RESOURCEPACK,
            ResourceKind.SHADER_PACK => RESOURCENAME_SHADERPACK,
            ResourceKind.DATA_PACK => RESOURCENAME_DATAPACK,
            _ => null,
        };

    public static ResourceKind? ProjectTypeToKind(string? kind) =>
        kind switch
        {
            RESOURCENAME_MODPACK => ResourceKind.MODPACK,
            RESOURCENAME_MOD => ResourceKind.MOD,
            RESOURCENAME_RESOURCEPACK => ResourceKind.RESOURCE_PACK,
            RESOURCENAME_SHADERPACK => ResourceKind.SHADER_PACK,
            RESOURCENAME_DATAPACK => ResourceKind.DATA_PACK,
            _ => null,
        };

    public static ReleaseType VersionTypeToReleaseType(string type) =>
        type switch
        {
            "release" => ReleaseType.RELEASE,
            "beta" => ReleaseType.BETA,
            "alpha" => ReleaseType.ALPHA,
            _ => ReleaseType.RELEASE,
        };

    public static Requirement ToRequirement(VersionInfo version) =>
        new(
            version.GameVersions,
            [
                .. version
                    .Loaders.Select(x => ModloaderMappings.GetValueOrDefault(x))
                    .Where(x => !string.IsNullOrEmpty(x))
                    .Select(x => x!),
            ]
        );

    public static IReadOnlyList<Dependency> ToDependencies(string label, VersionInfo version) =>
        [
            .. version.Dependencies.Select(x => new Dependency(
                label,
                null,
                x.ProjectId,
                x.VersionId,
                x.DependencyType != "optional"
            )),
        ];

    public static Exhibit ToExhibit(string label, SearchHit hit) =>
        new(
            label,
            null,
            hit.ProjectId,
            hit.Title,
            hit.IconUrl,
            hit.Author,
            hit.Description,
            ProjectTypeToKind(hit.ProjectType) ?? ResourceKind.UNKNOWN,
            hit.Downloads,
            hit.Categories,
            new(OFFICIAL_PROJECT_URL.Replace("{0}", hit.ProjectType).Replace("{1}", hit.Slug)),
            hit.DateCreated,
            hit.DateModified
        );

    public static Version ToVersion(string label, VersionInfo version) =>
        new(
            label,
            null,
            version.ProjectId,
            version.Id,
            version.VersionNumber,
            VersionTypeToReleaseType(version.VersionType),
            version.DatePublished,
            version.Downloads,
            ToRequirement(version),
            ToDependencies(label, version)
        );

    public static Project ToProject(string label, ProjectInfo project, MemberInfo? member)
    {
        var extracted = project.ProjectTypes.FirstOrDefault();
        var kind = ProjectTypeToKind(extracted) ?? ResourceKind.UNKNOWN;
        return new(
            label,
            null,
            project.Id,
            project.Name,
            project.IconUrl,
            member?.User.Name ?? member?.User.Username ?? project.TeamId,
            project.Summary,
            new(
                OFFICIAL_PROJECT_URL
                    .Replace("{0}", extracted ?? "unknown")
                    .Replace("{1}", project.Slug)
            ),
            kind,
            project.Categories,
            project.Published,
            project.Updated,
            project.Downloads,
            [.. project.Gallery.Select(x => new Project.Screenshot(x.Name, x.Url))]
        );
    }

    public static Package ToPackage(
        string label,
        ProjectInfo project,
        VersionInfo version,
        MemberInfo? member
    )
    {
        var extracted = project.ProjectTypes.FirstOrDefault();
        var kind = ProjectTypeToKind(extracted) ?? ResourceKind.UNKNOWN;
        var file =
            version.Files.FirstOrDefault(x => x.Primary)
            ?? version.Files.FirstOrDefault()
            ?? throw new ResourceNotFoundException(
                $"{project.Id}/{version.Id} has no file available"
            );
        return new(
            label,
            null,
            project.Id,
            version.Id,
            project.Name,
            version.VersionNumber,
            project.IconUrl,
            member?.User.Name ?? member?.User.Username ?? project.TeamId,
            project.Summary,
            new(
                OFFICIAL_PROJECT_URL
                    .Replace("{0}", extracted ?? "unknown")
                    .Replace("{1}", project.Slug)
            ),
            kind,
            VersionTypeToReleaseType(version.VersionType),
            version.DatePublished,
            file.Url,
            file.Size,
            file.Filename,
            file.Hashes.Sha1,
            ToRequirement(version),
            ToDependencies(label, version)
        );
    }

    public static IReadOnlyList<string> ToLoaderNames(IEnumerable<ModLoader> loaders) =>
        [.. loaders.Select(x => x.Name)];

    public static IReadOnlyList<string> ToVersionNames(IEnumerable<GameVersion> versions) =>
        [.. versions.Where(x => x.VersionType == "release").Select(x => x.Version)];

    public static string BuildFacets(string? projectType, string? gameVersion, string? modLoader)
    {
        var facets = new List<KeyValuePair<string, string>>();
        if (gameVersion != null)
        {
            facets.Add(new("versions", gameVersion));
        }

        if (modLoader != null)
        {
            facets.Add(new("categories", modLoader));
        }

        if (projectType != null)
        {
            facets.Add(new("project_type", projectType));
        }

        return "[" + string.Join(",", facets.Select(x => $"[\"{x.Key}:{x.Value}\"]")) + "]";
    }
}
