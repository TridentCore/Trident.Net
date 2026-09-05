using System.Text;
using System.Text.Json;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.LaunchPlans;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Models.MultiMcPack;

namespace TridentCore.Core.Utilities;

public static class MultiMcPatchConverter
{
    public static ConversionResult Convert(string uid, MmcPatch patch)
    {
        var operations = new List<LaunchPlanDocument.Operation>();
        var diagnostics = new List<LaunchPlanDiagnostic>();

        if (!string.IsNullOrWhiteSpace(patch.MainClass))
        {
            operations.Add(new LaunchPlanDocument.SetMainClassOperation(patch.MainClass));
        }

        if (patch.MinecraftArguments is not null)
        {
            operations.Add(new LaunchPlanDocument.SetGameArgumentsOperation(SplitArguments(patch.MinecraftArguments)));
        }

        foreach (var argument in patch.JvmArguments ?? [])
        {
            operations.Add(new LaunchPlanDocument.AppendJavaArgumentOperation(argument));
        }

        foreach (var tweaker in patch.Tweakers ?? [])
        {
            operations.Add(new LaunchPlanDocument.AppendGameArgumentOperation("--tweakClass"));
            operations.Add(new LaunchPlanDocument.AppendGameArgumentOperation(tweaker));
            diagnostics.Add(new(LaunchPlanDiagnostic.Kind.Warning,
                                $"MMC patch '{uid}' tweakers were converted to game arguments."));
        }

        if (patch.Traits is { Count: > 0 })
        {
            diagnostics.Add(new(LaunchPlanDiagnostic.Kind.Warning,
                                $"MMC patch '{uid}' traits are not represented by the native launch plan."));
        }

        if (patch.CompatibleJavaMajors is { Count: > 0 })
        {
            operations.Add(new LaunchPlanDocument.SetJavaMajorVersionOperation(patch.CompatibleJavaMajors[0]));
            if (patch.CompatibleJavaMajors.Count > 1)
            {
                diagnostics.Add(new(LaunchPlanDiagnostic.Kind.Warning,
                                    $"MMC patch '{uid}' has multiple compatible Java majors; the first was selected."));
            }
        }

        if (patch.AssetIndex is { } assetIndex)
        {
            operations.Add(new LaunchPlanDocument.SetAssetIndexOperation(new(assetIndex.Id,
                                                                              assetIndex.Url,
                                                                              FileHash.FromSha1(assetIndex.Sha1))));
        }

        foreach (var library in (patch.Libraries ?? []).Concat(patch.AdditionalLibraries ?? []))
        {
            if (!TryConvertLibrary(library, out var converted, out var diagnostic))
            {
                diagnostics.Add(new(LaunchPlanDiagnostic.Kind.Warning, diagnostic!));
                continue;
            }

            operations.Add(new LaunchPlanDocument.AddLibraryOperation(converted!));
        }

        return new(new() { Operations = operations }, diagnostics);
    }

    public static byte[] Serialize(LaunchPlanDocument document) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, FileHelper.SerializerOptions));

    private static bool TryConvertLibrary(
        MmcPatch.MmcLibrary library,
        out LockData.Library? converted,
        out string? diagnostic)
    {
        converted = null;
        diagnostic = null;
        LockData.Library.Identity identity;
        try
        {
            identity = LibraryHelper.ParseIdentity(library.Name);
        }
        catch (Exception e) when (e is FormatException or NotSupportedException)
        {
            diagnostic = $"MMC library '{library.Name}' has an unsupported identity.";
            return false;
        }

        var artifact = library.Downloads?.Artifact;
        var url = library.Url ?? artifact?.Url;
        if (url is null)
        {
            diagnostic = $"MMC library '{library.Name}' has no download URL.";
            return false;
        }

        converted = new(identity,
                         url,
                         artifact is null ? null : FileHash.FromSha1(artifact.Sha1),
                         library.Natives is not null);
        return true;
    }

    private static IReadOnlyList<string> SplitArguments(string value)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var quote = '\0';
        var escaping = false;
        foreach (var ch in value)
        {
            if (escaping)
            {
                builder.Append(ch);
                escaping = false;
                continue;
            }

            if (ch == '\\' && quote != '\0')
            {
                escaping = true;
                continue;
            }

            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }
                else
                {
                    builder.Append(ch);
                }

                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (builder.Length > 0)
                {
                    result.Add(builder.ToString());
                    builder.Clear();
                }
            }
            else
            {
                builder.Append(ch);
            }
        }

        if (escaping)
        {
            builder.Append('\\');
        }

        if (builder.Length > 0)
        {
            result.Add(builder.ToString());
        }

        return result;
    }

    public sealed record ConversionResult(LaunchPlanDocument Document, IReadOnlyList<LaunchPlanDiagnostic> Diagnostics);
}
