using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TridentCore.Abstractions.Extensions;
using TridentCore.Core.Facilities;
using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Services.Profiles;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Services;

public class ProfileManager : IDisposable
{
    internal readonly IList<ReservedKey> ReservedKeys = [];

    #region Injected

    private readonly ILogger<ProfileManager> _logger;

    #endregion

    private readonly List<ProfileHandle> _profiles = [];

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
            FileTransaction.EnsureInstanceReady(PathDef.Default.DirectoryOfHome(key));
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

        while (_profiles.Any(x => x.Key == sanitized) || ReservedKeys.Any(x => x.Key == sanitized)
            || Path.Exists(PathDef.Default.DirectoryOfHome(sanitized)))
        {
            sanitized += '_';
        }

        var rv = new ReservedKey(sanitized, this);
        ReservedKeys.Add(rv);
        return rv;
    }

    public void Add(ReservedKey key, Profile profile)
    {
        using (key)
        {
            var prepared = PrepareAdd(key, profile);
            prepared.Handle.Save();
            prepared.Publish();
            prepared.Notify();
        }
    }

    internal PreparedAddition PrepareAdd(ReservedKey key, Profile profile) => new(this, key,
        new ProfileHandle(key.Key, profile, FileHelper.SerializerOptions));

    internal sealed class PreparedAddition(ProfileManager owner, ReservedKey key, ProfileHandle handle)
    {
        public ProfileHandle Handle => handle;
        public void Publish()
        {
            if (!owner.ReservedKeys.Contains(key) || owner._profiles.Any(x => x.Key == key.Key))
                throw new InvalidOperationException($"Instance key '{key.Key}' is no longer reserved for this installation");
            owner._profiles.Add(handle);
        }
        public void Notify() => owner.OnProfileAdded(key.Key, handle.Value);
    }

    public void Remove(string key)
    {
        var handle = _profiles.FirstOrDefault(x => x.Key == key);
        // WARNING: 幂等语义。重复触发（连点删除）、并发移除、导航过期状态下 key 可能已不在，
        //  抛异常会冒泡到全局 Dispatcher handler 导致崩溃（POLYMERIUM-23）。删除一个不存在的东西视为已删除。
        if (handle is null)
        {
            return;
        }

        // WARNING: 废掉 handle，避免外部仍握着的 ProfileGuard 在 Dispose/Notify 时写回 profile 或再发 ProfileUpdated。
        handle.IsActive = false;
        _profiles.Remove(handle);

        _logger.LogInformation("{} removed", key);
        OnProfileRemoved(key, handle.Value);
    }

    internal PreparedUpdate PrepareUpdate(string key, Profile incoming)
    {
        incoming = incoming.Clone();
        var handle = _profiles.FirstOrDefault(x => x.Key == key)
            ?? throw new InvalidOperationException($"{key} is not in profiles");
        var original = JsonSerializer.Serialize(handle.Value, FileHelper.SerializerOptions);
        var updated = handle.Value.Clone();
        var source = incoming.Setup.Source;
        var changeSet = incoming.Setup.Packages.Select(x => x.Pref).ToDictionary(PackageHelper.ExtractProjectIdentityIfValid);
        var removeSet = new List<Profile.Rice.Entry>();
        foreach (var entry in updated.Setup.Packages.Where(x => x.Source == updated.Setup.Source))
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
            updated.Setup.Packages.Remove(remove);
        }

        foreach (var add in changeSet.Values)
        {
            updated.Setup.Packages.Add(new() { Enabled = true, Source = source, Pref = add });
        }

        foreach (var (k, v) in incoming.Overrides)
        {
            updated.Overrides[k] = v;
        }
        updated.Name = incoming.Name;
        updated.Setup.Source = source;
        updated.Setup.Version = incoming.Setup.Version;
        updated.Setup.Loader = incoming.Setup.Loader;
        return new(this, handle, original, updated);
    }

    internal sealed class PreparedUpdate(ProfileManager owner, ProfileHandle original, string fingerprint, Profile value)
    {
        private readonly ProfileHandle _replacement = new(original.Key, value, FileHelper.SerializerOptions);
        public Profile Value => value;

        public void Publish()
        {
            var index = owner._profiles.IndexOf(original);
            if (index < 0 || !original.IsActive
             || JsonSerializer.Serialize(original.Value, FileHelper.SerializerOptions) != fingerprint)
                throw new InvalidOperationException($"Profile '{original.Key}' changed while the update was being prepared");
            owner._profiles[index] = _replacement;
            original.IsActive = false;
        }

        public void Notify() => owner.OnProfileUpdated(original.Key, value);
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

    internal void OnProfileUpdated(string key, Profile profile) => Notify(ProfileUpdated, key, profile);
    internal void OnProfileRemoved(string key, Profile profile) => Notify(ProfileRemoved, key, profile);
    internal void OnProfileAdded(string key, Profile profile) => Notify(ProfileAdded, key, profile);

    private void Notify(EventHandler<ProfileChangedEventArgs>? handlers, string key, Profile profile)
    {
        // NOTE: Mutating a notification payload must not change committed state or another subscriber's snapshot.
        foreach (var subscriber in handlers?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler<ProfileChangedEventArgs>)subscriber)(this, new(key, profile.Clone()));
            }
            catch (Exception error)
            {
                _logger.LogError(error, "Profile notification subscriber failed for {key}", key);
            }
        }
    }

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
            try
            {
                x.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                _logger.LogError(error, "Could not save profile {key} while closing", x.Key);
            }
        }

        _profiles.Clear();
    }

    #endregion
}
