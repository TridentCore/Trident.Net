using System.Diagnostics.CodeAnalysis;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Abstractions.Extensions;

public static class ProfileExtensions
{
    public static Profile.Rice Clone(this Profile.Rice self)
    {
        var rules = new List<Profile.Rice.Rule>(
            self.Rules.Select(x => new Profile.Rice.Rule()
            {
                Enabled = x.Enabled,
                Selector = x.Selector,
                Destination = x.Destination,
                Skipping = x.Skipping,
                Normalizing = x.Normalizing,
            })
        );
        var packages = new List<Profile.Rice.Entry>(
            self.Packages.Select(x => new Profile.Rice.Entry
            {
                Enabled = x.Enabled,
                Purl = x.Purl,
                Source = x.Source,
                Tags = x.Tags,
            })
        );
        return new()
        {
            Version = self.Version,
            Loader = self.Loader,
            Source = self.Source,
            Packages = packages,
            Rules = self.Rules,
        };
    }

    #region Nested type: $extension

    extension(Profile profile)
    {
        public bool TryGetOverride<T>(
            string key,
            [MaybeNullWhen(false)] out T value,
            T? defaultValue = default
        )
        {
            if (profile.Overrides.TryGetValue(key, out var result) && result is T rv)
            {
                value = rv;
                return true;
            }

            if (result is not null && NumericConversionHelper.TryConvert(result, out T converted))
            {
                value = converted;
                return true;
            }

            value = defaultValue;
            return false;
        }

        public T? GetOverride<T>(string key, T? defaultValue = default) =>
            profile.TryGetOverride<T>(key, out var result) ? result : defaultValue;

        public void SetOverride<T>(string key, T? value)
        {
            if (value is null)
            {
                profile.Overrides.Remove(key);
            }
            else
            {
                profile.Overrides[key] = value;
            }
        }

        public void RemoveOverride(string key) => profile.Overrides.Remove(key);

        public Profile Clone()
        {
            var overrides = new Dictionary<string, object>(profile.Overrides);

            return new()
            {
                Name = profile.Name,
                Setup = profile.Setup.Clone(),
                Overrides = overrides,
            };
        }
    }

    #endregion
}
