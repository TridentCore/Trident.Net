using System.Runtime.InteropServices;
using System.Text.Json;
using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Core.Engines.Deploying;

namespace TridentCore.Core.Utilities;

public static class LockValidationHelper
{
    public static string VanillaInput(Profile.Rice setup, PatchSet patches) => PatchHelper.Fingerprint(new
    {
        Format = LockData.FORMAT,
        setup.Version,
        Platform = RuntimeInformation.RuntimeIdentifier,
        Architecture = RuntimeInformation.OSArchitecture,
        OsVersion = Environment.OSVersion.VersionString,
        Patch = patches.Fingerprint("vanilla")
    });

    public static string LoaderInput(Profile.Rice setup, PatchSet patches, LockData.ArtifactData input) =>
        PatchHelper.Fingerprint(new
        {
            Format = LockData.FORMAT,
            setup.Loader,
            setup.Version,
            Input = PatchHelper.Fingerprint(input),
            Patch = patches.Fingerprint("loader")
        });

    public static string LaunchInput(PatchSet patches, LockData.ArtifactData input) => PatchHelper.Fingerprint(new
    {
        Format = LockData.FORMAT,
        Input = PatchHelper.Fingerprint(input),
        Patch = patches.Fingerprint("launch")
    });

    public static string PackagesInput(Profile.Rice setup) => PatchHelper.Fingerprint(new
    {
        setup.Version,
        setup.Loader,
        setup.Source,
        setup.SourceOrders,
        Packages = setup.Packages.Where(x => x.Enabled),
        Rules = setup.Rules.Where(x => x.Enabled)
    });

    public static async Task<LockData?> ReadAsync(string key, CancellationToken token = default)
    {
        try
        {
            await using var stream = File.OpenRead(PathDef.Default.FileOfLockData(key));
            return await JsonSerializer.DeserializeAsync<LockData>(stream, JsonSerializerOptions.Web, token).ConfigureAwait(false);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (JsonException) { return null; }
    }

    public static async Task<bool> ValidateAsync(string key, Profile.Rice setup, LockData data, CancellationToken token = default)
    {
        if (data.Artifact is null || data.Vanilla is null || data.Loader is null || data.Launch is null
            || data.Platform != new LockData.PlatformData(setup.Version, setup.Loader)
            || data.PackagesInput != PackagesInput(setup)
            || data.RuntimeMajor != JavaHelper.SelectRuntimeMajor(data.Artifact.CompatibleJavaMajors))
        {
            return false;
        }

        var patches = await PatchStorageHelper.LoadAsync(key, token).ConfigureAwait(false);
        return data.Vanilla.Input == VanillaInput(setup, patches)
            && data.Loader.Input == LoaderInput(setup, patches, data.Vanilla.Output)
            && data.Launch.Input == LaunchInput(patches, data.Loader.Output)
            && PatchHelper.Fingerprint(data.Artifact) == PatchHelper.Fingerprint(data.Launch.Output);
    }
}
