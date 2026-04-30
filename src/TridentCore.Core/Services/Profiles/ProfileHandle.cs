using System.Text.Json;
using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;

namespace TridentCore.Core.Services.Profiles;

internal class ProfileHandle(string key, Profile value, JsonSerializerOptions options)
    : IAsyncDisposable
{
    public string Key => key;
    public Profile Value => value;

    internal bool IsActive { get; set; } = true;

    internal async Task SaveAsync()
    {
        if (!IsActive)
        {
            return;
        }

        var profilePath = PathDef.Default.FileOfProfile(key);
        var dir = Path.GetDirectoryName(profilePath);
        if (dir is not null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(Value, options);
        await File.WriteAllTextAsync(profilePath, json).ConfigureAwait(false);
    }

    public static ProfileHandle Create(string key, Profile value, JsonSerializerOptions options) =>
        new(key, value, options);

    public static ProfileHandle Create(string key, JsonSerializerOptions options)
    {
        var path = PathDef.Default.FileOfProfile(key);
        if (File.Exists(path))
        {
            var profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), options)!;
            return new(key, profile, options);
        }

        throw new FileNotFoundException("Profile not found");
    }

    #region Dispose

    private bool _isDisposing;

    public async ValueTask DisposeAsync()
    {
        if (_isDisposing)
        {
            return;
        }

        _isDisposing = true;

        await SaveAsync().ConfigureAwait(false);
    }

    #endregion
}
