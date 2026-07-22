using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Services.Profiles;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Services;

public class ProfileManager : IDisposable
{
    #region Injected

    private readonly ILogger<ProfileManager> _logger;

    #endregion

    private readonly List<ProfileHandle> _profiles = [];
    internal readonly IList<ReservedKey> ReservedKeys = [];

    public ProfileManager(ILogger<ProfileManager> logger)
    {
        _logger = logger;

        var dir = new DirectoryInfo(PathDef.Default.InstanceDirectory);
        if (!dir.Exists)
        {
            return;
        }

        foreach (var ins in dir.GetDirectories())
        {
            var path = PathDef.Default.FileOfProfile(ins.Name);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var bomb = PathDef.Default.FileOfBomb(ins.Name);
                if (File.Exists(bomb))
                {
                    ins.Delete(true);
                    continue;
                }

                var handle = ProfileHandle.Create(ins.Name, FileHelper.SerializerOptions);
                _profiles.Add(handle);
                logger.LogInformation("{} scanned", handle.Key);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to add profile {}", ins.Name);
            }
        }
    }

    public IEnumerable<(string, Profile)> Profiles => _profiles.Select(x => (x.Key, x.Value));

    public bool TryGetMutable(string key, [MaybeNullWhen(false)] out ProfileGuard profile)
    {
        var handle = _profiles.FirstOrDefault(x => x.Key == key);
        if (handle is not null)
        {
            profile = new(this, handle);
            return true;
        }

        profile = null;
        return false;
    }

    public bool TryGetImmutable(string key, [MaybeNullWhen(false)] out Profile profile)
    {
        var handle = _profiles.FirstOrDefault(x => x.Key == key);
        if (handle is not null)
        {
            profile = handle.Value;
            return true;
        }

        profile = null;
        return false;
    }

    public Profile GetImmutable(string key) =>
        TryGetImmutable(key, out var profile)
            ? profile
            : throw new KeyNotFoundException($"{key} is not a key to the managed profile");

    public ProfileGuard GetMutable(string key) =>
        TryGetMutable(key, out var profile)
            ? profile
            : throw new KeyNotFoundException($"{key} is not a key to the managed profile");

    public ReservedKey RequestKey(string key)
    {
        var sanitized = FileHelper.Sanitize(key).ToLower();

        while (_profiles.Any(x => x.Key == sanitized) || ReservedKeys.Any(x => x.Key == sanitized))
        {
            sanitized += '_';
        }

        var rv = new ReservedKey(sanitized, this);
        ReservedKeys.Add(rv);
        return rv;
    }

    public void Add(ReservedKey key, Profile profile)
    {
        var handle = new ProfileHandle(key.Key, profile, FileHelper.SerializerOptions);
        handle.Save();
        _profiles.Add(handle);
        key.Dispose();

        _logger.LogInformation("{} added", handle.Key);
        OnProfileAdded(key.Key, profile);
    }

    public void Remove(string key)
    {
        var handle = _profiles.FirstOrDefault(x => x.Key == key);
        // NOTE: 幂等语义。重复触发（连点删除）、并发移除、导航过期状态下 key 可能已不在，
        // 抛异常会冒泡到全局 Dispatcher handler 导致崩溃（POLYMERIUM-23）。删除一个不存在的东西视为已删除。
        if (handle is null)
        {
            return;
        }

        // NOTE: 废掉 handle，避免外部仍握着的 ProfileGuard 在 Dispose/Notify 时写回 profile 或再发 ProfileUpdated。
        handle.IsActive = false;
        _profiles.Remove(handle);

        _logger.LogInformation("{} removed", key);
        OnProfileRemoved(key, handle.Value);
    }

    public void Update(
        string key,
        string? source,
        string name,
        string version,
        string? loader,
        IReadOnlyList<string> packages,
        IDictionary<string, object> overrides)
    {
        var handle = _profiles.FirstOrDefault(x => x.Key == key);
        if (handle is null)
        {
            throw new InvalidOperationException($"{key} is not in profiles");
        }

        var changeSet = packages.ToDictionary(PackageHelper.ExtractProjectIdentityIfValid);
        var removeSet = new List<Profile.Rice.Entry>();
        foreach (var entry in handle.Value.Setup.Packages.Where(x => x.Source == handle.Value.Setup.Source))
        {
            var extracted = PackageHelper.ExtractProjectIdentityIfValid(entry.Pref);
            if (changeSet.TryGetValue(extracted, out var change))
            {
                entry.Pref = change;
                entry.Source = source;
                changeSet.Remove(extracted);
            }
            else
            {
                removeSet.Add(entry);
            }
        }

        foreach (var remove in removeSet)
        {
            handle.Value.Setup.Packages.Remove(remove);
        }

        foreach (var add in changeSet.Values)
        {
            handle.Value.Setup.Packages.Add(new() { Enabled = true, Source = source, Pref = add });
        }

        foreach (var (k, v) in overrides)
        {
            handle.Value.Overrides[k] = v;
        }

        handle.Value.Name = name;
        handle.Value.Setup.Source = source;
        handle.Value.Setup.Version = version;
        handle.Value.Setup.Loader = loader;

        handle.Save();
        _logger.LogInformation("{} updated", key);
        OnProfileUpdated(key, handle.Value);
    }

    #region Profile Changed Event

    public class ProfileChangedEventArgs(string key, Profile profile) : EventArgs
    {
        public string Key => key;
        public Profile Value => profile;
    }

    public event EventHandler<ProfileChangedEventArgs>? ProfileUpdated;

    public event EventHandler<ProfileChangedEventArgs>? ProfileRemoved;

    public event EventHandler<ProfileChangedEventArgs>? ProfileAdded;

    internal void OnProfileUpdated(string key, Profile profile) => ProfileUpdated?.Invoke(this, new(key, profile));

    internal void OnProfileRemoved(string key, Profile profile) => ProfileRemoved?.Invoke(this, new(key, profile));

    internal void OnProfileAdded(string key, Profile profile) => ProfileAdded?.Invoke(this, new(key, profile));

    #endregion

    #region Dispose

    private bool _isDisposing;

    public void Dispose()
    {
        if (_isDisposing)
        {
            return;
        }

        _isDisposing = true;

        foreach (var x in _profiles)
        {
            x.DisposeAsync().AsTask().Wait();
        }

        _profiles.Clear();
    }

    #endregion
}
