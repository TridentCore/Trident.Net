using System.Diagnostics.CodeAnalysis;
using Trident.Abstractions.FileModels;

namespace Trident.Abstractions.Extensions;

public static class ProfileExtensions
{
    #region Nested type: $extension

    extension(Profile profile)
    {
        public bool TryGetOverride<T>(string key, [MaybeNullWhen(false)] out T value, T? defaultValue = default)
        {
            if (profile.Overrides.TryGetValue(key, out var result) && result is T rv)
            {
                value = rv;
                return true;
            }

            value = defaultValue;
            return false;
        }

        public T? GetOverride<T>(string key, T? defaultValue = default) =>
            profile.TryGetOverride<T>(key, out var result) ? result : defaultValue;

        public void SetOverride<T>(string key, T? value)
        {
            if (value is null or "")
            {
                profile.Overrides.Remove(key);
            }
            else
            {
                profile.Overrides[key] = value;
            }
        }

        public void RemoveOverride(string key) => profile.Overrides.Remove(key);
    }

    #endregion
}
