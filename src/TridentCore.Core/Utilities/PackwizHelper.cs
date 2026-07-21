using System.Diagnostics.CodeAnalysis;
using System.Text;
using Tomlyn;
using Tomlyn.Model;
using TridentCore.Abstractions.Repositories.Resources;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.GitHubApi;
using Version = TridentCore.Abstractions.Repositories.Resources.Version;

namespace TridentCore.Core.Utilities;

public static class PackwizHelper
{
    public const string LABEL = "packwiz";
    public const string INDEX_FILE_NAME = "pack.toml";

    private static readonly Dictionary<string, string> LOADER_KEYS = new()
    {
        ["fabric"] = LoaderHelper.LOADERID_FABRIC,
        ["forge"] = LoaderHelper.LOADERID_FORGE,
        ["neoforge"] = LoaderHelper.LOADERID_NEOFORGE,
        ["quilt"] = LoaderHelper.LOADERID_QUILT,
    };

    public record PackManifest(
        string? Name,
        string? Author,
        string Minecraft,
        (string Identity, string Version)? Loader
    );

    #region Parsing

    public static TomlTable Parse(string content) => TomlSerializer.Deserialize<TomlTable>(content)!;

    public static PackManifest ParsePackManifest(string content)
    {
        var toml = Parse(content);

        var name = GetString(toml, "name");
        var author = GetString(toml, "author");

        string minecraft = string.Empty;
        (string, string)? loader = null;
        if (TryGetTable(toml, "versions", out var versions))
        {
            minecraft = GetString(versions, "minecraft") ?? string.Empty;
            foreach (var (key, identity) in LOADER_KEYS)
            {
                if (GetString(versions, key) is { } version)
                {
                    loader = (identity, version);
                    break;
                }
            }
        }

        return new(name, author, minecraft, loader);
    }

    // A mod contributes a pref only when its .pw.toml carries an [update] block; a
    // [download]-only mod points at a direct URL that no repository tracks, so it is skipped.
    // modrinth wins over curseforge when both are present.
    public static string? TryExtractPref(TomlTable mod)
    {
        if (!TryGetTable(mod, "update", out var update))
            return null;

        if (TryGetTable(update, "modrinth", out var modrinth)
            && GetString(modrinth, "mod-id") is { } modId
            && GetString(modrinth, "version") is { } version)
        {
            return PackageHelper.ToPref(ModrinthHelper.LABEL, null, modId, version);
        }

        if (TryGetTable(update, "curseforge", out var curseforge)
            && GetString(curseforge, "project-id") is { } projectId
            && GetString(curseforge, "file-id") is { } fileId)
        {
            return PackageHelper.ToPref(CurseForgeHelper.LABEL, null, projectId, fileId);
        }

        return null;
    }

    public static bool IsServerOnly(TomlTable mod) => GetString(mod, "side") is "server";

    // GitHub Contents API returns the file body base64-encoded with a newline every 76 columns.
    public static string DecodeContent(FileContent file)
    {
        var raw = file.Content?.Replace("\n", string.Empty).Replace("\r", string.Empty) ?? string.Empty;
        return raw.Length == 0 ? string.Empty : Encoding.UTF8.GetString(Convert.FromBase64String(raw));
    }

    #endregion

    #region Mapping

    public static Project ToProject(
        string label,
        string owner,
        string repo,
        CommitObject head,
        PackManifest manifest,
        string summary,
        IReadOnlyList<string> tags
    )
    {
        var date = head.Commit?.Committer?.Date ?? DateTimeOffset.MinValue;
        return new(
            label,
            owner,
            repo,
            manifest.Name ?? repo,
            OwnerAvatar(owner),
            manifest.Author ?? owner,
            summary,
            new($"https://github.com/{owner}/{repo}"),
            ResourceKind.Modpack,
            tags,
            date,
            date,
            0,
            Array.Empty<Project.Screenshot>()
        );
    }

    public static Package ToPackage(
        string label,
        string owner,
        string repo,
        CommitObject commit,
        string? versionId,
        PackManifest manifest,
        string summary
    )
    {
        var sha = commit.Sha ?? string.Empty;
        return new(
            label,
            owner,
            repo,
            versionId ?? ShortSha(sha),
            manifest.Name ?? repo,
            $"{ShortSha(sha)} {FirstLine(commit.Commit?.Message ?? string.Empty)}".Trim(),
            OwnerAvatar(owner),
            manifest.Author ?? owner,
            summary,
            new($"https://github.com/{owner}/{repo}"),
            ResourceKind.Modpack,
            ReleaseType.Release,
            commit.Commit?.Committer?.Date ?? DateTimeOffset.MinValue,
            new($"https://codeload.github.com/{owner}/{repo}/zip/{sha}"),
            0,
            $"{repo}-{sha}.zip",
            null,
            new Requirement(Array.Empty<string>(), Array.Empty<string>()),
            Array.Empty<Dependency>()
        );
    }

    public static Version ToVersion(string label, string owner, string repo, CommitObject commit, string? versionId)
    {
        var sha = commit.Sha ?? string.Empty;
        return new(
            label,
            owner,
            repo,
            versionId ?? ShortSha(sha),
            $"{ShortSha(sha)} {FirstLine(commit.Commit?.Message ?? string.Empty)}".Trim(),
            ReleaseType.Release,
            commit.Commit?.Committer?.Date ?? DateTimeOffset.MinValue,
            0,
            new Requirement(Array.Empty<string>(), Array.Empty<string>()),
            Array.Empty<Dependency>()
        );
    }

    #endregion

    private static string? GetString(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static bool TryGetTable(TomlTable table, string key, [NotNullWhen(true)] out TomlTable? value)
    {
        if (table.TryGetValue(key, out var raw) && raw is TomlTable sub)
        {
            value = sub;
            return true;
        }

        value = null;
        return false;
    }

    private static Uri OwnerAvatar(string owner) =>
        new($"https://github.com/{owner}.png");

    private static string ShortSha(string sha) => sha.Length > 7 ? sha[..7] : sha;

    private static string FirstLine(string message) => message.Split('\n', 2)[0].Trim();
}
